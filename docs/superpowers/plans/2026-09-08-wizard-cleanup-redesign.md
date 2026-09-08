# Guided Wizard and Complete Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the all-at-once installer screen with a state-driven three-step wizard and add a confirmed cleanup flow that removes Codex, all `.codex` data, assistant-managed artifacts, and the assistant itself.

**Architecture:** `MainWindowViewModel` owns the visible wizard state while `DesktopSetupActions` reports phase-specific progress. A persisted, secret-free install ledger records provenance. Cleanup is split into fixed elevated AppX removal, current-user data cleanup, and a temporary finalizer that waits for the UI process to exit before deleting only known published files and then itself.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit, Windows AppX PowerShell cmdlets, Windows Credential Manager.

**Spec:** `docs/superpowers/specs/2026-09-08-wizard-cleanup-redesign.md`

## Global Constraints

- Support Windows 10 build 19041 or newer on x64; recommend build 19045 or Windows 11.
- Download only the official OpenAI MSIX and offline license URLs already defined in `OfficialPayloadUrls`.
- Never serialize, log, or pass the DeepSeek API Key on a command line.
- A confirmed cleanup permanently deletes `%USERPROFILE%\.codex`.
- Never recursively delete a drive root, profile root, Desktop, or caller-supplied arbitrary directory.
- Unknown files beside the published assistant files must survive self-removal.
- Do not begin user-data cleanup if the required elevation is cancelled or AppX removal fails.

---

### Task 1: Persist a secret-free install ledger

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/Workflow/AssistantInstallState.cs`
- Create: `src/CodexDeepSeekSetup.Core/Workflow/AssistantInstallStateStore.cs`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/Workflow/AssistantInstallStateStoreTests.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`

**Interfaces:**
- Produces: `AssistantInstallState`, `AssistantInstallStateStore.LoadAsync`, `AssistantInstallStateStore.SaveAsync`.
- Consumes: existing atomic-file conventions from `SetupStateStore`.

- [ ] **Step 1: Write the failing persistence tests**

```csharp
[Fact]
public async Task SaveAsync_RoundTripsProvenanceWithoutSecretText()
{
    var path = Path.Combine(temp.Path, "install-state.json");
    var state = new AssistantInstallState(
        1, false, "official", "D:/tools/codex.exe", true,
        "C:/Users/test/AppData/Local/CodexDeepSeekSetup/Downloads",
        null,
        "C:/Users/test/AppData/Local/Programs/OpenAI/Codex/bin",
        "C:/Users/test/AppData/Local/Programs/CodexDeepSeekSetup/CodexDeepSeekSetup.Helper.exe",
        "D:/assistant");
    var store = new AssistantInstallStateStore(path);

    await store.SaveAsync(state, default);

    Assert.Equal(state, await store.LoadAsync(default));
    Assert.DoesNotContain("apiKey", await File.ReadAllTextAsync(path), StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task LoadAsync_WhenStateDoesNotExist_ReturnsNull()
{
    var store = new AssistantInstallStateStore(Path.Combine(temp.Path, "missing.json"));

    Assert.Null(await store.LoadAsync(default));
}
```

Use a write-failure filesystem double for `SaveAsync_WhenReplacementFails_PreservesOriginalState`; seed an original valid JSON file, force the replacement operation to throw, and assert the original bytes are unchanged.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.Core.Tests/CodexDeepSeekSetup.Core.Tests.csproj --no-restore --filter AssistantInstallStateStoreTests`

Expected: compilation fails because `AssistantInstallState` and `AssistantInstallStateStore` do not exist.

- [ ] **Step 3: Implement the ledger and wire install/configuration writes**

```csharp
public sealed record AssistantInstallState(
    int SchemaVersion,
    bool CodexExistedBefore,
    string InstallMode,
    string? PreviousCodexCliPath,
    bool DeepSeekConfigured,
    string DownloadCache,
    string? PortableDirectory,
    string? CliDirectory,
    string CredentialHelperPath,
    string AssistantDirectory);
```

Use UTF-8 JSON, a sibling GUID-suffixed temporary file, `File.Move(..., overwrite: true)`, and cleanup of the temporary file in `finally`. `DesktopSetupActions.Create` loads the ledger from `%LOCALAPPDATA%\CodexDeepSeekSetup\install-state.json`; the first successful official or portable installation records the pre-install package state and prior user `CODEX_CLI_PATH`; successful DeepSeek configuration sets `DeepSeekConfigured=true`.

- [ ] **Step 4: Run the focused tests and full Core tests**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.Core.Tests/CodexDeepSeekSetup.Core.Tests.csproj --no-restore`

Expected: all Core tests pass.

- [ ] **Step 5: Commit the ledger**

```bash
git add src/CodexDeepSeekSetup.Core/Workflow tests/CodexDeepSeekSetup.Core.Tests/Workflow src/CodexDeepSeekSetup.App/DesktopSetupActions.cs
git commit -m "feat: record assistant installation provenance"
```

### Task 2: Make wizard state and progress explicit

**Files:**
- Create: `src/CodexDeepSeekSetup.App.Logic/SetupProgress.cs`
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MainWindowViewModelTests.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`

**Interfaces:**
- Produces: `SetupProgress(string Message, double? Percent)`, `InitializeAsync`, `IsInstallStep`, `IsConfigureStep`, `IsCompleteStep`, `IsProgressVisible`, `CanInstall`, `CanConfigure`, and `CanLaunch`.
- Consumes: `IWizardActions.CheckAsync`, install/configure/launch operations, and the install ledger from Task 1.

- [ ] **Step 1: Write failing ViewModel transition tests**

```csharp
[Fact]
public async Task InitializeAsync_WhenCodexIsMissing_ShowsOnlyInstallStep()
{
    var actions = new FakeActions { IsCodexInstalled = false };
    var viewModel = new MainWindowViewModel(actions);

    await viewModel.InitializeAsync(default);

    Assert.True(viewModel.IsInstallStep);
    Assert.False(viewModel.IsConfigureStep);
    Assert.Equal("准备安装 Codex", viewModel.StatusMessage);
}

[Fact]
public async Task InstallAsync_OnSuccess_AdvancesAndClearsProgress()
{
    var viewModel = new MainWindowViewModel(new FakeActions());

    await viewModel.InstallAsync(default);

    Assert.True(viewModel.IsConfigureStep);
    Assert.False(viewModel.IsProgressVisible);
    Assert.Equal(0, viewModel.Progress);
}

[Fact]
public async Task ConfigureAsync_OnFailure_StaysOnConfigureStepWithActionableMessage()
{
    var actions = new FakeActions
    {
        IsCodexInstalled = true,
        ConfigureResult = OperationResult<Unit>.Failure("deepseek.balance.insufficient", "请先充值后重试")
    };
    var viewModel = new MainWindowViewModel(actions);
    await viewModel.InitializeAsync(default);

    await viewModel.ConfigureAsync("sk-test", default);

    Assert.True(viewModel.IsConfigureStep);
    Assert.Equal("请先充值后重试", viewModel.StatusMessage);
}
```

Create separate literal-assertion cases for already-installed startup (`IsConfigureStep=true`), configure success (`IsCompleteStep=true`), launch success (`StatusMessage="Codex 已启动"`), and a blocked second operation while `IsBusy=true`.

- [ ] **Step 2: Run the App.Logic tests and verify RED**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.App.Tests/CodexDeepSeekSetup.App.Tests.csproj --no-restore`

Expected: compilation fails for the new state and progress API.

- [ ] **Step 3: Implement state-derived properties and progress lifecycle**

```csharp
public sealed record SetupProgress(string Message, double? Percent = null);
```

Change install methods to accept `IProgress<SetupProgress>?`. `RunAsync` sets a running message, applies each progress message/percentage, and clears `Progress` plus `IsProgressVisible` in `finally`. Property setters raise dependent visibility and command-state properties whenever `CurrentStep` or `IsBusy` changes.

- [ ] **Step 4: Emit installation phases from `DesktopSetupActions`**

Report these messages in order: `正在检查 Windows`, `正在下载 OpenAI 官方文件`, `正在校验官方签名`, `等待 Windows 管理员授权（不是 API Key）`, `正在注册当前用户`, and `正在准备 Codex CLI`. Preserve byte-level download percentage only during the download phase.

- [ ] **Step 5: Run App.Logic and full solution tests**

Run: `./.tools/dotnet/dotnet test CodexDeepSeekSetup.sln --no-restore`

Expected: all test projects pass.

- [ ] **Step 6: Commit the wizard state model**

```bash
git add src/CodexDeepSeekSetup.App.Logic src/CodexDeepSeekSetup.App/DesktopSetupActions.cs tests/CodexDeepSeekSetup.App.Tests
git commit -m "feat: add state-driven setup progress"
```

### Task 3: Replace the all-at-once WPF layout

**Files:**
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Windows.Tests/MainWindowStartupTests.cs`

**Interfaces:**
- Consumes: visibility, progress, and command-state properties from Task 2.
- Produces: one visible page for each wizard step and automatic startup initialization.

- [ ] **Step 1: Write failing WPF structure tests**

```csharp
[Fact]
public void MainWindow_ContainsOneNamedPanelPerWizardStep()
{
    var window = new MainWindow();

    Assert.NotNull(window.FindName("InstallStepPanel"));
    Assert.NotNull(window.FindName("ConfigureStepPanel"));
    Assert.NotNull(window.FindName("CompleteStepPanel"));
}

[Fact]
public void MainWindow_CompletePanelContainsEveryTerminalAction()
{
    var window = new MainWindow();

    Assert.NotNull(window.FindName("LaunchCodexButton"));
    Assert.NotNull(window.FindName("CloseAssistantButton"));
    Assert.NotNull(window.FindName("CleanupEverythingButton"));
}
```

Parse the loaded controls and assert `ProgressPanel.Visibility` has a binding whose path is `IsProgressVisible`; assert `ApiKeyBox` is a descendant of `ConfigureStepPanel` and not either sibling page.

- [ ] **Step 2: Run the Windows UI tests and verify RED on Windows or compilation failure locally**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.App.Windows.Tests/CodexDeepSeekSetup.App.Windows.Tests.csproj --no-restore`

Expected: the named panels are missing.

- [ ] **Step 3: Implement the three-page XAML**

Use a compact header with three numbered step labels and a single white content card. Bind each named panel's `Visibility` through the built-in `BooleanToVisibilityConverter`. Place the experimental mode and its warning inside the install page only when `CanUsePortable=true`. Put the Key instructions, links, password box, and validation button together in the configure page. Put success text and launch/close/cleanup actions in the complete page. Bind button `IsEnabled` to the corresponding ViewModel state.

- [ ] **Step 4: Initialize automatically and keep confirmations explicit**

Handle `Loaded` once and call `InitializeAsync`. Before installation elevation, show a short Windows authorization explanation. Keep portable-mode confirmation. The cleanup click handler is added in Task 6.

- [ ] **Step 5: Build the WPF application**

Run: `./.tools/dotnet/dotnet build src/CodexDeepSeekSetup.App/CodexDeepSeekSetup.App.csproj --no-restore -c Release -r win-x64`

Expected: build succeeds without warnings introduced by this task.

- [ ] **Step 6: Commit the new layout**

```bash
git add src/CodexDeepSeekSetup.App/MainWindow.xaml src/CodexDeepSeekSetup.App/MainWindow.xaml.cs tests/CodexDeepSeekSetup.App.Windows.Tests/MainWindowStartupTests.cs
git commit -m "feat: present setup as a guided wizard"
```

### Task 4: Add fixed-scope AppX removal

**Files:**
- Modify: `src/CodexDeepSeekSetup.Windows/Packages/CodexPackageManager.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Processes/RestrictedCommandRouter.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Processes/DefaultRestrictedOperations.cs`
- Modify: `src/CodexDeepSeekSetup.App/App.xaml.cs`
- Modify: `tests/CodexDeepSeekSetup.Windows.Tests/Processes/RestrictedCommandRouterTests.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Packages/CodexPackageRemovalTests.cs`

**Interfaces:**
- Produces: `ICodexPackageManager.RemoveAsync(CancellationToken)`, `IRestrictedOperations.RemoveCodexAsync`, and fixed route `elevated appx-remove`.
- Consumes: current controlled elevation path and `IProcessRunner`.

- [ ] **Step 1: Write failing router and package-removal tests**

```csharp
[Fact]
public async Task ExecuteAsync_RoutesOnlyExactAppxRemoveCommand()
{
    var operations = new RecordingRestrictedOperations();
    var router = new RestrictedCommandRouter(operations, requestRoot);

    Assert.Equal(0, await router.ExecuteAsync(["elevated", "appx-remove"], output, error, default));
    Assert.True(operations.RemoveCodexCalled);
    Assert.NotEqual(0, await router.ExecuteAsync(["elevated", "appx-remove", "C:\\"], output, error, default));
}
```

The package-removal test captures the PowerShell process request and asserts it uses the fixed package name `OpenAI.Codex`, removes registered packages, and removes provisioned packages without accepting a package name or path parameter.

- [ ] **Step 2: Run Windows tests and verify RED**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.Windows.Tests/CodexDeepSeekSetup.Windows.Tests.csproj --no-restore --filter "RestrictedCommandRouterTests|CodexPackageRemovalTests"`

Expected: compilation fails because the removal APIs do not exist.

- [ ] **Step 3: Implement the fixed elevated removal operation**

Use a constant PowerShell script that stops `ChatGPT` and `Codex`, removes every detected `OpenAI.Codex` registration with `Remove-AppxPackage -AllUsers`, removes every matching online provisioned package with `Remove-AppxProvisionedPackage -Online`, and exits nonzero if either package class remains. No user-provided package names or filesystem paths enter this script.

- [ ] **Step 4: Generalize elevated startup routing**

In `App.OnStartup`, route every argument list beginning with `elevated` through `RestrictedCommandRouter`; normal launches still create `MainWindow`. `CodexPackageManager.RemoveAsync` invokes the current executable with `runas`, arguments `elevated appx-remove`, and returns an actionable cancellation or failure result.

- [ ] **Step 5: Run Windows tests and full solution tests**

Run: `./.tools/dotnet/dotnet test CodexDeepSeekSetup.sln --no-restore`

Expected: all tests pass.

- [ ] **Step 6: Commit fixed-scope removal**

```bash
git add src/CodexDeepSeekSetup.Windows src/CodexDeepSeekSetup.App/App.xaml.cs tests/CodexDeepSeekSetup.Windows.Tests
git commit -m "feat: add controlled Codex package removal"
```

### Task 5: Delete current-user data safely and prepare self-removal

**Files:**
- Create: `src/CodexDeepSeekSetup.Windows/Cleanup/CleanupRoots.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Cleanup/UserCleanupService.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Cleanup/SelfDeleteFinalizer.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Cleanup/UserCleanupServiceTests.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Cleanup/SelfDeleteFinalizerTests.cs`
- Modify: `src/CodexDeepSeekSetup.Helper/Program.cs`

**Interfaces:**
- Produces: `CleanupRoots.ForCurrentUser()`, `UserCleanupService.CleanAsync`, and `SelfDeleteFinalizer.RunAsync(int parentPid, string appDirectory)`.
- Consumes: install ledger, `ISecretStore`, and only environment-derived managed directories.

- [ ] **Step 1: Write failing cleanup tests using isolated temporary roots**

```csharp
[Fact]
public async Task CleanAsync_DeletesEntireCodexHomeAndManagedRoots()
{
    var roots = testRoots.CreateWithFiles();
    var service = new UserCleanupService(fakeSecrets, roots);

    var result = await service.CleanAsync(previousCodexCliPath: null, default);

    Assert.True(result.IsSuccess, result.ErrorMessage);
    Assert.False(Directory.Exists(roots.CodexHome));
    Assert.False(Directory.Exists(roots.DownloadCache));
    Assert.True(fakeSecrets.DeepSeekCredentialDeleted);
}


[Theory]
[InlineData("drive")]
[InlineData("profile")]
[InlineData("desktop")]
public void Validate_RejectsBroadDeletionRoot(string broadRoot)
{
    var roots = testRoots.WithCodexHome(testRoots.ResolveBroadRoot(broadRoot));

    Assert.Throws<InvalidOperationException>(() => roots.Validate());
}
```

Use three independent environment-variable tests: restore literal `D:\\preexisting\\codex.exe` from the ledger; delete a legacy value under `roots.CliRoot`; preserve literal `D:\\unrelated\\codex.exe` when no ledger exists.

- [ ] **Step 2: Write failing finalizer tests**

```csharp
[Fact]
public async Task RunAsync_DeletesPublishedFilesButPreservesUnknownNeighbor()
{
    var appDirectory = testDirectory.CreatePublishedLayout();
    File.WriteAllText(Path.Combine(appDirectory, "my-notes.txt"), "keep");

    await finalizer.RunAsync(parentPid: 0, appDirectory, default);

    Assert.True(File.Exists(Path.Combine(appDirectory, "my-notes.txt")));
    Assert.DoesNotContain(SelfDeleteFinalizer.PublishedFileNames,
        name => File.Exists(Path.Combine(appDirectory, name)));
}
```

For `RunAsync_RemovesDirectoryWhenNoUnknownFilesRemain`, create only the published layout and assert `Directory.Exists(appDirectory)` is false.

- [ ] **Step 3: Run focused tests and verify RED**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.Windows.Tests/CodexDeepSeekSetup.Windows.Tests.csproj --no-restore --filter "UserCleanupServiceTests|SelfDeleteFinalizerTests"`

Expected: compilation fails because cleanup types do not exist.

- [ ] **Step 4: Implement derived roots and guarded deletion**

`CleanupRoots.ForCurrentUser()` derives `%USERPROFILE%\.codex`, `%LOCALAPPDATA%\CodexDeepSeekSetup`, `%LOCALAPPDATA%\Programs\CodexDeepSeekSetup`, `%LOCALAPPDATA%\Programs\OpenAI\Codex`, and `%LOCALAPPDATA%\Programs\OpenAI\CodexPortable`. Validation uses normalized full paths and rejects filesystem roots, profile root, LocalAppData root, Desktop, empty strings, and duplicates that broaden scope.

`UserCleanupService` deletes the DeepSeek credential, restores/removes the user `CODEX_CLI_PATH` according to the ledger, and recursively deletes only the validated roots. It returns the exact failed item names if cleanup is incomplete.

- [ ] **Step 5: Implement the finalizer and helper entry point**

The main process copies the published single-file helper to `%TEMP%\CodexDeepSeekCleanup-{guid}.exe`. `finalize-cleanup <parentPid> <appDirectory>` waits for the parent process to exit, deletes only `CodexDeepSeekSetup.exe`, `CodexDeepSeekSetup.Helper.exe`, `wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `D3DCompiler_47_cor3.dll`, and `PenImc_cor3.dll`, then removes the directory only if empty. It launches a fixed hidden PowerShell command with its own generated temp path supplied through an environment variable so the temporary helper is deleted after exit.

- [ ] **Step 6: Run focused and full tests**

Run: `./.tools/dotnet/dotnet test CodexDeepSeekSetup.sln --no-restore`

Expected: all tests pass, including preservation of unknown neighboring files.

- [ ] **Step 7: Commit cleanup primitives**

```bash
git add src/CodexDeepSeekSetup.Windows/Cleanup src/CodexDeepSeekSetup.Helper/Program.cs tests/CodexDeepSeekSetup.Windows.Tests/Cleanup
git commit -m "feat: add guarded user and self cleanup"
```

### Task 6: Orchestrate confirmed complete cleanup from the wizard

**Files:**
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MainWindowViewModelTests.cs`

**Interfaces:**
- Produces: `IWizardActions.CleanupAsync(int parentProcessId, string appDirectory, CancellationToken)` and `MainWindowViewModel.CleanupAsync`.
- Consumes: package removal from Task 4, ledger from Task 1, and cleanup services from Task 5.

- [ ] **Step 1: Write failing orchestration tests**

```csharp
[Fact]
public async Task CleanupAsync_WhenElevationFails_KeepsAssistantAvailable()
{
    var actions = new FakeActions { CleanupResult = Failure("cleanup.elevation.cancelled") };
    var viewModel = CompletedViewModel(actions);

    var result = await viewModel.CleanupAsync(123, "D:/assistant", default);

    Assert.False(result.IsSuccess);
    Assert.True(viewModel.IsCompleteStep);
    Assert.False(viewModel.ShouldExit);
}
```

Add a success test that sets `ShouldExit=true` only after all cleanup layers succeed and a failure test that exposes the remaining item names.

- [ ] **Step 2: Run App.Logic tests and verify RED**

Run: `./.tools/dotnet/dotnet test tests/CodexDeepSeekSetup.App.Tests/CodexDeepSeekSetup.App.Tests.csproj --no-restore --filter MainWindowViewModelTests`

Expected: compilation fails for cleanup APIs.

- [ ] **Step 3: Implement cleanup orchestration in strict order**

`DesktopSetupActions.CleanupAsync` first calls `packageManager.RemoveAsync`. Only after success does it close user Codex processes, call `UserCleanupService.CleanAsync`, copy and start the finalizer, and return success. If any step fails, it does not start self-removal. Do not place secret values or arbitrary delete paths in arguments.

- [ ] **Step 4: Add the destructive confirmation UI**

The complete-page cleanup button opens a warning with the exact list: official Codex package, entire `.codex` directory including sessions/plugins/config, DeepSeek credential, CLI/cache/portable files, and the assistant. Use `MessageBoxButton.YesNo`, warning icon, and `MessageBoxResult.No` as the default. For a missing/legacy ledger, show a second warning that an existing Codex installation may predate the assistant. After success call `Application.Current.Shutdown()` immediately so the finalizer can complete.

- [ ] **Step 5: Run full tests and build win-x64**

Run: `./.tools/dotnet/dotnet test CodexDeepSeekSetup.sln --no-restore`

Run: `./.tools/dotnet/dotnet build src/CodexDeepSeekSetup.App/CodexDeepSeekSetup.App.csproj --no-restore -c Release -r win-x64`

Expected: all tests pass and WPF build exits zero.

- [ ] **Step 6: Commit the complete cleanup flow**

```bash
git add src/CodexDeepSeekSetup.App src/CodexDeepSeekSetup.App.Logic tests/CodexDeepSeekSetup.App.Tests
git commit -m "feat: expose confirmed complete cleanup"
```

### Task 7: Document, publish, and verify on Windows

**Files:**
- Modify: `README.md`
- Modify: `docs/privacy.md`
- Create: `docs/windows-cleanup-checklist.md`
- Regenerate: `artifacts/CodexDeepSeekSetup-open-source-win-x64.zip`

**Interfaces:**
- Consumes: completed wizard and cleanup behavior from Tasks 1–6.
- Produces: distributable open-source Windows x64 archive and manual Windows verification record.

- [ ] **Step 1: Document the three-step flow and irreversible cleanup**

State that cleanup removes the complete `.codex` directory and all assistant-created files, requires administrator authorization to remove provisioned packages, preserves unknown files beside the assistant, and cannot be undone.

- [ ] **Step 2: Add the Windows 10 end-to-end checklist**

The checklist must cover fresh normal administrator install, UAC credential prompt wording, automatic step transitions, DeepSeek validation, visible Codex window, cleanup cancellation, complete cleanup confirmation, package/provision removal, credential absence, environment variable restoration, `.codex` absence, self-removal, and preservation of an unknown neighbor file.

- [ ] **Step 3: Run fresh verification**

Run: `./.tools/dotnet/dotnet test CodexDeepSeekSetup.sln --no-restore -c Release`

Run both win-x64 publish commands from `build/Publish-OpenSource.ps1` using `./.tools/dotnet/dotnet`, copy the helper, and archive the output.

Run: `unzip -t artifacts/CodexDeepSeekSetup-open-source-win-x64.zip`

Run: `shasum -a 256 artifacts/CodexDeepSeekSetup-open-source-win-x64.zip`

Expected: zero failed tests, successful publish, no ZIP errors, and a recorded SHA-256 digest.

- [ ] **Step 4: Commit documentation and source changes not yet committed**

```bash
git add README.md docs src tests
git commit -m "docs: explain guided setup and complete cleanup"
```

- [ ] **Step 5: Execute the manual Windows checklist**

Copy the new archive to a Windows 10 22H2 x64 test machine and record each checklist item as pass/fail. Do not claim end-to-end Windows completion until the visible window and self-removal checks pass on that machine.
