# Codex + DeepSeek Windows 安装向导设计规格

日期：2026-09-08  
状态：已确认  
目标版本：开源版、内部版  
技术栈：C#、.NET 8、WPF、Windows x64 自包含发布

## 1. 目标

开发一个中文 Windows 图形化安装向导，让普通用户能够在 Windows 10 build 19041 或更新版本、Windows 11 x64 电脑上完成以下工作。Windows 10 22H2（19045）是推荐基线，19041—19044 作为兼容尝试：

1. 检查操作系统、账户类型、AppX 组件和必要服务。
2. 从 OpenAI 官方固定地址下载 Microsoft Store 签名的 Codex MSIX 和离线许可证。
3. 自动选择可用的官方安装路径并安装或更新 Codex。
4. 引导用户在 DeepSeek 开放平台完成注册、实名认证、充值和创建 API Key。
5. 在本机安全保存 DeepSeek API Key。
6. 备份并更新用户级 Codex 配置，使桌面端和 CLI 默认使用 DeepSeek Responses API。
7. 启动 Codex，并验证安装状态、CLI 状态、配置语法、API Key 和模型调用。
8. 对失败步骤给出中文原因、日志位置和可重试的修复操作。

## 2. 明确不包含的功能

- 不下载、安装或配置 FlClash 或其他代理/VPN 客户端。
- 不接收、保存或分发任何节点订阅。
- 不自动修改 Windows 系统代理、WinHTTP 代理、DNS 或路由。
- 不自动执行 DeepSeek 实名认证；身份证、姓名、手机号验证码和人脸识别只能由用户在 DeepSeek 官方页面中自行提交。
- 不自动登录 OpenAI/ChatGPT 账号。
- 不绕过任何平台身份认证、地区限制或服务器端验证。
- 不重签名或重新封装 OpenAI 的 MSIX。仅在用户明确选择实验回退时解压已经验证的官方 MSIX。

## 3. 官方依赖与信任边界

Codex 文件只从以下 OpenAI 官方地址取得：

- `https://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix`
- `https://persistent.oaistatic.com/codex-app-prod/ChatGPT-License.xml`

DeepSeek 引导和 API 只使用以下官方域名：

- `https://platform.deepseek.com/`
- `https://api.deepseek.com/`
- `https://api-docs.deepseek.com/`

程序只允许 HTTPS，不跟随到非 HTTPS 地址。下载完成后检查文件存在、长度、包清单、包架构、包系列名称和 Authenticode/证书链。验证失败时不得继续安装。

## 4. 两个发行版本

### 4.1 开源版

- 完整源代码公开，建议采用 MIT 许可证。
- 不携带第三方二进制文件和任何 API Key。
- 运行时从官方固定地址下载 Codex 文件。
- 用户自行粘贴 DeepSeek API Key。
- 默认模型为 `deepseek-v4-flash`，可在向导中选择其他官方兼容模型。

### 4.2 内部版

- 与开源版使用同一代码库，通过 MSBuild 构建属性选择 `Internal` 配置。
- 可读取程序旁边的 `payload` 目录中的官方 MSIX 和许可证，便于内网或离线部署；文件不存在时仍从官方地址下载。
- 可配置组织名称、帮助联系方式、默认模型和是否显示高级诊断。
- 不在二进制、配置模板或构建参数中嵌入 DeepSeek API Key、用户身份资料或其他秘密。

## 5. 用户界面与流程

安装向导采用单窗口分步界面，包含返回、继续、重试、取消按钮和顶部总进度。

### 第 1 页：欢迎与说明

- 说明将安装官方 Codex，并把模型提供商设置为 DeepSeek。
- 展示将创建或修改的目录、配置文件和凭据名称。
- 链接到 OpenAI、DeepSeek 官方文档和本项目隐私说明。

### 第 2 页：电脑检查

- Windows 版本与 x64 架构。
- 当前用户、是否为管理员、是否为 SID 结尾 `-500` 的内置 Administrator。
- UAC 的 `EnableLUA` 和 `FilterAdministratorToken` 状态。
- `AppXSvc`、`ClipSVC`、`LicenseManager`、`StateRepository` 服务状态。
- AppX 默认卷、WindowsApps 目录、可用空间和系统时间。
- 已安装的 `OpenAI.Codex` 包版本、状态和当前用户注册状态。

普通的手动启动服务可以由用户点击“修复”执行。涉及注册表安全策略的修改必须先展示解释并取得明确确认。若内置 Administrator 需要启用 `FilterAdministratorToken=1`，程序设置后要求重启，并使用当前用户的 RunOnce 项在重启后恢复向导进度。

### 第 3 页：下载与验证

- 优先使用内部版相邻 `payload` 中验证通过的文件。
- 否则并行下载 MSIX 和许可证到 `%ProgramData%\CodexDeepSeekSetup\Cache`。
- 支持断点续传、取消、超时和最多三次指数退避重试。
- 显示来源域名、已下载大小和验证状态。
- 不允许用户用任意 URL 替换官方来源；可选择已经下载的本地官方文件。

### 第 4 页：安装 Codex

自动按以下顺序选择策略：

1. 如果当前用户已经安装正常且版本不旧，跳过安装。
2. Windows Store/winget 环境完整时，优先使用官方产品 ID `9PLM9XGG6VKS` 更新或安装。
3. 无法使用 Microsoft 分发服务时，使用已验证 MSIX 和离线许可证执行离线部署。
4. 部署后为当前用户注册包，并通过 AUMID `OpenAI.Codex_2p2nqsd0c76g0!App` 验证激活。

安装命令由受控参数生成，不拼接用户输入。需要管理员权限的步骤通过单独的提权帮助进程执行，主界面保持普通用户权限。失败时收集 AppXDeploymentServer、AppModel-Runtime、TWinUI 和 ClipSVC 的相关事件，但不收集无关系统日志。

官方离线部署仍是首选。部署失败时允许用户明确选择“实验性解包运行”：再次验证官方签名、包身份和 x64 架构后，解压到当前用户目录，设置 `CODEX_CLI_PATH` 并直接启动。界面必须说明该模式缺少稳定的包身份、自动更新、通知、协议关联或部分沙盒保证，不得将其描述为正式安装。

### 第 5 页：DeepSeek 注册与实名引导

- “打开 DeepSeek 开放平台”按钮只打开官方 HTTPS 页面。
- 用图文步骤说明：注册账号、按页面要求完成实名、进入账户中心、按需充值、进入 API Keys、创建新 Key、立即复制。
- 程序不读取浏览器页面，不代填实名资料，不保存身份证、姓名、手机号或验证码。
- 用户点击“我已创建 API Key”后进入下一页。

### 第 6 页：API Key 与模型

- 使用密码框接收以 `sk-` 开头的 Key，支持临时显示和粘贴，但禁止复制到日志。
- 调用 `GET https://api.deepseek.com/user/balance` 验证身份和余额。
- 返回 401 时提示 Key 无效；402 或余额不可用时提示充值；网络错误允许重试。
- 默认选择 `deepseek-v4-flash`；其他模型仅在 DeepSeek `/models` 返回可用且本地模型目录模板支持时展示。
- 默认推理强度为 `high`。

### 第 7 页：配置 Codex

- 目标目录：`%USERPROFILE%\.codex`。
- 修改前将 `config.toml` 和 `models.json` 复制到 `%USERPROFILE%\.codex\backups\<UTC时间戳>`。
- 使用 TOML/JSON 解析库读取并合并，不用字符串替换。
- 保留用户已有的 MCP、插件、通知、信任目录和桌面设置。
- 只替换与模型、登录方式、模型目录和 `model_providers.deepseek` 冲突的字段。
- 写入临时文件，完成语法验证后使用原子替换；失败时保留原文件。

预期的逻辑配置为：

```toml
model = "deepseek-v4-flash"
model_provider = "deepseek"
preferred_auth_method = "apikey"
forced_login_method = "api"
model_reasoning_effort = "high"
model_catalog_json = "~/.codex/models.json"

[model_providers.deepseek]
name = "DeepSeek"
base_url = "https://api.deepseek.com/"
wire_api = "responses"
supports_websockets = false

[model_providers.deepseek.auth]
command = "<本程序安装后的凭据帮助程序路径>"
args = ["credential", "read", "--target", "CodexDeepSeekSetup/DeepSeekApiKey"]
```

真实 API Key 写入当前用户的 Windows 凭据管理器，不出现在 TOML、命令行参数或日志中。凭据帮助程序只向标准输出返回 Token，错误信息写入标准错误，不在正常模式下展示 Key。

`models.json` 使用随程序版本固定并经过测试的 DeepSeek 官方 Codex 模型目录结构。程序更新时同步升级模板；不会在运行时执行远程 PowerShell 脚本。

### 第 8 页：启动与验证

- 验证包状态和当前用户注册状态。
- 定位安装包内 Codex CLI。若桌面包未生成用户级 CLI runtime，允许把已安装、已验证的同版本 `app\resources\codex*.exe` 配套文件复制到当前用户的版本化目录并设置 `CODEX_CLI_PATH`；不得修改来源包、混用版本或复制其他资源。
- 运行一个最小、无工具的 DeepSeek Responses API 测试。
- 通过正式 AUMID 启动桌面应用。
- 检查 ChatGPT/Codex 进程、主窗口句柄和近期激活日志。
- 成功页显示“Codex 已连接 DeepSeek”；失败页显示错误分类和对应修复按钮。

## 6. 组件设计

- `CodexDeepSeekSetup.App`：WPF UI、导航和状态展示。
- `CodexDeepSeekSetup.Core`：领域模型、流程状态机、结果和错误代码。
- `CodexDeepSeekSetup.Windows`：Windows 版本、服务、注册表、AppX、AUMID、凭据管理器和事件日志。
- `CodexDeepSeekSetup.Downloads`：官方文件下载、缓存、重试和验证。
- `CodexDeepSeekSetup.DeepSeek`：Key 验证、余额、模型列表和 Responses 测试。
- `CodexDeepSeekSetup.Configuration`：TOML/JSON 合并、备份、原子写入和恢复。
- `CodexDeepSeekSetup.Elevated`：最小权限的提权帮助进程，只接受预定义操作和结构化参数。
- `CodexDeepSeekSetup.CredentialHelper`：由 Codex 调用，从当前用户凭据管理器读取 Key。

为减少部署文件数量，主程序、提权帮助和凭据帮助可由同一个可执行文件使用不同命令行模式实现；内部逻辑仍按独立组件隔离。

## 7. 状态、恢复和回滚

- 向导状态保存在 `%LOCALAPPDATA%\CodexDeepSeekSetup\state.json`，不含 API Key。
- 仅在需要重启时保留可恢复状态；安装成功后清理 RunOnce。
- 每次配置修改都有独立备份。
- 设置页提供“恢复上一次 Codex 配置”和“删除本机 DeepSeek 凭据”。
- 安装 Codex 失败不修改 Codex 配置；配置验证失败不启动 Codex。
- 取消操作不会删除用户原有 Codex 安装或配置。

## 8. 日志与隐私

- 日志目录：`%LOCALAPPDATA%\CodexDeepSeekSetup\Logs`。
- 记录时间、步骤、错误代码、HTTP 状态、包版本和事件日志摘要。
- 对 `Authorization`、`sk-` 开头的值、查询参数、用户名路径和身份资料进行脱敏。
- 默认不上传遥测，不提供远程日志上传功能。
- 用户可以在结束页打开日志目录或导出脱敏诊断包。

## 9. 错误处理

错误分为：

- 环境不支持：Windows 版本、架构、磁盘空间或策略限制。
- 下载失败：DNS、TLS、超时、HTTP 状态或文件验证失败。
- AppX 失败：以十六进制错误码显示，并附最相关的部署事件。
- DeepSeek 失败：认证、余额、模型、限速、服务繁忙或 Responses 兼容错误。
- 配置失败：TOML/JSON 解析、权限、路径或原子替换失败。
- 启动失败：包未注册、CLI 缺失、进程无窗口或激活错误。

每个错误必须包含：发生了什么、程序已做过什么、用户下一步可以做什么。可恢复错误提供“重试”，需要重启的错误提供“保存并重启”。

## 10. 测试策略

### 自动化测试

- TOML 合并保留未知字段和复杂表。
- 配置冲突移除、备份、恢复和原子写入。
- 日志中 API Key、Authorization 和用户路径脱敏。
- 下载断点续传、哈希/签名失败、重试和取消。
- DeepSeek 401、402、429、500、503 和超时映射。
- OS、SID、UAC、服务和 AppX 状态分类。
- 提权帮助进程拒绝未知操作和任意路径。
- 凭据的写入、读取、替换和删除。

### Windows 验证矩阵

- Windows 10 22H2 x64，普通管理员账户。
- Windows 10 22H2 x64，SID `-500` 的内置 Administrator。
- Windows 11 x64，普通管理员账户。
- Windows Store 存在/缺失、winget 存在/缺失。
- Codex 未安装、已安装旧版、已安装最新版和包注册损坏。
- 全新 `.codex`、已有复杂配置、损坏 TOML。
- 有网、下载中断、离线 payload、无效 Key、余额不足。

### 发布验证

- `dotnet test` 全部通过。
- `dotnet publish -c Release -r win-x64 --self-contained true` 成功。
- 在干净的 Windows 虚拟机中完成端到端安装、配置、启动和恢复测试。
- 对最终 EXE 进行 Authenticode 签名；无代码签名证书时必须明确显示未签名开发构建，不伪造发布者。

## 11. 交付物

- Visual Studio/.NET 解决方案和源代码。
- 开源版、内部版构建配置。
- 中文 WPF 安装向导。
- 单元测试与 Windows 集成测试脚本。
- `README.md`、构建说明、隐私说明和第三方许可证清单。
- 开源版 `win-x64` 自包含发布产物。
- 内部版支持相邻离线 payload 的发布产物。

## 12. 验收标准

在受支持的干净 Windows 机器上，用户不需要手工编辑 TOML/JSON 或运行 PowerShell。用户完成 DeepSeek 官方页面中的注册、实名、充值和 Key 创建，并把 Key 粘贴到向导后，程序能够安装官方 Codex、保存凭据、生成有效配置、启动 Codex，并成功完成一次 DeepSeek 模型测试。任何失败都不会泄露 Key，也不会破坏用户原有配置。
