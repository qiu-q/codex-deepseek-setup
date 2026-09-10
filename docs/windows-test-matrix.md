# Windows 发布测试矩阵

在 Windows 上先运行启动回归测试：

```powershell
dotnet test .\tests\CodexDeepSeekSetup.App.Windows.Tests\CodexDeepSeekSetup.App.Windows.Tests.csproj -c Release
```

未完成下表前，不得标记为 Windows 发布就绪。

| 系统 | 账户 | 下载位置 | 签名 | UAC 部署 | D 盘迁移 | 已装检测 | CLI | DeepSeek | 完成页/启动 | 推广断网降级 | 按项清理/自删 | 日志无 Key | 结果 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Windows 10 22H2 x64 build 19045 | 普通管理员 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |
| Windows 10 x64 build 19041—19044 | 普通管理员（兼容尝试） | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |
| Windows 10 x64 build 19041—19044 | 内置 Administrator（实验解包） | 待测 | 待测 | 不适用 | 不适用 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |
| Windows 11 x64 当前稳定版 | 普通管理员 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |

每次发布记录 Codex 包版本、Codex CLI 版本、测试日期、两个 EXE/ZIP 大小、SHA-256 和安装错误事件日志。开源版确认完成页完全没有推广；内部版确认推广可关闭、只有主动点击才打开浏览器、同一次流程展示只统计一次、超时/断网/无效 JSON/图片错误都不影响启动。重点复测：缺少 Microsoft Store、内置 Administrator（SID 末尾 `-500`）、UAC 被关闭、安装取消、D 盘不是 NTFS/D 盘空间不足、从 C 迁移到 D、无余额、错误 Key、接口不兼容、已有复杂 `config.toml`、恢复备份、只删除选中项、未知相邻文件保留、自删除。
