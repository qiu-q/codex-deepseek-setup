using System.Reflection;
using System.Text.Json;

namespace CodexDeepSeekSetup.Core.Configuration;

internal static class ModelCatalogTemplate
{
    private const string ResourceName = "CodexDeepSeekSetup.Core.Resources.deepseek-models.json";

    public static async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入资源：{ResourceName}");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.GetArrayLength() == 0)
        {
            throw new InvalidDataException("DeepSeek 模型目录为空");
        }

        return json;
    }
}
