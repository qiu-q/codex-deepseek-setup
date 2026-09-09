namespace CodexDeepSeekSetup.App.Logic;

public sealed record DeepSeekGuideItem(
    int Number,
    string Title,
    string Description,
    string ActionLabel,
    Uri Url);

public static class DeepSeekGuideCatalog
{
    public static IReadOnlyList<DeepSeekGuideItem> Items { get; } =
    [
        new(
            1,
            "登录或注册",
            "使用手机号验证码、密码或微信扫码登录。",
            "打开登录页",
            new Uri("https://platform.deepseek.com/sign_in")),
        new(
            2,
            "完成实名认证",
            "登录后按开放平台提示完成认证，身份资料只填写在 DeepSeek 官方页面。",
            "打开账户中心",
            new Uri("https://platform.deepseek.com/")),
        new(
            3,
            "充值 API 余额",
            "进入充值页确认计费与余额，充值在 DeepSeek 官方页面完成。",
            "打开充值页",
            new Uri("https://platform.deepseek.com/top_up")),
        new(
            4,
            "创建并复制 API Key",
            "新建 Key 后立即复制 sk- 开头的密钥，然后返回安装助手粘贴。",
            "打开 API Keys",
            new Uri("https://platform.deepseek.com/api_keys"))
    ];
}
