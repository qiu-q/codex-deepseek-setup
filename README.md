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
- 进入 DeepSeek 阶段时自动打开一次可提前关闭的分步弹窗，按“登录 → 实名认证 → 充值 → 创建 Key → 粘贴”逐步说明；关闭后可随时点击“查看操作引导”重新打开。内部版优先读取网页后台配置，断网或配置无效时自动使用内置教程。
- 完成页逐项确认 Codex、Windows 凭据和配置文件状态，并提供“启动 Codex”和“查看首次使用教程”。启动成功后会直接显示下一步提示。
- 随时提供“检测安装与清理数据”窗口，可查看位置和大小，并按项删除安装包、Codex、用户数据、CLI、凭据、缓存或安装助手自身。
- 主窗口、维护窗口和操作引导统一采用接近 Element Plus 的轻量视觉：白色页面、浅灰边框、4—6 px 小圆角和克制的状态色，不使用大面积深色顶栏。
- 可选启用轻量 Mihomo 网络辅助：用户自行粘贴有权使用的 HTTPS 订阅，运行时优先从项目自有 HTTPS 镜像下载并校验固定 SHA-256，GitHub 官方 Release 仅作备用；启用、停用和恢复均有可见进度。

它不会内置节点、订阅或共享代理账号，不启用 TUN，不修改 DNS、路由、防火墙、WinHTTP 或整机设置，也不包含 API Key。网络辅助仅在用户主动填写订阅并点击启用后，修改当前 Windows 用户的 Internet 代理；停用与清理会恢复启用前的原值。

## 使用

发布目录必须同时包含：

```text
CodexDeepSeekSetup.exe
CodexDeepSeekSetup.Helper.exe
CodexDeepSeekSetup.NetworkHelper.exe
```

双击 `CodexDeepSeekSetup.exe`，程序会自动检查电脑。第一张向导页包含下载和安装两个阶段，依次完成：

1. 确认“安装包保存到”和“Codex 安装磁盘”。D 盘是已就绪的本地固定 NTFS 磁盘且可用空间不少于 3 GB 时会被优先选择；否则使用系统盘。
2. 点击“下载并校验”，等待 MSIX 和许可证完成下载及签名校验。下载阶段不会请求管理员授权。
3. 校验通过后点击“安装 Codex”。安装时的系统弹窗需要输入 **Windows 管理员密码**，不是 DeepSeek API Key。安装失败可直接重试，不会重复下载；只有官方离线部署失败时才会显示“实验性解包运行”。
4. 进入 DeepSeek 页面后按分步弹窗完成登录、实名认证、充值和 Key 创建；弹窗可随时关闭，并可用“查看操作引导”重新打开。如果网络确实需要辅助，可先在可选区域填写自己的 HTTPS 订阅并启用；不需要时直接跳过。粘贴 API Key 后点击“验证并配置 Codex”。
5. 在完成页核对三项完成结果，点击“启动 Codex”；按钮变为“Codex 已启动”后即可新建任务并选择 DeepSeek 模型。清理功能位于折叠的“高级操作”。

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

发布脚本同时生成 ZIP、`release-report.json`、ZIP 的 `.sha256.txt` 校验文件，并检查两个辅助程序分别不超过 25 MB、无官方 MSIX 时发布 ZIP 不超过 85 MB。WPF 主程序保持不裁剪并启用单文件压缩；辅助程序使用完整裁剪、固定区域设置和单文件压缩，因此 Windows 10/11 无需另装 .NET 运行时。

脚本生成的是未签名开发版。Windows 可能显示“未知发布者”或 SmartScreen 提示；对外分发前必须使用受信任的 Authenticode 证书签署三个 EXE。

内部离线版只允许把未修改的 OpenAI 官方文件放在仓库根目录的 `payload` 文件夹，再执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build\Publish-Internal.ps1
```

内部版固定使用 Obfuscar 2.2.50，只处理 Core 和 App.Logic 的非公开实现；WPF 界面、公开接口、绑定/序列化/P/Invoke 名称不会重命名。混淆用于增加静态分析成本，不等同于加密。开源版不混淆、不连接推广接口。

## 内部版推广

推广只会在内部版配置成功后的完成页异步加载，明确标注“推广”，可以关闭。它不会弹窗、不会自动打开浏览器，也不会进入或修改官方 Codex；只有用户主动点击按钮时才会打开 HTTPS 页面。请求超时为 3 秒，断网、返回错误或图片加载失败不会影响安装、配置或启动。

客户端只发送活动编号及 `impression`/`click` 事件：不生成设备 ID，不读取或发送用户名、硬件信息、Codex 数据或 DeepSeek Key。开源版没有广告和统计代码入口。

推广与引导内容服务位于 `server/codex-ad`，默认监听 `127.0.0.1:8765`，包含健康检查、当前广告、分步操作引导、匿名汇总事件、带密码/CSRF 的管理后台和受限图片上传。后台可调整引导标题、步骤顺序、图片、说明和官方 HTTPS 按钮。引导请求不发送统计事件或用户数据。部署说明见 [内容服务说明](server/codex-ad/README.md)。

## 安全边界

- 下载主机被限制为 `persistent.oaistatic.com`，DeepSeek API 固定为 `https://api.deepseek.com/`。
- 提权助手只接受受限命令；它会在管理员上下文再次验证 MSIX。
- API Key 不进入 TOML、JSON、命令行参数、日志或构建产物。
- 代理订阅地址仅保存在当前用户 Windows 凭据管理器，不进入日志、配置或命令行；节点缓存位于当前用户本地数据目录，可由维护页清理。
- Mihomo 固定为 `v1.19.30`，镜像清单、上游源码归档和 GPLv3 许可证见 `server/mihomo-mirror`；程序仍会对下载归档执行固定 SHA-256 校验。
- 推广后台密码、服务器登录凭据和第三方密钥均不嵌入客户端；对外分发的内部版也只包含公开 HTTPS 接口地址。
- 清理目录由程序从 Windows 已知用户目录推导；安全检查会拒绝磁盘根目录、用户目录根、桌面和任意外部路径。
- 正式路径不解包运行；实验路径只解压通过官方签名和包身份校验的 MSIX，始终不重签名、不重新封包。
- 实验路径可能缺少自动更新、通知、协议关联或部分沙盒能力，不属于 OpenAI 官方支持的独立 EXE 安装方式。

## 当前验证状态

跨平台单元测试和 macOS 上的 Windows 交叉编译可用于开发验证；发布前仍必须按 [Windows 测试矩阵](docs/windows-test-matrix.md)在真实 Windows 10 和 Windows 11 上验证安装、启动及回滚。
