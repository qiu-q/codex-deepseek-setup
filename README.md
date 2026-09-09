# Codex + DeepSeek Windows 安装助手

这是一个中文 WPF 安装向导，目标是让 Windows 10 / Windows 11 x64 用户使用官方 Codex 桌面应用，并通过 DeepSeek 官方 API 使用模型。推荐 Windows 10 22H2（19045）或更新版本；19041—19044 可使用兼容尝试。

它会：

- 从 OpenAI 固定官方地址下载 `ChatGPT-x64.msix` 和离线许可证；
- 启动时检测当前用户是否已安装 Codex，并持续显示版本、实际安装盘和安装路径；
- 安装包默认保存到本助手 EXE 旁的 `Downloads`，目录不可写时回退到当前用户本地数据目录，也可以在下载前自行选择；
- 列出符合条件的 Windows AppX 安装盘，D 盘可用时默认选择 D，只迁移 Codex，不改变其他应用的默认安装盘；
- 把下载校验和系统安装拆成两个明确阶段，分别重试；
- 显示当前文件、已下载/总大小、百分比、实时速度、预计剩余时间、总体进度和最多 200 条带时间记录；
- 校验 MSIX 包身份、架构、内部签名文件以及 Windows Authenticode 状态；
- 经 UAC 授权调用 Windows AppX 部署命令；
- 官方离线部署失败时，可由用户明确选择实验性解包运行；
- 引导用户自行前往 DeepSeek 官方网站注册、实名认证、充值并创建 API Key；
- 调用 DeepSeek 官方接口验证 Key 和模型；
- 把 Key 保存到当前用户的 Windows 凭据管理器；
- 备份并合并 `%USERPROFILE%\.codex\config.toml`，保留已有 MCP、插件等无关配置；
- 若官方桌面包没有生成用户级 CLI runtime，则从已验证安装包复制同版本 `codex*.exe` 配套文件到当前用户目录，并设置 `CODEX_CLI_PATH`（不修改或重打包 MSIX）。
- 按“安装 Codex → 配置 DeepSeek → 完成”的三步向导自动推进，当前页只显示下一个应执行的操作。
- 随时提供“检测安装与清理数据”窗口，可查看位置和大小，并按项删除安装包、Codex、用户数据、CLI、凭据、缓存或安装助手自身。

它不会安装或配置 FlClash、代理、VPN、DNS、路由或节点订阅，也不包含 API Key。

## 使用

发布目录必须同时包含：

```text
CodexDeepSeekSetup.exe
CodexDeepSeekSetup.Helper.exe
```

双击 `CodexDeepSeekSetup.exe`，程序会自动检查电脑。第一张向导页包含下载和安装两个阶段，依次完成：

1. 确认“安装包保存到”和“Codex 安装磁盘”。D 盘是已就绪的本地固定 NTFS 磁盘且可用空间不少于 3 GB 时会被优先选择；否则使用系统盘。
2. 点击“下载并校验”，等待 MSIX 和许可证完成下载及签名校验。下载阶段不会请求管理员授权。
3. 校验通过后点击“安装 Codex”。安装时的系统弹窗需要输入 **Windows 管理员密码**，不是 DeepSeek API Key。安装失败可直接重试，不会重复下载；只有官方离线部署失败时才会显示“实验性解包运行”。
4. 在 DeepSeek 官方平台准备 API Key，粘贴后点击“验证并配置 Codex”。
5. 在完成页启动 Codex，或关闭安装助手。

下载区域会持续显示文件级进度与总体进度。展开“查看详细记录”可查看检查、下载、校验、授权、部署、注册、CLI 和保存状态等阶段；记录只包含运行状态，不包含 API Key。

配置前请在 [DeepSeek 开放平台](https://platform.deepseek.com/)完成账户准备，并在 [API Keys](https://platform.deepseek.com/api_keys) 页面创建密钥。实名认证资料只应填写在 DeepSeek 官方页面。

## 安装位置与检测清理

MSIX 桌面应用的位置由 Windows AppX 管理，不能选择任意普通文件夹。安装助手使用 Windows 的 AppX 卷机制和 `Move-AppxPackage` 移动 Codex；目标通常是 `<盘符>:\WindowsApps`。根据 [Microsoft 的说明](https://learn.microsoft.com/en-us/powershell/module/appx/move-appxpackage)，移动包时应用数据也会随包移动。迁移失败不会回滚已经完成的 Codex 安装，维护窗口可稍后重试。

窗口顶部的“检测安装与清理数据”随时可用。它只扫描当前用户和本助手的已知位置，包括 `OpenAI.Codex`、`.codex`、Windows 应用本地数据、CLI、实验性目录、下载文件、安装状态、`CODEX_CLI_PATH` 以及 DeepSeek 凭据是否存在；不会读取或显示 Key。

清理时每一类内容独立选择。下载项只删除 `ChatGPT-x64.msix`、`ChatGPT-License.xml` 及其临时分片，不递归删除用户选择的下载目录。`.codex`、Windows 应用数据、凭据和安装助手自身默认不勾选；选中后会在最终清单中标红并再次确认。移除系统包需要 Windows 管理员授权。安装助手自删除只移除已知发布文件；随助手附带的两个官方安装文件有独立选项，旁边的未知文件始终保留。

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
