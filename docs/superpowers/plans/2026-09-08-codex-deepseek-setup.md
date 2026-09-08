# Codex + DeepSeek Windows Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Chinese Windows wizard that installs the official Codex MSIX, guides DeepSeek onboarding, stores the API key in Windows Credential Manager, writes compatible Codex configuration, and verifies launch.

**Architecture:** A self-contained .NET 8 WPF executable hosts the normal UI plus restricted command modes for elevated AppX operations and credential retrieval. Platform-neutral orchestration, configuration, download, and DeepSeek code is separated from Windows adapters so core behavior can be tested on macOS/Linux; Windows-only publishing and smoke tests run on Windows.

**Tech Stack:** C# 12, .NET 8, WPF, CommunityToolkit.Mvvm, Tomlyn, System.Text.Json, xUnit, Windows Credential Manager P/Invoke, PowerShell/AppX.

**Spec:** `docs/specs/2026-09-08-codex-deepseek-setup-design.md`

## Global Constraints

- Target Windows 10 22H2 and Windows 11, x64 only.
- Download Codex only from `persistent.oaistatic.com` and DeepSeek content only from official DeepSeek domains.
- Never install or configure FlClash, proxies, VPNs, DNS, routes, or node subscriptions.
- Never collect or automate real-name identity data.
- Never place the DeepSeek API key in TOML, JSON, command-line arguments, state, or logs.
- Back up valid existing Codex configuration before mutation and use atomic replacement.
- Preserve unrelated Codex settings, including MCP servers, plugins, notifications, trust, and desktop settings.
- Do not unpack, re-sign, or repackage the official Codex MSIX.
- Do not claim Windows support without a Windows publish/smoke-test result.

---

### Task 1: Solution foundation, result model, and secret redaction

**Files:**
- Create: `CodexDeepSeekSetup.sln`
- Create: `Directory.Build.props`
- Create: `src/CodexDeepSeekSetup.Core/CodexDeepSeekSetup.Core.csproj`
- Create: `src/CodexDeepSeekSetup.Core/Results/OperationResult.cs`
- Create: `src/CodexDeepSeekSetup.Core/Security/SecretRedactor.cs`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/CodexDeepSeekSetup.Core.Tests.csproj`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/Security/SecretRedactorTests.cs`

**Interfaces:**
- Produces: `OperationResult<T>.Success(T)` and `OperationResult<T>.Failure(string,string)`.
- Produces: `SecretRedactor.Redact(string?) : string`.

- [ ] **Step 1: Create solution/project configuration and write the failing redaction tests**

```csharp
[Theory]
[InlineData("Authorization: Bearer sk-abc123456789", "Authorization: Bearer [REDACTED]")]
[InlineData("key=sk-abc123456789", "key=[REDACTED]")]
public void Redact_RemovesBearerAndDeepSeekKeys(string input, string expected)
    => Assert.Equal(expected, SecretRedactor.Redact(input));
```

- [ ] **Step 2: Run the focused test and confirm it fails because `SecretRedactor` does not exist**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests --filter SecretRedactorTests`

Expected: FAIL with missing `SecretRedactor` type.

- [ ] **Step 3: Implement the smallest redactor and result type**

```csharp
public static class SecretRedactor
{
    private static readonly Regex Bearer = new(@"(?i)(Authorization\s*:\s*Bearer\s+)\S+", RegexOptions.Compiled);
    private static readonly Regex DeepSeekKey = new(@"(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{8,}", RegexOptions.Compiled);
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return DeepSeekKey.Replace(Bearer.Replace(value, "$1[REDACTED]"), "[REDACTED]");
    }
}
```

- [ ] **Step 4: Run all core tests and confirm they pass**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests`

Expected: PASS, 0 failed.

### Task 2: DeepSeek API validation and error mapping

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/DeepSeek/DeepSeekClient.cs`
- Create: `src/CodexDeepSeekSetup.Core/DeepSeek/DeepSeekModels.cs`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/DeepSeek/DeepSeekClientTests.cs`

**Interfaces:**
- Produces: `DeepSeekClient.ValidateAsync(string,CancellationToken) : Task<OperationResult<DeepSeekAccountStatus>>`.
- Produces: `DeepSeekClient.TestResponseAsync(string,string,CancellationToken) : Task<OperationResult<string>>`.

- [ ] **Step 1: Write failing tests using a real `HttpMessageHandler` test double at the external HTTP boundary**

```csharp
[Fact]
public async Task ValidateAsync_MapsUnauthorizedWithoutEchoingKey()
{
    const string key = "sk-secret12345678";
    var client = DeepSeekClientTestFactory.Create(HttpStatusCode.Unauthorized, "{}");
    var result = await client.ValidateAsync(key, default);
    Assert.False(result.IsSuccess);
    Assert.Equal("deepseek.auth.invalid", result.ErrorCode);
    Assert.DoesNotContain(key, result.ErrorMessage);
}
```

- [ ] **Step 2: Run the tests and confirm missing client/types cause failure**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests --filter DeepSeekClientTests`

Expected: FAIL because `DeepSeekClient` is undefined.

- [ ] **Step 3: Implement HTTPS-only endpoints, balance parsing, response test, and 401/402/429/5xx mapping**

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "user/balance");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
using var response = await http.SendAsync(request, cancellationToken);
return response.StatusCode switch
{
    HttpStatusCode.Unauthorized => OperationResult<DeepSeekAccountStatus>.Failure("deepseek.auth.invalid", "API Key 无效"),
    HttpStatusCode.PaymentRequired => OperationResult<DeepSeekAccountStatus>.Failure("deepseek.balance.insufficient", "账户余额不足"),
    (HttpStatusCode)429 => OperationResult<DeepSeekAccountStatus>.Failure("deepseek.rate_limited", "请求过于频繁"),
    _ => await ParseBalanceAsync(response, cancellationToken)
};
```

- [ ] **Step 4: Run DeepSeek and complete core test suites**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests`

Expected: PASS, including invalid key, insufficient balance, rate limit, malformed JSON, timeout, and success cases.

### Task 3: TOML/JSON configuration merge, backup, and atomic restore

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/Configuration/CodexConfigService.cs`
- Create: `src/CodexDeepSeekSetup.Core/Configuration/ModelCatalogTemplate.cs`
- Create: `src/CodexDeepSeekSetup.Core/Configuration/Models.cs`
- Create: `src/CodexDeepSeekSetup.Core/Resources/deepseek-models.json`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/Configuration/CodexConfigServiceTests.cs`

**Interfaces:**
- Produces: `CodexConfigService.ApplyAsync(CodexConfigRequest,CancellationToken) : Task<OperationResult<ConfigApplyResult>>`.
- Produces: `CodexConfigService.RestoreAsync(string,CancellationToken) : Task<OperationResult<Unit>>`.
- Consumes: credential helper executable path and credential target, never the API key.

- [ ] **Step 1: Write a failing test that starts with complex existing TOML and asserts unrelated tables survive**

```csharp
[Fact]
public async Task ApplyAsync_PreservesPluginsAndMcpWhileReplacingProvider()
{
    await fixture.WriteConfigAsync("[plugins.\"browser@openai-bundled\"]\nenabled=true\n[mcp_servers.demo]\ncommand='demo'\nmodel='old'");
    var result = await fixture.Service.ApplyAsync(fixture.Request, default);
    var merged = await fixture.ReadConfigAsync();
    Assert.True(result.IsSuccess);
    Assert.Contains("[mcp_servers.demo]", merged);
    Assert.Contains("model = \"deepseek-v4-flash\"", merged);
    Assert.DoesNotContain("sk-", merged);
}
```

- [ ] **Step 2: Run focused tests and confirm failure from the missing service**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests --filter CodexConfigServiceTests`

Expected: FAIL because `CodexConfigService` is undefined.

- [ ] **Step 3: Implement Tomlyn-based merge, embedded catalog validation, timestamped backup, and same-volume atomic replace**

```csharp
model["model"] = request.Model;
model["model_provider"] = "deepseek";
model["preferred_auth_method"] = "apikey";
model["forced_login_method"] = "api";
model["model_reasoning_effort"] = "high";
model["model_catalog_json"] = request.ModelCatalogPath;
model["model_providers"] = MergeProvider(model, request);
```

- [ ] **Step 4: Run configuration tests, then all core tests**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests`

Expected: PASS for new config, existing config, malformed TOML rejection, backup, rollback, Unicode Windows paths, and no-key assertions.

### Task 4: Download cache, official-origin policy, and package metadata validation

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/Downloads/OfficialDownloadService.cs`
- Create: `src/CodexDeepSeekSetup.Core/Downloads/DownloadModels.cs`
- Create: `src/CodexDeepSeekSetup.Core/Downloads/OfficialOriginPolicy.cs`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/Downloads/OfficialDownloadServiceTests.cs`

**Interfaces:**
- Produces: `OfficialDownloadService.DownloadCodexPayloadAsync(string,IProgress<DownloadProgress>,CancellationToken)`.
- Produces: `OfficialOriginPolicy.EnsureAllowed(Uri) : OperationResult<Uri>`.
- Leaves Authenticode verification to `IPackageVerifier` in the Windows project.

- [ ] **Step 1: Write failing tests for domain allowlisting, HTTPS enforcement, resume, and cancellation**

```csharp
[Theory]
[InlineData("http://persistent.oaistatic.com/a.msix")]
[InlineData("https://evil.example/a.msix")]
public void EnsureAllowed_RejectsUnsafeOrigins(string value)
    => Assert.False(new OfficialOriginPolicy().EnsureAllowed(new Uri(value)).IsSuccess);
```

- [ ] **Step 2: Run the focused tests and confirm they fail for missing production types**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests --filter Downloads`

Expected: FAIL with missing download/origin types.

- [ ] **Step 3: Implement allowed immutable URLs, `.partial` resume, retry, progress, and atomic cache promotion**

```csharp
private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
{
    "persistent.oaistatic.com"
};
```

- [ ] **Step 4: Run download tests and all core tests**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests`

Expected: PASS with 0 failures.

### Task 5: Windows diagnostics, credential manager, and restricted helper modes

**Files:**
- Create: `src/CodexDeepSeekSetup.Windows/CodexDeepSeekSetup.Windows.csproj`
- Create: `src/CodexDeepSeekSetup.Windows/Diagnostics/WindowsReadinessService.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Security/WindowsCredentialStore.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Packages/CodexPackageVerifier.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Packages/CodexPackageManager.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Processes/RestrictedCommandRouter.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/CodexDeepSeekSetup.Windows.Tests.csproj`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Processes/RestrictedCommandRouterTests.cs`

**Interfaces:**
- Produces: `IWindowsReadinessService.CheckAsync` with OS, architecture, SID, UAC, service, package, volume, and disk results.
- Produces: `ISecretStore.Write/Read/Delete` backed by `CredWriteW`, `CredReadW`, and `CredDeleteW`.
- Produces: `RestrictedCommandRouter.ExecuteAsync(string[])`, accepting only `credential read`, `elevated appx-install`, `elevated start-service`, and `resume`.
- Produces: `ICodexPackageManager.InstallAsync` and `LaunchAsync`.

- [ ] **Step 1: Write failing command-router tests proving arbitrary commands and arbitrary paths are rejected**

```csharp
[Theory]
[InlineData("powershell", "-Command", "whoami")]
[InlineData("elevated", "delete", "C:\\")]
public async Task ExecuteAsync_RejectsUnknownOperations(params string[] args)
    => Assert.Equal(64, await router.ExecuteAsync(args));
```

- [ ] **Step 2: Run Windows tests and confirm the router type is missing**

Run on Windows or with Windows targeting enabled: `dotnet test tests/CodexDeepSeekSetup.Windows.Tests`

Expected: FAIL because `RestrictedCommandRouter` is undefined.

- [ ] **Step 3: Implement diagnostics and the strict router, then Credential Manager and package operations behind interfaces**

```csharp
return args switch
{
    ["credential", "read", "--target", CredentialTargets.DeepSeek] => await ReadCredentialAsync(),
    ["elevated", "appx-install", var requestFile] when IsOwnedRequestFile(requestFile) => await InstallAsync(requestFile),
    ["elevated", "start-service", var service] when AllowedServices.Contains(service) => await StartServiceAsync(service),
    ["resume"] => await ResumeAsync(),
    _ => 64
};
```

- [ ] **Step 4: Run Windows tests and verify package signature tests against a controlled signed fixture**

Run: `dotnet test tests/CodexDeepSeekSetup.Windows.Tests`

Expected: PASS; Windows-only tests skip with an explicit reason on non-Windows systems.

### Task 6: Installation/configuration workflow state machine

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/Workflow/SetupWorkflow.cs`
- Create: `src/CodexDeepSeekSetup.Core/Workflow/SetupState.cs`
- Create: `src/CodexDeepSeekSetup.Core/Workflow/SetupStateStore.cs`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/Workflow/SetupWorkflowTests.cs`

**Interfaces:**
- Produces: `SetupWorkflow.RunReadinessAsync`, `DownloadAsync`, `InstallAsync`, `ValidateKeyAsync`, `ConfigureAsync`, and `VerifyAsync`.
- Produces resumable `SetupState` containing stage and non-secret paths only.

- [ ] **Step 1: Write failing tests for ordering and failure isolation**

```csharp
[Fact]
public async Task ConfigureAsync_DoesNotWriteConfigWhenPackageInstallFailed()
{
    var workflow = WorkflowFixture.WithInstallFailure();
    var result = await workflow.ConfigureAsync(default);
    Assert.False(result.IsSuccess);
    Assert.False(workflow.ConfigWasWritten);
}
```

- [ ] **Step 2: Run workflow tests and confirm failure from missing state machine**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests --filter SetupWorkflowTests`

Expected: FAIL because `SetupWorkflow` is undefined.

- [ ] **Step 3: Implement the minimal guarded state transitions and JSON state persistence without secrets**

```csharp
if (state.Stage < SetupStage.CodexInstalled)
    return OperationResult<ConfigApplyResult>.Failure("workflow.order", "请先完成 Codex 安装");
```

- [ ] **Step 4: Run workflow and full core test suites**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests`

Expected: PASS for success, cancellation, retry, reboot resume, install failure, invalid key, config failure, and verification failure.

### Task 7: WPF wizard and Chinese user guidance

**Files:**
- Create: `src/CodexDeepSeekSetup.App/CodexDeepSeekSetup.App.csproj`
- Create: `src/CodexDeepSeekSetup.App/App.xaml`
- Create: `src/CodexDeepSeekSetup.App/App.xaml.cs`
- Create: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Create: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Create: `src/CodexDeepSeekSetup.App/ViewModels/MainWindowViewModel.cs`
- Create: `src/CodexDeepSeekSetup.App/ViewModels/WizardPageViewModel.cs`
- Create: `src/CodexDeepSeekSetup.App/Views/WelcomeView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/ReadinessView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/DownloadView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/InstallView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/DeepSeekGuideView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/ApiKeyView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/ConfigureView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/VerifyView.xaml`
- Create: `src/CodexDeepSeekSetup.App/Views/CompleteView.xaml`
- Create: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `SetupWorkflow`, readiness results, download progress, and operation results.
- Produces: navigation commands, retry/cancel/reboot commands, masked key input, and official-browser-link commands.

- [ ] **Step 1: Write failing ViewModel tests for navigation gates and key non-retention**

```csharp
[Fact]
public async Task ContinueCommand_CannotReachConfigureBeforeKeyValidation()
{
    var vm = WizardFixture.CreateAtKeyPage(validationSucceeds: false);
    await vm.ContinueCommand.ExecuteAsync(null);
    Assert.Equal(WizardPage.ApiKey, vm.CurrentPage);
    Assert.Null(vm.State.DeepSeekApiKey);
}
```

- [ ] **Step 2: Run App ViewModel tests and confirm missing ViewModel failure**

Run: `dotnet test tests/CodexDeepSeekSetup.App.Tests`

Expected: FAIL because `MainWindowViewModel` is undefined.

- [ ] **Step 3: Implement ViewModels, then add XAML views as thin bindings over tested commands**

```csharp
[RelayCommand]
private async Task ContinueAsync(CancellationToken cancellationToken)
{
    var result = await pageActions[CurrentPage](cancellationToken);
    if (result.IsSuccess) CurrentPage = Next(CurrentPage);
    else ErrorMessage = result.ErrorMessage;
}
```

- [ ] **Step 4: Run App tests and compile the WPF project**

Run: `dotnet test tests/CodexDeepSeekSetup.App.Tests && dotnet build src/CodexDeepSeekSetup.App -c Release`

Expected: PASS and build exit code 0.

### Task 8: Build flavors, documentation, and end-to-end release checks

**Files:**
- Create: `src/CodexDeepSeekSetup.App/BuildFlavor.cs`
- Create: `build/Publish-OpenSource.ps1`
- Create: `build/Publish-Internal.ps1`
- Create: `README.md`
- Create: `docs/privacy.md`
- Create: `docs/windows-test-matrix.md`
- Create: `LICENSE`
- Create: `.gitignore`

**Interfaces:**
- Produces: `artifacts/open-source/win-x64/` and `artifacts/internal/win-x64/` self-contained outputs.
- Internal flavor consumes only optional adjacent `payload/ChatGPT-x64.msix` and `payload/ChatGPT-License.xml`.

- [ ] **Step 1: Write a failing build-flavor test proving secrets and proxy features cannot be enabled by either flavor**

```csharp
[Theory]
[InlineData(BuildFlavor.OpenSource)]
[InlineData(BuildFlavor.Internal)]
public void Flavor_NeverEnablesProxyOrEmbeddedKey(BuildFlavor flavor)
{
    var options = BuildFlavorOptions.For(flavor);
    Assert.False(options.EnableProxyConfiguration);
    Assert.Null(options.EmbeddedApiKey);
}
```

- [ ] **Step 2: Run the test and confirm flavor types are missing**

Run: `dotnet test tests/CodexDeepSeekSetup.App.Tests --filter Flavor`

Expected: FAIL because `BuildFlavorOptions` is undefined.

- [ ] **Step 3: Implement flavor options, publish scripts, and operator/user documentation**

```csharp
public sealed record BuildFlavorOptions(bool AllowAdjacentPayload, bool EnableProxyConfiguration, string? EmbeddedApiKey)
{
    public static BuildFlavorOptions For(BuildFlavor flavor) =>
        new(flavor == BuildFlavor.Internal, false, null);
}
```

- [ ] **Step 4: Run all available tests and cross-target build; then run Windows publish on a Windows host**

Run locally: `dotnet test CodexDeepSeekSetup.sln && dotnet build CodexDeepSeekSetup.sln -c Release -p:EnableWindowsTargeting=true`

Run on Windows: `powershell -ExecutionPolicy Bypass -File build/Publish-OpenSource.ps1` and `powershell -ExecutionPolicy Bypass -File build/Publish-Internal.ps1`

Expected: all tests pass; both publish commands exit 0; no proxy-related binary/config payload or `sk-` key exists in artifacts.

- [ ] **Step 5: Execute the Windows test matrix and record exact outcomes**

Run the checklist in `docs/windows-test-matrix.md` on Windows 10 22H2 x64 and Windows 11 x64 VMs. Record OS build, account type, install strategy, package version, CLI version, model test result, launch result, rollback result, and log redaction result. Do not mark Windows release-ready until both required OS rows pass.

## Plan self-review

- Spec coverage: installation, DeepSeek guidance, credential storage, configuration merge, rollback, logging, build flavors, Windows account/AppX diagnostics, and verification are assigned to Tasks 1-8.
- Security coverage: FlClash/proxy/node behavior is excluded globally and asserted in Task 8; keys are protected in Tasks 1, 2, 3, 5, and 7.
- Platform coverage: core tests are cross-platform; Windows adapters and release claims require Windows execution.
- Type consistency: workflow consumes the operation, downloader, DeepSeek, configuration, package, and secret-store interfaces produced by earlier tasks.
