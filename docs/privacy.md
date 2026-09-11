# 隐私说明

本程序不收集、上传或保存身份证、手机号等实名认证资料。相关资料由用户直接在 DeepSeek 官方网页提交。

DeepSeek API Key 只会发送到 `https://api.deepseek.com/` 做账户和模型调用验证，并保存到当前 Windows 用户的凭据管理器，目标名称为 `CodexDeepSeekSetup/DeepSeekApiKey`。Codex 运行时通过本地受限助手读取该凭据。

程序会在本机读取 Windows 版本、架构、UAC、必要系统服务、系统盘空间和 Codex 包状态，用于安装诊断。只有用户主动启用可选网络辅助时，程序才会下载 Mihomo、读取用户提供的 HTTPS 订阅，并修改当前 Windows 用户的 Internet 代理；停用或清理会恢复原设置。程序不启用 TUN，不修改 DNS、路由、防火墙、WinHTTP 或其他用户的设置。

代理订阅地址只保存在当前用户的 Windows 凭据管理器，不进入日志、普通配置文件或命令行。节点选择通过仅监听 `127.0.0.1` 且带随机密钥的 Mihomo 控制接口完成；节点缓存、控制密钥和选择结果保存在 `%LOCALAPPDATA%\CodexDeepSeekSetup\Network`，可在维护窗口删除。

用户明确选择实验模式时，程序会把已验证的官方 MSIX 解压到当前用户的 `%LOCALAPPDATA%\Programs\OpenAI\CodexPortable`。程序不会修改、重签名或重新封装其中的文件。

程序会在 `%LOCALAPPDATA%\CodexDeepSeekSetup\install-state.json` 保存不含密钥的安装来源和清理记录，用于安全恢复修改前的 `CODEX_CLI_PATH`。

只有用户在完成页明确确认“彻底卸载并清理”后，程序才会删除官方 Codex 包、DeepSeek 凭据、整个 `%USERPROFILE%\.codex`、本助手创建的缓存/CLI/便携目录/安装记录和安装助手自身。这会永久删除 Codex 配置、插件缓存和会话，无法恢复。取消 Windows 管理员授权时，不会开始用户数据清理。
