using System.Text;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;
using Tomlyn;
using Tomlyn.Model;

namespace CodexDeepSeekSetup.Core.Configuration;

public sealed class CodexConfigService
{
    public async Task<OperationResult<ConfigApplyResult>> ApplyAsync(
        CodexConfigRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var configPath = Path.Combine(request.CodexHome, "config.toml");
        var modelsPath = Path.Combine(request.CodexHome, "models.json");

        try
        {
            Directory.CreateDirectory(request.CodexHome);
            var table = await ReadExistingAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (table is null)
            {
                return OperationResult<ConfigApplyResult>.Failure(
                    "config.toml.invalid",
                    "现有 config.toml 无法解析，未修改任何文件");
            }

            var catalog = await ModelCatalogTemplate.ReadAsync(cancellationToken).ConfigureAwait(false);
            var backupDirectory = CreateBackupDirectory(request.CodexHome);
            Directory.CreateDirectory(backupDirectory);
            CopyIfPresent(configPath, Path.Combine(backupDirectory, "config.toml"));
            CopyIfPresent(modelsPath, Path.Combine(backupDirectory, "models.json"));

            ApplyModelSettings(table, request, modelsPath);
            var toml = TomlSerializer.Serialize(table);
            _ = TomlSerializer.Deserialize<TomlTable>(toml)
                ?? throw new InvalidDataException("生成的 TOML 为空");
            using (JsonDocument.Parse(catalog))
            {
            }

            await WriteAtomicallyAsync(modelsPath, catalog, cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(configPath, toml, cancellationToken).ConfigureAwait(false);

            return OperationResult<ConfigApplyResult>.Success(
                new ConfigApplyResult(backupDirectory, configPath, modelsPath));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return OperationResult<ConfigApplyResult>.Failure(
                "config.write.failed",
                "写入 Codex 配置失败，原配置已保留");
        }
    }

    private static async Task<TomlTable?> ReadExistingAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return TomlSerializer.Deserialize<TomlTable>(text);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyModelSettings(TomlTable root, CodexConfigRequest request, string modelsPath)
    {
        root["model"] = request.Model;
        root["model_provider"] = "deepseek";
        root["preferred_auth_method"] = "apikey";
        root["forced_login_method"] = "api";
        root["model_reasoning_effort"] = "high";
        root["model_catalog_json"] = modelsPath.Replace('\\', '/');

        var providers = GetOrCreateTable(root, "model_providers");
        var provider = new TomlTable
        {
            ["name"] = "DeepSeek",
            ["base_url"] = "https://api.deepseek.com/",
            ["wire_api"] = "responses",
            ["supports_websockets"] = false,
            ["auth"] = new TomlTable
            {
                ["command"] = request.CredentialHelperPath,
                ["args"] = new TomlArray
                {
                    "credential",
                    "read",
                    "--target",
                    request.CredentialTarget
                }
            }
        };
        providers["deepseek"] = provider;
    }

    private static TomlTable GetOrCreateTable(TomlTable root, string name)
    {
        if (root.TryGetValue(name, out var value) && value is TomlTable table)
        {
            return table;
        }

        var created = new TomlTable();
        root[name] = created;
        return created;
    }

    private static string CreateBackupDirectory(string codexHome) =>
        Path.Combine(
            codexHome,
            "backups",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"[..(20 + 1 + 8)]);

    private static void CopyIfPresent(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        string content,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
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
