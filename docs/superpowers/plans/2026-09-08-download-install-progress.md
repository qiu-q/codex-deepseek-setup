# Download and Install Progress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate official payload preparation from Codex installation and present detailed file, phase, speed, ETA, overall progress, and bounded event history.

**Architecture:** `DesktopSetupActions` owns a verified in-memory `CodexPayload` and exposes separate prepare/install methods. `MainWindowViewModel` owns the first-page substage and converts structured `SetupProgress` plus a testable transfer-rate estimator into presentation properties. WPF binds two buttons, file and overall progress bars, phase labels, and an expandable event history.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit, existing OpenAI downloader and package verifier.

**Spec:** `docs/superpowers/specs/2026-09-08-download-install-progress-design.md`

## Global Constraints

- Keep the existing three main wizard pages.
- Never include an API Key or credential value in progress or logs.
- Installation must not download files implicitly.
- A failed or cancelled install preserves the verified payload for direct retry.
- A fresh process must revalidate cached or adjacent files before enabling installation.
- Detailed progress history retains at most 200 entries.
- Windows support remains x64 build 19041+, with build 19045 or Windows 11 recommended.

---

### Task 1: Structured progress and transfer metrics

**Files:**
- Modify: `src/CodexDeepSeekSetup.App.Logic/SetupProgress.cs`
- Create: `src/CodexDeepSeekSetup.App.Logic/DownloadRateEstimator.cs`
- Create: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/DownloadRateEstimatorTests.cs`

**Interfaces:**
- Produces: `SetupPhase`, extended `SetupProgress`, `DownloadRateEstimator.Update(SetupProgress, DateTimeOffset)` and `DownloadMetrics`.
- Consumes: raw current/total byte fields from `DownloadProgress`.

- [ ] Write tests proving two reports for the same file produce literal speed, remaining duration, byte text and percentage; a new file resets the sample; missing total bytes produces no ETA.
- [ ] Run the focused App.Logic tests and verify they fail because the estimator does not exist.
- [ ] Implement the minimal estimator with per-file previous byte/time samples, nonnegative deltas and invariant byte calculations.
- [ ] Extend `SetupProgress` with optional phase, detail, filename, byte count, total count and indeterminate fields while preserving simple call sites.
- [ ] Run all App.Logic tests and commit `feat: add structured transfer progress`.

### Task 2: Split preparation from installation

**Files:**
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`
- Modify: `src/CodexDeepSeekSetup.Core/Downloads/OfficialDownloadService.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MainWindowViewModelTests.cs`
- Modify: `tests/CodexDeepSeekSetup.Core.Tests/Downloads/OfficialDownloadServiceTests.cs`

**Interfaces:**
- Produces: `IWizardActions.PrepareCodexAsync`, `InstallPreparedCodexAsync`, `InstallPreparedPortableAsync`; `CodexInstallStage`; ViewModel `DownloadAsync` and install-only `InstallAsync`.
- Consumes: `CodexPackageVerifier.VerifyAsync` and the in-memory verified `CodexPayload`.

- [ ] Update fakes and write failing ViewModel tests: install disabled initially; successful download enables install; failed download does not; install failure retains ready payload and enables direct retry/portable; retry install does not call prepare again; success enters DeepSeek.
- [ ] Add a downloader test that progress for the MSIX completes before license progress starts, then run it red against concurrent downloads.
- [ ] Change official downloads to deterministic sequential order.
- [ ] Implement the substage state and split action interface in ViewModel.
- [ ] Move environment checking, download/cache selection and signature verification into `PrepareCodexAsync`; report 0–100 overall progress and raw file bytes.
- [ ] Make official and portable install methods reject a missing verified payload and never call the downloader; report authorization, deployment, registration, CLI and state-save phases.
- [ ] Run Core and App.Logic tests and commit `feat: separate payload download from installation`.

### Task 3: Detailed presentation and WPF controls

**Files:**
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MainWindowViewModelTests.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Windows.Tests/MainWindowStartupTests.cs`

**Interfaces:**
- Produces: `ProgressTitle`, `ProgressDetail`, `FileProgress`, `OverallProgress`, `TransferSummary`, `ProgressEntries`, `CanDownload`, `CanInstall`, `IsDownloading`, `IsInstalling`.
- Consumes: structured progress from Task 1 and stage state from Task 2.

- [ ] Write failing tests for phase/detail projection, file percentage, bounded 200-entry history, and distinct download/install command availability.
- [ ] Extend the WPF structure test to require `DownloadCodexButton`, `InstallCodexButton`, `FileProgressBar`, `OverallProgressBar`, `DownloadPhaseLabel`, `InstallPhaseLabel`, and `ProgressLogList`.
- [ ] Run App.Logic tests red and compile the Windows test project red for missing names.
- [ ] Implement ViewModel presentation properties, deduplicated phase event records with local `HH:mm:ss` timestamps, and 200-entry trimming.
- [ ] Replace the first-page combined button and single bar with two substage cards, two independent buttons, file/overall progress, transfer details, and an expandable event log.
- [ ] Update click handlers so download confirmation is network-only and install confirmation explains Windows UAC.
- [ ] Run App.Logic tests and build the Windows UI test assembly; commit `feat: show detailed download and install progress`.

### Task 4: Documentation, regression verification and packages

**Files:**
- Modify: `README.md`
- Modify: `docs/windows-cleanup-checklist.md`
- Regenerate: `artifacts/CodexDeepSeekSetup-open-source-win-x64.zip`
- Regenerate: `artifacts/CodexDeepSeekSetup-internal-win-x64.zip`

**Interfaces:**
- Consumes: completed feature and publish commands.
- Produces: updated Windows x64 self-contained archives and hashes.

- [ ] Document the two explicit first-page phases, displayed progress fields and retry behavior.
- [ ] Add Windows checks for visible byte progress, speed/ETA, UAC transition and no repeated download on installation retry.
- [ ] Run `./.tools/dotnet/dotnet test CodexDeepSeekSetup.sln --no-restore -c Release` and build the Windows UI test project.
- [ ] Publish the helper, open-source app and internal app as self-contained single-file win-x64 outputs.
- [ ] Recreate both ZIP files, run `unzip -t`, record SHA-256 hashes and scan source/artifacts for forbidden embedded provider credentials.
- [ ] Commit documentation and report that real Windows UI behavior still requires the checklist run on Windows 10 22H2.
