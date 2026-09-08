using System.Text;
using System.Text.Json;

namespace CodexDeepSeekSetup.Core.Workflow;

public sealed class SetupStateStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<SetupState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new SetupState();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SetupState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new SetupState();
        }
        catch (JsonException)
        {
            return new SetupState();
        }
    }

    public async Task SaveAsync(SetupState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        if (json.Contains("sk-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装状态不能包含 API Key。");
        }

        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
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
