#!/usr/bin/env bash
set -euo pipefail

archive=${1:-/tmp/codex-ad-deploy.tar.gz}
site=/etc/nginx/sites-enabled/gkapp-www
work=$(mktemp -d)
env_file=$(mktemp)
cleanup() { rm -rf "$work" "$env_file"; }
trap cleanup EXIT

tar -xzf "$archive" -C "$work"
if ! id codex-ad >/dev/null 2>&1; then
  sudo useradd --system --home-dir /var/lib/codex-ad --shell /usr/sbin/nologin codex-ad
fi

sudo install -d -m 0755 /opt/codex-ad
sudo install -d -m 0750 -o codex-ad -g codex-ad /var/lib/codex-ad /var/lib/codex-ad/media
sudo install -d -m 0750 /etc/codex-ad
sudo install -m 0755 "$work/codex-ad-server" /opt/codex-ad/codex-ad-server

admin_password=$(openssl rand -hex 12)
password_hash=$(printf '%s\n' "$admin_password" | /opt/codex-ad/codex-ad-server hash-password)
printf '%s\n' \
  'CODEX_AD_DATA_DIR=/var/lib/codex-ad' \
  'CODEX_AD_LISTEN=127.0.0.1:8765' \
  'CODEX_AD_PUBLIC_BASE_URL=https://www.qiuqiuqiu.top/xxx/codex-ad' \
  "CODEX_AD_PASSWORD_HASH='$password_hash'" > "$env_file"
sudo install -m 0600 "$env_file" /etc/codex-ad/env
sudo install -m 0644 "$work/codex-ad.service" /etc/systemd/system/codex-ad.service
sudo install -m 0644 "$work/nginx-location.conf" /etc/nginx/snippets/codex-ad.conf

stamp=$(date +%Y%m%d-%H%M%S)
backup="${site}.backup-${stamp}"
sudo cp -a "$site" "$backup"
if ! sudo grep -q 'snippets/codex-ad.conf' "$site"; then
  sudo sed -i '/include \/etc\/nginx\/snippets\/ops-browser.conf;/a\    include /etc/nginx/snippets/codex-ad.conf;' "$site"
fi
if ! sudo nginx -t; then
  sudo cp -a "$backup" "$site"
  sudo nginx -t
  exit 1
fi

sudo systemctl daemon-reload
sudo systemctl enable --now codex-ad
healthy=false
for _ in $(seq 1 20); do
  if curl --fail --silent http://127.0.0.1:8765/healthz >/dev/null; then
    healthy=true
    break
  fi
  sleep 0.25
done
if [[ "$healthy" != true ]]; then
  sudo systemctl --no-pager --full status codex-ad
  exit 1
fi
sudo systemctl reload nginx
curl --fail --silent --show-error https://www.qiuqiuqiu.top/xxx/codex-ad/healthz >/dev/null

printf 'ADMIN_URL=https://www.qiuqiuqiu.top/xxx/codex-ad/admin/\n'
printf 'ADMIN_PASSWORD=%s\n' "$admin_password"
