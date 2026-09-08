using System.Text;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;
using Tomlyn;
using Tomlyn.Model;

namespace CodexDeepSeekSetup.Core.Configuration;

public sealed class CodexConfigService
{
    public async Task<OperationResult<Unit>> RestoreAsync(
        ConfigApplyResult applied,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applied);
        var backupConfig = Path.Combine(applied.BackupDirectory, "config.toml");
        var backupModels = Path.Combine(applied.BackupDirectory, "models.json");
        try
        {
            string? config = null;
            string? models = null;
            if (File.Exists(backupConfig))
            {
                config = await File.ReadAllTextAsync(backupConfig, cancellationToken).ConfigureAwait(false);
                _ = TomlSerializer.Deserialize<TomlTable>(config)
                    ?? throw new InvalidDataException("备份 TOML 无效");
            }
            if (File.Exists(backupModels))
            {
                models = await File.ReadAllTextAsync(backupModels, cancellationToken).ConfigureAwait(false);
                using (JsonDocument.Parse(models))
                {
                }
            }

            if (config is null)
            {
                File.Delete(applied.ConfigPath);
            }
            else
            {
                await WriteAtomicallyAsync(applied.ConfigPath, config, cancellationToken).ConfigureAwait(false);
            }

            if (models is null)
            {
                File.Delete(applied.ModelCatalogPath);
            }
            else
            {
                await WriteAtomicallyAsync(applied.ModelCatalogPath, models, cancellationToken).ConfigureAwait(false);
            }

            return OperationResult<Unit>.Success(default);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return OperationResult<Unit>.Failure("config.restore.failed", "无法恢复上一次 Codex 配置，备份文件仍保留。");
        }
    }

    public async Task<OperationResult<ConfigApplyResult>> ApplyAsync(
        CodexConfigRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var configPath = Path.Combine(request.CodexHome, "config.toml");
        var modelsPath = Path.Combine(request.CodexHome, "models.json");
        var configExisted = File.Exists(configPath);
        var modelsExisted = File.Exists(modelsPath);
        string? backupDirectory = null;

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
            backupDirectory = CreateBackupDirectory(request.CodexHome);
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
            TryRollback(configPath, modelsPath, backupDirectory, configExisted, modelsExisted);
            throw;
        }
        catch (Exception)
        {
            TryRollback(configPath, modelsPath, backupDirectory, configExisted, modelsExisted);
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

    private static void TryRollback(
        string configPath,
        string modelsPath,
        string? backupDirectory,
        bool configExisted,
        bool modelsExisted)
    {
        if (backupDirectory is null || !Directory.Exists(backupDirectory))
        {
            return;
        }

        try
        {
            RestoreOne(configPath, Path.Combine(backupDirectory, "config.toml"), configExisted);
            RestoreOne(modelsPath, Path.Combine(backupDirectory, "models.json"), modelsExisted);
        }
        catch
        {
            // Best-effort rollback; the untouched backup remains available for manual recovery.
        }
    }

    private static void RestoreOne(string destination, string backup, bool originallyExisted)
    {
        if (originallyExisted && File.Exists(backup))
        {
            File.Copy(backup, destination, overwrite: true);
        }
        else if (!originallyExisted && File.Exists(destination))
        {
            File.Delete(destination);
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
