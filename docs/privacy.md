# 隐私说明

本程序不收集、上传或保存身份证、手机号等实名认证资料。相关资料由用户直接在 DeepSeek 官方网页提交。

DeepSeek API Key 只会发送到 `https://api.deepseek.com/` 做账户和模型调用验证，并保存到当前 Windows 用户的凭据管理器，目标名称为 `CodexDeepSeekSetup/DeepSeekApiKey`。Codex 运行时通过本地受限助手读取该凭据。

程序会在本机读取 Windows 版本、架构、UAC、必要系统服务、系统盘空间和 Codex 包状态，用于安装诊断。程序不会配置代理、VPN、DNS、路由或订阅服务。

用户明确选择实验模式时，程序会把已验证的官方 MSIX 解压到当前用户的 `%LOCALAPPDATA%\Programs\OpenAI\CodexPortable`。程序不会修改、重签名或重新封装其中的文件。
