# Codex Storage, Detection, and Maintenance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add visible installed-state detection, selectable download storage, D-drive-preferred AppX placement, and a safe selectable Codex maintenance inventory.

**Architecture:** Keep the wizard orchestration in `MainWindowViewModel`, add focused Windows services for storage targets, package status/movement, and known-artifact inventory, and isolate destructive selection in a maintenance dialog. Elevated operations remain restricted to JSON requests in the existing owned request directory.

**Tech Stack:** C# 12, .NET 8, WPF, PowerShell AppX cmdlets, xUnit

**Spec:** `docs/superpowers/specs/2026-09-09-storage-detection-maintenance-design.md`

## Global Constraints

- Default to D only when it is a ready local fixed NTFS drive; otherwise use the system drive.
- Never modify the computer-wide default AppX volume.
- Never recursively delete a user-selected download directory.
- Never read or display the DeepSeek API Key during inventory.
- Never directly modify a `WindowsApps` directory.

---

### Task 1: Storage Choices and Download Destination

**Files:**
- Create: `src/CodexDeepSeekSetup.Windows/Storage/StorageSelectionService.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Storage/StorageSelectionServiceTests.cs`
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces: `StorageSelectionService.GetInstallDrives()`, `ResolveDefaultDownloadDirectory(string executableDirectory, string fallbackDirectory)`, and wizard properties for the selected paths.

- [ ] Write tests proving D preference, system-drive fallback, and executable-adjacent download fallback.
- [ ] Run the focused tests and confirm failure because the service does not exist.
- [ ] Implement validated storage choices and expose them through the wizard.
- [ ] Run the focused tests and confirm success.

### Task 2: Package Status and AppX Drive Movement

**Files:**
- Create: `src/CodexDeepSeekSetup.Windows/Packages/CodexPackageStatus.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Packages/CodexPackageManager.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Processes/RestrictedCommandRouter.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Processes/DefaultRestrictedOperations.cs`
- Modify: `tests/CodexDeepSeekSetup.Windows.Tests/Processes/RestrictedCommandRouterTests.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Packages/CodexPackageStatusTests.cs`

**Interfaces:**
- Produces: `ICodexPackageManager.GetStatusAsync()` and `MoveCurrentUserPackageAsync(string driveRoot)`.

- [ ] Write tests for status JSON parsing and rejection of non-root or non-fixed target paths.
- [ ] Run the focused tests and confirm the missing behavior fails.
- [ ] Add status probing, restricted volume preparation, and current-user package movement.
- [ ] Run the focused tests and confirm success.

### Task 3: Known Codex Data Inventory and Selective Cleanup

**Files:**
- Create: `src/CodexDeepSeekSetup.Windows/Cleanup/CodexArtifactInventory.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Cleanup/SelectiveCleanupService.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Cleanup/CodexArtifactInventoryTests.cs`
- Create: `tests/CodexDeepSeekSetup.Windows.Tests/Cleanup/SelectiveCleanupServiceTests.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`

**Interfaces:**
- Produces: `CodexArtifact`, `CodexCleanupRequest`, `CodexArtifactInventory.ScanAsync(...)`, and `SelectiveCleanupService.CleanAsync(...)`.

- [ ] Write tests proving only known roots and known download filenames are returned and deleted.
- [ ] Run the focused tests and confirm failure because inventory and selective cleanup are missing.
- [ ] Implement size-tolerant inventory and per-category cleanup without exposing secret values.
- [ ] Run the focused tests and confirm success.

### Task 4: Maintenance User Interface

**Files:**
- Create: `src/CodexDeepSeekSetup.App.Logic/MaintenanceViewModel.cs`
- Create: `src/CodexDeepSeekSetup.App/MaintenanceWindow.xaml`
- Create: `src/CodexDeepSeekSetup.App/MaintenanceWindow.xaml.cs`
- Create: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MaintenanceViewModelTests.cs`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Windows.Tests/MainWindowStartupTests.cs`

**Interfaces:**
- Consumes: inventory and selective cleanup interfaces from Task 3.
- Produces: an always-available “检测与清理” dialog with refresh, selection summary, confirmation, and per-item results.

- [ ] Write failing view-model and window construction tests.
- [ ] Run the focused tests and confirm expected failures.
- [ ] Implement the maintenance dialog and persistent installed-state banner.
- [ ] Run focused tests and confirm success.

### Task 5: Integration and Release

**Files:**
- Modify: `README.md`
- Modify: `docs/windows-test-matrix.md`

**Interfaces:**
- Consumes: all prior tasks.
- Produces: open-source and internal Windows x64 ZIP artifacts.

- [ ] Run all unit tests and Windows WPF startup tests.
- [ ] Publish both build flavors for `win-x64`.
- [ ] Inspect ZIP contents and compute SHA-256 hashes.
- [ ] Document the Windows 10 AppX movement checks that still require a physical test machine.
