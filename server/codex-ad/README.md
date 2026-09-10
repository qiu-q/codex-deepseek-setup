# Codex 推广服务

该服务只向内部版安装助手的完成页提供一张可关闭的推广卡，并提供带密码的管理页面。

## 本地测试

```bash
go test ./...
go vet ./...
CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath -ldflags="-s -w" -o codex-ad-server ./cmd/codex-ad-server
```

## 运行环境

服务默认监听 `127.0.0.1:8765`，需要以下环境变量：

```text
CODEX_AD_DATA_DIR=/var/lib/codex-ad
CODEX_AD_LISTEN=127.0.0.1:8765
CODEX_AD_PUBLIC_BASE_URL=https://www.qiuqiuqiu.top/xxx/codex-ad
CODEX_AD_PASSWORD_HASH=<bcrypt cost 12 hash>
```

使用标准输入生成密码哈希，避免密码出现在命令行参数中：

```bash
printf '%s\n' '至少二十位的随机密码' | ./codex-ad-server hash-password
```

首次部署可在服务器上用 root 执行：

```bash
sudo ./deploy/install.sh ./codex-ad-server '<bcrypt-hash>'
```

脚本会创建专用 `codex-ad` 系统用户、安装到 `/opt/codex-ad`、把数据保存在 `/var/lib/codex-ad`，并启用带 `NoNewPrivileges`、`ProtectSystem=strict`、`PrivateTmp` 的 systemd 服务。密码哈希保存在 root 可读的 `/etc/codex-ad/env`。

将 `deploy/nginx-location.conf` 中的 location 加入现有 HTTPS `server` 块。修改 Nginx 前应创建带时间戳的配置备份，必须在 `nginx -t` 成功后才能重载。公开事件接口关闭访问日志；服务自身只持久化按活动编号汇总的展示量和点击量，不保存 IP。

管理地址为 `https://www.qiuqiuqiu.top/xxx/codex-ad/admin/`。登录会话有效期 8 小时，Cookie 使用 Secure、HttpOnly、SameSite=Strict，并要求 CSRF Token；同一来源登录每分钟最多尝试 5 次。图片仅接受 PNG、JPEG、WebP，最大 2 MB。
