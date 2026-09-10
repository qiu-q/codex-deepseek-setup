package adserver

import (
	"bytes"
	"encoding/json"
	"io"
	"mime/multipart"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"golang.org/x/crypto/bcrypt"
)

func TestHealthAndDisabledCampaign(t *testing.T) {
	server := newTestServer(t)

	health := perform(t, server.Handler(), http.MethodGet, "/healthz", nil, nil)
	if health.Code != http.StatusOK || strings.TrimSpace(health.Body.String()) != "ok" {
		t.Fatalf("health = %d %q", health.Code, health.Body.String())
	}

	current := perform(t, server.Handler(), http.MethodGet, "/api/v1/ad/current", nil, nil)
	if current.Code != http.StatusNoContent {
		t.Fatalf("current status = %d, want 204", current.Code)
	}
}

func TestGuideIsDisabledByDefault(t *testing.T) {
	server := newTestServer(t)

	response := perform(t, server.Handler(), http.MethodGet, "/api/v1/guide", nil, nil)

	if response.Code != http.StatusNoContent {
		t.Fatalf("guide status = %d, want 204", response.Code)
	}
}

func TestGuideAdminUpdatePreservesStepOrderAndRequiresCsrf(t *testing.T) {
	server := newTestServer(t)
	handler := server.Handler()
	cookie, csrf := login(t, handler)
	payload := validGuideJSON()

	withoutCSRF := perform(t, handler, http.MethodPut, "/api/admin/guide", strings.NewReader(payload), []*http.Cookie{cookie})
	if withoutCSRF.Code != http.StatusForbidden {
		t.Fatalf("guide update without csrf = %d", withoutCSRF.Code)
	}

	request := httptest.NewRequest(http.MethodPut, "/api/admin/guide", strings.NewReader(payload))
	request.AddCookie(cookie)
	request.Header.Set("X-CSRF-Token", csrf)
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	if response.Code != http.StatusOK {
		t.Fatalf("guide update = %d %s", response.Code, response.Body.String())
	}

	current := perform(t, handler, http.MethodGet, "/api/v1/guide", nil, nil)
	if current.Code != http.StatusOK {
		t.Fatalf("public guide = %d %s", current.Code, current.Body.String())
	}
	var guide OnboardingGuide
	decode(t, current.Body, &guide)
	if len(guide.Steps) != 2 || guide.Steps[0].ID != "sign-in" || guide.Steps[1].ID != "api-key" {
		t.Fatalf("guide order = %#v", guide.Steps)
	}
}

func TestGuideInvalidUpdateDoesNotReplaceStoredDocument(t *testing.T) {
	server := newTestServer(t)
	handler := server.Handler()
	cookie, csrf := login(t, handler)
	putGuide(t, handler, cookie, csrf, validGuideJSON(), http.StatusOK)

	invalidDocuments := []string{
		`{"version":1,"enabled":true,"title":"Bad","steps":[]}`,
		`{"version":1,"enabled":true,"title":"Bad","steps":[{"id":"same","title":"One","body":"Body","completionHint":"Done","actionText":"Open","actionUrl":"https://example.com/1"},{"id":"same","title":"Two","body":"Body","completionHint":"Done","actionText":"Open","actionUrl":"https://example.com/2"}]}`,
		`{"version":1,"enabled":true,"title":"Bad","steps":[{"id":"one","title":"One","body":"Body","completionHint":"Done","actionText":"Open","actionUrl":"http://example.com"}]}`,
	}
	for _, payload := range invalidDocuments {
		putGuide(t, handler, cookie, csrf, payload, http.StatusBadRequest)
	}
	nineSteps := make([]GuideStep, 9)
	for index := range nineSteps {
		nineSteps[index] = validGuideStep("step-" + string(rune('a'+index)))
	}
	encoded, err := json.Marshal(OnboardingGuide{Version: 1, Enabled: true, Title: "Too many", Steps: nineSteps})
	if err != nil {
		t.Fatal(err)
	}
	putGuide(t, handler, cookie, csrf, string(encoded), http.StatusBadRequest)

	current := perform(t, handler, http.MethodGet, "/api/v1/guide", nil, nil)
	if current.Code != http.StatusOK || !strings.Contains(current.Body.String(), `"title":"DeepSeek API 使用准备"`) {
		t.Fatalf("stored guide was replaced: %d %s", current.Code, current.Body.String())
	}
}

func TestLoginAndCsrfProtectAdminUpdates(t *testing.T) {
	server := newTestServer(t)
	handler := server.Handler()

	bad := perform(t, handler, http.MethodPost, "/api/admin/login", strings.NewReader(`{"password":"wrong"}`), nil)
	if bad.Code != http.StatusUnauthorized {
		t.Fatalf("bad login status = %d", bad.Code)
	}

	login := perform(t, handler, http.MethodPost, "/api/admin/login", strings.NewReader(`{"password":"correct horse battery staple"}`), nil)
	if login.Code != http.StatusOK {
		t.Fatalf("login status = %d body=%s", login.Code, login.Body.String())
	}
	var session struct {
		CSRFToken string `json:"csrfToken"`
	}
	decode(t, login.Body, &session)
	cookie := login.Result().Cookies()[0]
	if !cookie.Secure || !cookie.HttpOnly || cookie.SameSite != http.SameSiteStrictMode {
		t.Fatalf("insecure session cookie: %#v", cookie)
	}

	ad := validAdJSON()
	withoutCSRF := perform(t, handler, http.MethodPut, "/api/admin/ad", strings.NewReader(ad), []*http.Cookie{cookie})
	if withoutCSRF.Code != http.StatusForbidden {
		t.Fatalf("update without csrf = %d", withoutCSRF.Code)
	}

	request := httptest.NewRequest(http.MethodPut, "/api/admin/ad", strings.NewReader(ad))
	request.AddCookie(cookie)
	request.Header.Set("X-CSRF-Token", session.CSRFToken)
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	if response.Code != http.StatusOK {
		t.Fatalf("update status = %d body=%s", response.Code, response.Body.String())
	}

	current := perform(t, handler, http.MethodGet, "/api/v1/ad/current", nil, nil)
	if current.Code != http.StatusOK || !strings.Contains(current.Body.String(), `"campaignId":"campaign-1"`) {
		t.Fatalf("current = %d %s", current.Code, current.Body.String())
	}
}

func TestExpiredCampaignIsNotPublic(t *testing.T) {
	server := newTestServer(t)
	server.store.ad = Advertisement{
		Version: 1, Enabled: true, CampaignID: "ended", Title: "t", Body: "b", CTAText: "go",
		TargetURL: "https://example.com", EndsAt: ptrTime(time.Date(2026, 9, 1, 0, 0, 0, 0, time.UTC)),
	}

	current := perform(t, server.Handler(), http.MethodGet, "/api/v1/ad/current", nil, nil)
	if current.Code != http.StatusNoContent {
		t.Fatalf("expired campaign status = %d", current.Code)
	}
}

func TestEventsAreAggregatedWithoutPersistingClientAddress(t *testing.T) {
	server := newTestServer(t)
	request := httptest.NewRequest(http.MethodPost, "/api/v1/events", strings.NewReader(`{"campaignId":"campaign-1","event":"click"}`))
	request.RemoteAddr = "203.0.113.44:54321"
	response := httptest.NewRecorder()
	server.Handler().ServeHTTP(response, request)
	if response.Code != http.StatusAccepted {
		t.Fatalf("event status = %d", response.Code)
	}

	contents, err := os.ReadFile(filepath.Join(server.store.dataDir, "stats.json"))
	if err != nil {
		t.Fatal(err)
	}
	if bytes.Contains(contents, []byte("203.0.113.44")) {
		t.Fatal("stats persisted client IP")
	}
	if !bytes.Contains(contents, []byte(`"clicks":1`)) {
		t.Fatalf("stats = %s", contents)
	}
}

func TestMediaUploadRejectsSvg(t *testing.T) {
	server := newTestServer(t)
	handler := server.Handler()
	cookie, csrf := login(t, handler)

	var body bytes.Buffer
	writer := multipart.NewWriter(&body)
	part, err := writer.CreateFormFile("image", "bad.svg")
	if err != nil {
		t.Fatal(err)
	}
	_, _ = io.WriteString(part, `<svg xmlns="http://www.w3.org/2000/svg"></svg>`)
	_ = writer.Close()
	request := httptest.NewRequest(http.MethodPost, "/api/admin/media", &body)
	request.AddCookie(cookie)
	request.Header.Set("Content-Type", writer.FormDataContentType())
	request.Header.Set("X-CSRF-Token", csrf)
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)

	if response.Code != http.StatusUnsupportedMediaType {
		t.Fatalf("svg upload status = %d body=%s", response.Code, response.Body.String())
	}
}

func TestMediaUploadRejectsFilesLargerThanTwoMegabytes(t *testing.T) {
	server := newTestServer(t)
	handler := server.Handler()
	cookie, csrf := login(t, handler)

	var body bytes.Buffer
	writer := multipart.NewWriter(&body)
	part, err := writer.CreateFormFile("image", "large.png")
	if err != nil {
		t.Fatal(err)
	}
	_, _ = part.Write(append([]byte("\x89PNG\r\n\x1a\n"), bytes.Repeat([]byte{0}, 2*1024*1024)...))
	_ = writer.Close()
	request := httptest.NewRequest(http.MethodPost, "/api/admin/media", &body)
	request.AddCookie(cookie)
	request.Header.Set("Content-Type", writer.FormDataContentType())
	request.Header.Set("X-CSRF-Token", csrf)
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)

	if response.Code != http.StatusRequestEntityTooLarge {
		t.Fatalf("oversized upload status = %d body=%s", response.Code, response.Body.String())
	}
}

func TestAdminPageIsEmbedded(t *testing.T) {
	server := newTestServer(t)
	response := perform(t, server.Handler(), http.MethodGet, "/admin/", nil, nil)
	page := response.Body.String()
	if response.Code != http.StatusOK || !strings.Contains(page, "Codex 内容管理") ||
		!strings.Contains(page, `id="adEditor"`) || !strings.Contains(page, `id="guideEditor"`) ||
		!strings.Contains(page, `id="guideSteps"`) {
		t.Fatalf("admin page = %d %s", response.Code, response.Body.String())
	}
}

func TestHashPasswordProducesBcryptCostTwelve(t *testing.T) {
	var output bytes.Buffer
	if err := HashPassword(strings.NewReader("a sufficiently long admin password\n"), &output); err != nil {
		t.Fatal(err)
	}
	hash := strings.TrimSpace(output.String())
	cost, err := bcrypt.Cost([]byte(hash))
	if err != nil {
		t.Fatal(err)
	}
	if cost != 12 {
		t.Fatalf("bcrypt cost = %d, want 12", cost)
	}
}

func newTestServer(t *testing.T) *Server {
	t.Helper()
	hash, err := bcrypt.GenerateFromPassword([]byte("correct horse battery staple"), bcrypt.MinCost)
	if err != nil {
		t.Fatal(err)
	}
	server, err := New(ServerConfig{
		DataDir:       t.TempDir(),
		PasswordHash:  string(hash),
		PublicBaseURL: "https://www.qiuqiuqiu.top/xxx/codex-ad",
		Now:           func() time.Time { return time.Date(2026, 9, 10, 0, 0, 0, 0, time.UTC) },
	})
	if err != nil {
		t.Fatal(err)
	}
	return server
}

func login(t *testing.T, handler http.Handler) (*http.Cookie, string) {
	t.Helper()
	response := perform(t, handler, http.MethodPost, "/api/admin/login", strings.NewReader(`{"password":"correct horse battery staple"}`), nil)
	var session struct {
		CSRFToken string `json:"csrfToken"`
	}
	decode(t, response.Body, &session)
	return response.Result().Cookies()[0], session.CSRFToken
}

func perform(t *testing.T, handler http.Handler, method, path string, body io.Reader, cookies []*http.Cookie) *httptest.ResponseRecorder {
	t.Helper()
	request := httptest.NewRequest(method, path, body)
	for _, cookie := range cookies {
		request.AddCookie(cookie)
	}
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	return response
}

func decode(t *testing.T, reader io.Reader, target any) {
	t.Helper()
	if err := json.NewDecoder(reader).Decode(target); err != nil {
		t.Fatal(err)
	}
}

func ptrTime(value time.Time) *time.Time { return &value }

func validAdJSON() string {
	return `{"version":1,"enabled":true,"campaignId":"campaign-1","title":"推广标题","body":"推广说明","ctaText":"了解详情","targetUrl":"https://example.com/product","imageUrl":"","startsAt":null,"endsAt":null}`
}

func validGuideJSON() string {
	return `{"version":1,"enabled":true,"title":"DeepSeek API 使用准备","steps":[{"id":"sign-in","title":"登录或注册","body":"登录开放平台。","completionHint":"已进入控制台","actionText":"打开登录页","actionUrl":"https://platform.deepseek.com/sign_in","imageUrl":"https://www.qiuqiuqiu.top/xxx/codex-ad/media/login.webp"},{"id":"api-key","title":"创建 API Key","body":"创建后立即复制。","completionHint":"已复制 Key","actionText":"打开 API Keys","actionUrl":"https://platform.deepseek.com/api_keys","imageUrl":""}]}`
}

func validGuideStep(id string) GuideStep {
	return GuideStep{ID: id, Title: "Title", Body: "Body", CompletionHint: "Done", ActionText: "Open", ActionURL: "https://example.com/" + id}
}

func putGuide(t *testing.T, handler http.Handler, cookie *http.Cookie, csrf, payload string, expectedStatus int) {
	t.Helper()
	request := httptest.NewRequest(http.MethodPut, "/api/admin/guide", strings.NewReader(payload))
	request.AddCookie(cookie)
	request.Header.Set("X-CSRF-Token", csrf)
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	if response.Code != expectedStatus {
		t.Fatalf("guide update status = %d want %d body=%s", response.Code, expectedStatus, response.Body.String())
	}
}
