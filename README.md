# Codex + DeepSeek Windows 安装助手

这是一个中文 WPF 安装向导，目标是让 Windows 10 / Windows 11 x64 用户使用官方 Codex 桌面应用，并通过 DeepSeek 官方 API 使用模型。推荐 Windows 10 22H2（19045）或更新版本；19041—19044 可使用兼容尝试。

它会：

- 从 OpenAI 固定官方地址下载 `ChatGPT-x64.msix` 和离线许可证；
- 校验 MSIX 包身份、架构、内部签名文件以及 Windows Authenticode 状态；
- 经 UAC 授权调用 Windows AppX 部署命令；
- 官方离线部署失败时，可由用户明确选择实验性解包运行；
- 引导用户自行前往 DeepSeek 官方网站注册、实名认证、充值并创建 API Key；
- 调用 DeepSeek 官方接口验证 Key 和模型；
- 把 Key 保存到当前用户的 Windows 凭据管理器；
- 备份并合并 `%USERPROFILE%\.codex\config.toml`，保留已有 MCP、插件等无关配置；
- 若官方桌面包没有生成用户级 CLI runtime，则从已验证安装包复制同版本 `codex*.exe` 配套文件到当前用户目录，并设置 `CODEX_CLI_PATH`（不修改或重打包 MSIX）。
- 按“安装 Codex → 配置 DeepSeek → 完成”的三步向导自动推进，当前页只显示下一个应执行的操作。
- 在完成页提供需要两次明确确认的彻底清理功能。

它不会安装或配置 FlClash、代理、VPN、DNS、路由或节点订阅，也不包含 API Key。

## 使用

发布目录必须同时包含：

```text
CodexDeepSeekSetup.exe
CodexDeepSeekSetup.Helper.exe
```

双击 `CodexDeepSeekSetup.exe`，程序会自动检查电脑。按向导完成三步：

1. 下载并安装 Codex。安装时的系统弹窗需要输入 **Windows 管理员密码**，不是 DeepSeek API Key。只有官方离线部署失败时才会显示“实验性解包运行”。
2. 在 DeepSeek 官方平台准备 API Key，粘贴后点击“验证并配置 Codex”。
3. 在完成页启动 Codex，或关闭安装助手。

配置前请在 [DeepSeek 开放平台](https://platform.deepseek.com/)完成账户准备，并在 [API Keys](https://platform.deepseek.com/api_keys) 页面创建密钥。实名认证资料只应填写在 DeepSeek 官方页面。

## 彻底卸载与清理

完成页的“彻底卸载并清理”是不可恢复操作。确认后将删除检测到的 `OpenAI.Codex` 用户包和系统预配包、整个 `%USERPROFILE%\.codex`（包括配置、插件缓存和会话）、DeepSeek Windows 凭据、本助手创建的 CLI/便携目录/下载缓存/安装记录，最后删除安装助手自身。

移除系统预配包需要 Windows 管理员授权；如果取消该授权，程序不会继续删除用户数据。安装助手所在目录如果还有不属于发布包的文件，这些未知文件会被保留。

## 构建

需要 .NET 8 SDK。在 Windows PowerShell 中执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build\Publish-OpenSource.ps1
```

输出位于 `artifacts\open-source\win-x64`。

脚本生成的是未签名开发版。Windows 可能显示“未知发布者”或 SmartScreen 提示；对外分发前必须使用受信任的 Authenticode 证书签署两个 EXE。

内部离线版只允许把未修改的 OpenAI 官方文件放在仓库根目录的 `payload` 文件夹，再执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build\Publish-Internal.ps1
```

## 安全边界

- 下载主机被限制为 `persistent.oaistatic.com`，DeepSeek API 固定为 `https://api.deepseek.com/`。
- 提权助手只接受受限命令；它会在管理员上下文再次验证 MSIX。
- API Key 不进入 TOML、JSON、命令行参数、日志或构建产物。
- 清理目录由程序从 Windows 已知用户目录推导；安全检查会拒绝磁盘根目录、用户目录根、桌面和任意外部路径。
- 正式路径不解包运行；实验路径只解压通过官方签名和包身份校验的 MSIX，始终不重签名、不重新封包。
- 实验路径可能缺少自动更新、通知、协议关联或部分沙盒能力，不属于 OpenAI 官方支持的独立 EXE 安装方式。

## 当前验证状态

跨平台单元测试和 macOS 上的 Windows 交叉编译可用于开发验证；发布前仍必须按 [Windows 测试矩阵](docs/windows-test-matrix.md)在真实 Windows 10 和 Windows 11 上验证安装、启动及回滚。
