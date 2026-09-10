# Codex 内容服务

该服务为内部版安装助手提供两类可配置内容：完成页上一张可关闭的推广卡，以及 DeepSeek 阶段可提前关闭、可重新打开的分步操作引导。服务不可用时客户端会隐藏推广并使用内置引导，不影响安装、配置或启动 Codex。

## 接口

- `GET /healthz`：健康检查。
- `GET /api/v1/ad/current`：返回当前有效推广；未启用或不在投放时间时返回 `204`。
- `GET /api/v1/guide`：返回当前有效分步引导；未启用时返回 `204`。
- `POST /api/v1/events`：汇总推广的 `impression` 或 `click`。
- `POST /api/admin/login`、`POST /api/admin/logout`：管理会话。
- `GET/PUT /api/admin/ad`：读取或保存推广配置。
- `GET/PUT /api/admin/guide`：读取或保存引导配置。
- `POST /api/admin/media`：上传推广或引导图片。
- `GET /api/admin/stats`：读取推广汇总数据。

引导支持 1—8 个有序步骤。每一步可配置标题、说明、完成条件、下一步提示、官方 HTTPS 操作按钮和可选图片。服务拒绝重复步骤 ID、非 HTTPS 链接、过长文本和无效 JSON；保存采用原子替换，失败不会覆盖上一份有效配置。

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

脚本会创建专用 `codex-ad` 系统用户、安装到 `/opt/codex-ad`、把数据保存在 `/var/lib/codex-ad`，并启用带 `NoNewPrivileges`、`ProtectSystem=strict`、`PrivateTmp` 的 systemd 服务。推广、引导和汇总数据分别原子写入数据目录；密码哈希保存在 root 可读的 `/etc/codex-ad/env`。

将 `deploy/nginx-location.conf` 中的 location 加入现有 HTTPS `server` 块。修改 Nginx 前应创建带时间戳的配置备份，必须在 `nginx -t` 成功后才能重载。公开事件接口关闭访问日志；服务自身只持久化按活动编号汇总的展示量和点击量，不保存 IP。

管理地址为 `https://www.qiuqiuqiu.top/xxx/codex-ad/admin/`。后台以“推广配置”和“操作引导”两个标签页管理内容，并提供步骤排序、图片上传和预览。登录会话有效期 8 小时，Cookie 使用 Secure、HttpOnly、SameSite=Strict，并要求 CSRF Token；同一来源登录每分钟最多尝试 5 次。图片仅接受 PNG、JPEG、WebP，最大 2 MB。
