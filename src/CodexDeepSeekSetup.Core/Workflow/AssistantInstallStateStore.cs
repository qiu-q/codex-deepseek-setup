using System.Text;
using System.Text.Json;

namespace CodexDeepSeekSetup.Core.Workflow;

public sealed class AssistantInstallStateStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<AssistantInstallState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AssistantInstallState>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AssistantInstallState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        if (json.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("apiKey", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装记录不能包含 API Key。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
