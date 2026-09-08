# Windows 10 安装与彻底清理验收清单

测试环境：Windows 10 22H2 x64（build 19045），使用普通本地管理员账户（非 SID 末尾 `-500` 的内置 Administrator）。正式标记发布前，必须在真实 Windows 电脑上逐项记录通过/失败。

## 全新安装

- [ ] 解压发布 ZIP，并在同一目录放一个不属于发布包的 `keep-me.txt`。
- [ ] 双击 `CodexDeepSeekSetup.exe`，窗口可见，程序自动完成环境检查。
- [ ] 首页只显示安装操作；未提前显示 API Key 输入或完成页。
- [ ] 点击“下载并校验”，确认界面显示当前文件名、已下载/总大小、文件百分比、速度、预计剩余时间和总体进度。
- [ ] 展开详细记录，确认下载、校验等阶段带有本地时间，且日志中没有 API Key。
- [ ] 下载校验成功后“安装 Codex”才可点击，下载阶段没有 UAC 窗口。
- [ ] 点击“安装 Codex”后，提示明确说明接下来输入的是 Windows 管理员密码，不是 DeepSeek API Key。
- [ ] 在 UAC、部署、注册、CLI 和保存状态之间切换时，阶段标题和总体进度同步更新。
- [ ] 模拟一次安装失败后直接重试，确认没有再次下载 MSIX 或许可证。
- [ ] UAC 授权成功，下载、签名校验、系统部署、当前用户注册和 CLI 准备状态准确。
- [ ] 安装成功后自动进入第 2 步，进度条和安装警告不再显示。
- [ ] 输入错误 Key 时留在第 2 步，显示可执行的失败原因，界面和日志不显示 Key。
- [ ] 输入有效 DeepSeek Key 并完成模型验证后，输入框清空并自动进入第 3 步。
- [ ] 点击“启动 Codex”后出现可见 Codex 窗口，并能通过 DeepSeek 模型完成一次回复。

## 取消清理

- [ ] 点击“彻底卸载并清理”，删除预览包含 Codex、整个 `.codex`、DeepSeek 凭据、缓存/CLI/安装记录和安装助手自身。
- [ ] 第一个确认框默认选中“否”；点击“否”后任何内容都未删除。
- [ ] 再次确认后，在 Windows 管理员授权框点击取消；官方 Codex、`.codex`、DeepSeek 凭据和安装助手仍存在。

## 完整清理

- [ ] 再次点击“彻底卸载并清理”，确认不可恢复删除，并允许 Windows 管理员授权。
- [ ] `Get-AppxPackage -AllUsers -Name OpenAI.Codex` 无输出。
- [ ] `Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'OpenAI.Codex'` 无输出。
- [ ] Windows 凭据管理器中不存在 `CodexDeepSeekSetup/DeepSeekApiKey`。
- [ ] 如果安装前 `CODEX_CLI_PATH` 有非本助手值，该值已恢复；否则不再指向本助手管理目录。
- [ ] `%USERPROFILE%\.codex` 整个目录不存在。
- [ ] `%LOCALAPPDATA%\CodexDeepSeekSetup`、`%LOCALAPPDATA%\Programs\CodexDeepSeekSetup`、`%LOCALAPPDATA%\Programs\OpenAI\Codex`和 `%LOCALAPPDATA%\Programs\OpenAI\CodexPortable` 不存在。
- [ ] `%PUBLIC%\Documents\CodexDeepSeekSetup` 不存在。
- [ ] 主程序自动关闭，发布目录中的安装助手已知文件全部删除。
- [ ] 预先放入的 `keep-me.txt` 仍然存在，发布目录未被递归误删。
- [ ] `%TEMP%` 中本次生成的 `CodexDeepSeekCleanup-*.exe` 已自动删除。

## 记录

- 测试日期：
- Windows 版本/build：
- Codex 包版本：
- Codex CLI 版本：
- 发布 ZIP SHA-256：
- 失败项及事件日志：
