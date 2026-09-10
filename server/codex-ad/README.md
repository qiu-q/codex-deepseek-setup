# Codex 推广服务

该服务只向内部版安装助手的完成页提供一张可关闭的推广卡，并提供带密码的管理页面。

## 本地测试

```bash
go test ./...
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

部署时将 `deploy/nginx-location.conf` 中的 location 加入现有 HTTPS `server` 块，并在 `nginx -t` 成功后重载。公开事件接口不记录访问日志；服务自身只持久化按活动编号汇总的展示量和点击量。
