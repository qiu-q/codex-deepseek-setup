package main

import (
	"log"
	"net/http"
	"os"
	"time"

	"qiuqiuqiu.top/codex-ad/internal/adserver"
)

func main() {
	if len(os.Args) == 2 && os.Args[1] == "hash-password" {
		if err := adserver.HashPassword(os.Stdin, os.Stdout); err != nil {
			log.Fatal(err)
		}
		return
	}

	config := adserver.ServerConfig{
		DataDir:       envOr("CODEX_AD_DATA_DIR", "/var/lib/codex-ad"),
		PasswordHash:  os.Getenv("CODEX_AD_PASSWORD_HASH"),
		PublicBaseURL: envOr("CODEX_AD_PUBLIC_BASE_URL", "https://www.qiuqiuqiu.top/xxx/codex-ad"),
	}
	server, err := adserver.New(config)
	if err != nil {
		log.Fatal(err)
	}

	httpServer := &http.Server{
		Addr: envOr("CODEX_AD_LISTEN", "127.0.0.1:8765"), Handler: server.Handler(),
		ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 10 * time.Second,
		WriteTimeout: 15 * time.Second, IdleTimeout: 60 * time.Second,
	}
	log.Printf("codex-ad listening on %s", httpServer.Addr)
	log.Fatal(httpServer.ListenAndServe())
}

func envOr(name, fallback string) string {
	if value := os.Getenv(name); value != "" {
		return value
	}
	return fallback
}
