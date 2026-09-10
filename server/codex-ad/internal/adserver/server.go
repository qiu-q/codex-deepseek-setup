package adserver

import (
	"crypto/rand"
	"crypto/subtle"
	"embed"
	"encoding/base64"
	"encoding/json"
	"errors"
	"io"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"golang.org/x/crypto/bcrypt"
)

const (
	sessionCookieName = "codex_ad_session"
	maximumBodyBytes  = 2*1024*1024 + 32*1024
	sessionLifetime   = 8 * time.Hour
)

//go:embed admin.html
var adminFiles embed.FS

type ServerConfig struct {
	DataDir       string
	PasswordHash  string
	PublicBaseURL string
	Now           func() time.Time
}

type session struct {
	CSRFToken string
	ExpiresAt time.Time
}

type loginWindow struct {
	Started time.Time
	Count   int
}

type Server struct {
	store         *dataStore
	passwordHash  []byte
	publicBaseURL string
	now           func() time.Time
	sessionsMu    sync.Mutex
	sessions      map[string]session
	loginLimitsMu sync.Mutex
	loginLimits   map[string]loginWindow
}

func New(config ServerConfig) (*Server, error) {
	if config.DataDir == "" || config.PasswordHash == "" || !isHTTPS(config.PublicBaseURL) {
		return nil, errors.New("data directory, password hash and HTTPS public base URL are required")
	}
	store, err := openStore(config.DataDir)
	if err != nil {
		return nil, err
	}
	now := config.Now
	if now == nil {
		now = time.Now
	}
	return &Server{
		store: store, passwordHash: []byte(config.PasswordHash),
		publicBaseURL: strings.TrimRight(config.PublicBaseURL, "/"), now: now,
		sessions: map[string]session{}, loginLimits: map[string]loginWindow{},
	}, nil
}

func (server *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", server.health)
	mux.HandleFunc("GET /api/v1/ad/current", server.currentAd)
	mux.HandleFunc("POST /api/v1/events", server.recordEvent)
	mux.HandleFunc("POST /api/admin/login", server.login)
	mux.HandleFunc("POST /api/admin/logout", server.requireSession(true, server.logout))
	mux.HandleFunc("GET /api/admin/ad", server.requireSession(false, server.adminAd))
	mux.HandleFunc("PUT /api/admin/ad", server.requireSession(true, server.adminAd))
	mux.HandleFunc("POST /api/admin/media", server.requireSession(true, server.uploadMedia))
	mux.HandleFunc("GET /api/admin/stats", server.requireSession(false, server.adminStats))
	mux.Handle("GET /media/", http.StripPrefix("/media/", http.FileServer(http.Dir(filepath.Join(server.store.dataDir, "media")))))
	mux.HandleFunc("GET /admin/", server.adminPage)
	return securityHeaders(mux)
}

func (server *Server) health(response http.ResponseWriter, _ *http.Request) {
	response.Header().Set("Content-Type", "text/plain; charset=utf-8")
	_, _ = io.WriteString(response, "ok\n")
}

func (server *Server) currentAd(response http.ResponseWriter, _ *http.Request) {
	ad := server.store.getAd()
	if !ad.active(server.now()) || ad.validate() != nil {
		response.WriteHeader(http.StatusNoContent)
		return
	}
	response.Header().Set("Cache-Control", "public, max-age=60")
	writeJSON(response, http.StatusOK, ad)
}

func (server *Server) recordEvent(response http.ResponseWriter, request *http.Request) {
	var event eventRequest
	if !decodeJSON(response, request, &event) || strings.TrimSpace(event.CampaignID) == "" ||
		len(event.CampaignID) > 80 || (event.Event != "impression" && event.Event != "click") {
		http.Error(response, "invalid event", http.StatusBadRequest)
		return
	}
	if err := server.store.record(event.CampaignID, event.Event); err != nil {
		http.Error(response, "unable to record event", http.StatusInternalServerError)
		return
	}
	response.WriteHeader(http.StatusAccepted)
}

func (server *Server) login(response http.ResponseWriter, request *http.Request) {
	if !server.allowLogin(request) {
		http.Error(response, "too many attempts", http.StatusTooManyRequests)
		return
	}
	var payload struct {
		Password string `json:"password"`
	}
	if !decodeJSON(response, request, &payload) ||
		bcrypt.CompareHashAndPassword(server.passwordHash, []byte(payload.Password)) != nil {
		http.Error(response, "invalid credentials", http.StatusUnauthorized)
		return
	}
	token, err := randomToken(32)
	if err != nil {
		http.Error(response, "unable to create session", http.StatusInternalServerError)
		return
	}
	csrf, err := randomToken(24)
	if err != nil {
		http.Error(response, "unable to create session", http.StatusInternalServerError)
		return
	}
	expires := server.now().Add(sessionLifetime)
	server.sessionsMu.Lock()
	server.sessions[token] = session{CSRFToken: csrf, ExpiresAt: expires}
	server.sessionsMu.Unlock()
	http.SetCookie(response, &http.Cookie{
		Name: sessionCookieName, Value: token, Path: "/", Expires: expires,
		Secure: true, HttpOnly: true, SameSite: http.SameSiteStrictMode,
	})
	writeJSON(response, http.StatusOK, map[string]string{"csrfToken": csrf})
}

func (server *Server) logout(response http.ResponseWriter, request *http.Request) {
	cookie, _ := request.Cookie(sessionCookieName)
	if cookie != nil {
		server.sessionsMu.Lock()
		delete(server.sessions, cookie.Value)
		server.sessionsMu.Unlock()
	}
	http.SetCookie(response, &http.Cookie{Name: sessionCookieName, Path: "/", MaxAge: -1, Secure: true, HttpOnly: true, SameSite: http.SameSiteStrictMode})
	response.WriteHeader(http.StatusNoContent)
}

func (server *Server) adminAd(response http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		writeJSON(response, http.StatusOK, server.store.getAd())
		return
	}
	var ad Advertisement
	if !decodeJSON(response, request, &ad) {
		return
	}
	if err := ad.validate(); err != nil {
		http.Error(response, err.Error(), http.StatusBadRequest)
		return
	}
	if err := server.store.setAd(ad); err != nil {
		http.Error(response, "unable to save advertisement", http.StatusInternalServerError)
		return
	}
	writeJSON(response, http.StatusOK, ad)
}

func (server *Server) adminStats(response http.ResponseWriter, _ *http.Request) {
	writeJSON(response, http.StatusOK, server.store.getStats())
}

func (server *Server) uploadMedia(response http.ResponseWriter, request *http.Request) {
	request.Body = http.MaxBytesReader(response, request.Body, maximumBodyBytes)
	if err := request.ParseMultipartForm(maximumBodyBytes); err != nil {
		http.Error(response, "image exceeds 2 MB", http.StatusRequestEntityTooLarge)
		return
	}
	file, _, err := request.FormFile("image")
	if err != nil {
		http.Error(response, "image is required", http.StatusBadRequest)
		return
	}
	defer file.Close()
	bytes, err := io.ReadAll(io.LimitReader(file, 2*1024*1024+1))
	if err != nil || len(bytes) > 2*1024*1024 {
		http.Error(response, "image exceeds 2 MB", http.StatusRequestEntityTooLarge)
		return
	}
	contentType := http.DetectContentType(bytes)
	extension := map[string]string{"image/png": ".png", "image/jpeg": ".jpg", "image/webp": ".webp"}[contentType]
	if extension == "" {
		http.Error(response, "only PNG, JPEG and WebP are allowed", http.StatusUnsupportedMediaType)
		return
	}
	name, err := randomToken(18)
	if err != nil {
		http.Error(response, "unable to save image", http.StatusInternalServerError)
		return
	}
	name += extension
	path := filepath.Join(server.store.dataDir, "media", name)
	if err := os.WriteFile(path, bytes, 0o640); err != nil {
		http.Error(response, "unable to save image", http.StatusInternalServerError)
		return
	}
	writeJSON(response, http.StatusCreated, map[string]string{"imageUrl": server.publicBaseURL + "/media/" + name})
}

func (server *Server) adminPage(response http.ResponseWriter, _ *http.Request) {
	contents, err := adminFiles.ReadFile("admin.html")
	if err != nil {
		http.Error(response, "admin page unavailable", http.StatusInternalServerError)
		return
	}
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	_, _ = response.Write(contents)
}

func (server *Server) requireSession(requireCSRF bool, next http.HandlerFunc) http.HandlerFunc {
	return func(response http.ResponseWriter, request *http.Request) {
		cookie, err := request.Cookie(sessionCookieName)
		if err != nil {
			http.Error(response, "authentication required", http.StatusUnauthorized)
			return
		}
		server.sessionsMu.Lock()
		session, found := server.sessions[cookie.Value]
		if found && !server.now().Before(session.ExpiresAt) {
			delete(server.sessions, cookie.Value)
			found = false
		}
		server.sessionsMu.Unlock()
		if !found {
			http.Error(response, "authentication required", http.StatusUnauthorized)
			return
		}
		if requireCSRF && subtle.ConstantTimeCompare([]byte(request.Header.Get("X-CSRF-Token")), []byte(session.CSRFToken)) != 1 {
			http.Error(response, "invalid csrf token", http.StatusForbidden)
			return
		}
		next(response, request)
	}
}

func (server *Server) allowLogin(request *http.Request) bool {
	host, _, err := net.SplitHostPort(request.RemoteAddr)
	if err != nil {
		host = request.RemoteAddr
	}
	now := server.now()
	server.loginLimitsMu.Lock()
	defer server.loginLimitsMu.Unlock()
	window := server.loginLimits[host]
	if window.Started.IsZero() || now.Sub(window.Started) >= time.Minute {
		server.loginLimits[host] = loginWindow{Started: now, Count: 1}
		return true
	}
	if window.Count >= 5 {
		return false
	}
	window.Count++
	server.loginLimits[host] = window
	return true
}

func decodeJSON(response http.ResponseWriter, request *http.Request, target any) bool {
	request.Body = http.MaxBytesReader(response, request.Body, maximumBodyBytes)
	decoder := json.NewDecoder(request.Body)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		http.Error(response, "invalid json", http.StatusBadRequest)
		return false
	}
	return true
}

func writeJSON(response http.ResponseWriter, status int, value any) {
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.WriteHeader(status)
	_ = json.NewEncoder(response).Encode(value)
}

func randomToken(size int) (string, error) {
	buffer := make([]byte, size)
	if _, err := rand.Read(buffer); err != nil {
		return "", err
	}
	return base64.RawURLEncoding.EncodeToString(buffer), nil
}

func securityHeaders(next http.Handler) http.Handler {
	return http.HandlerFunc(func(response http.ResponseWriter, request *http.Request) {
		response.Header().Set("X-Content-Type-Options", "nosniff")
		response.Header().Set("X-Frame-Options", "DENY")
		response.Header().Set("Referrer-Policy", "no-referrer")
		response.Header().Set("Content-Security-Policy", "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'")
		next.ServeHTTP(response, request)
	})
}
