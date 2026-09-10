using CodexDeepSeekSetup.Core.Guides;

namespace CodexDeepSeekSetup.App.Logic;

public static class DeepSeekGuideCatalog
{
    public static GuideDocument Document { get; } = new(
        "DeepSeek API 使用准备",
        [
            new GuideStep(
                "sign-in",
                "登录或注册",
                "使用手机号验证码、密码或微信扫码登录 DeepSeek 开放平台。",
                "已成功进入 DeepSeek 开放平台控制台",
                "打开登录页",
                new Uri("https://platform.deepseek.com/sign_in"),
                null),
            new GuideStep(
                "identity",
                "完成实名认证",
                "登录后按开放平台提示完成认证。姓名和身份证资料只填写在 DeepSeek 官方页面，本助手不会读取。",
                "账户已通过 DeepSeek 官方实名认证",
                "打开账户中心",
                new Uri("https://platform.deepseek.com/"),
                null),
            new GuideStep(
                "top-up",
                "充值 API 余额",
                "进入充值页确认账户、金额和官方计费说明。API 调用按量计费，请根据实际需要充值。",
                "开放平台账户已有可用 API 余额",
                "打开充值页",
                new Uri("https://platform.deepseek.com/top_up"),
                null),
            new GuideStep(
                "api-key",
                "创建并复制 API Key",
                "创建 Key 后立即复制完整内容。密钥通常只完整显示一次，然后返回安装助手粘贴。",
                "已经复制 sk- 开头的 DeepSeek API Key",
                "打开 API Keys",
                new Uri("https://platform.deepseek.com/api_keys"),
                null)
        ]);
}
