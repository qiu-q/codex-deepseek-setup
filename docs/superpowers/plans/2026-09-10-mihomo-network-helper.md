# Mihomo Network Helper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reversible internal-build Mihomo network helper, mirror its pinned official artifacts on the user's server, and publish the current open-source installer on GitHub.

**Architecture:** Core services download and validate a pinned Mihomo archive and subscription, Windows services snapshot/restore per-user proxy settings, and a small helper owns Mihomo's lifetime. WPF exposes an optional internal-only step, while cleanup removes every owned artifact in dependency order.

**Tech Stack:** C# 12, .NET 8 WPF, Windows Registry and Credential Manager APIs, xUnit, PowerShell, Nginx, GitHub CLI, Mihomo v1.19.30.

**Spec:** `docs/superpowers/specs/2026-09-10-mihomo-network-helper-design.md`

## Global Constraints

- Internal build only; open-source build contains no active proxy feature.
- Accept only user-supplied HTTPS subscription URLs and never log or serialize the URL.
- Pin Mihomo v1.19.30 archive SHA-256 `289fde5e29d37a5b3326480590d8b3551c5bf7f8737290355c19bce74d57a563`.
- No TUN, driver, DNS, route, firewall, WinHTTP, or machine-wide proxy changes.
- Always restore the current user's exact prior proxy settings before removing or stopping runtime files.
- Mirror the corresponding GPL-3.0 license and source archive alongside the unmodified binary.

---

### Task 1: Publish current open-source release

**Files:**
- Verify: all tracked source files and Git history
- Use: `artifacts/CodexDeepSeekSetup-open-source-win-x64.zip`
- Use: `artifacts/CodexDeepSeekSetup-open-source-win-x64.zip.sha256.txt`

**Interfaces:**
- Consumes: authenticated `gh` account `qiu-q` and the verified v1.1.0 open-source build.
- Produces: public repository `qiu-q/codex-deepseek-setup` with default branch `main` and release `v1.1.0`.

- [ ] Scan tracked content and patch history for `sk-`, `agt_codex_`, `OwO=`, server passwords, private keys, payloads, and subscription URLs; abort publication on any secret match.
- [ ] Run the full .NET tests and both release builds; confirm the open-source ZIP contains only the two EXEs and report files and contains no proxy or advertisement endpoint activation.
- [ ] Create the public repository with `gh repo create qiu-q/codex-deepseek-setup --public --description "Windows installer assistant for OpenAI Codex and DeepSeek"`, add it as `origin-public`, and push the reviewed current commit as `HEAD:main` without altering the existing local branch or remote.
- [ ] Create GitHub release `v1.1.0` with both ZIP and SHA-256 assets and an unsigned-development-build warning.
- [ ] Verify the repository and release are publicly readable with unauthenticated HTTPS requests.

### Task 2: Deploy the pinned Mihomo mirror

**Files:**
- Create remotely: `/var/www/codex-network/v1.19.30/manifest.json`
- Upload remotely: `/var/www/codex-network/v1.19.30/mihomo-windows-amd64-compatible-v1.19.30.zip`
- Upload remotely: `/var/www/codex-network/v1.19.30/mihomo-v1.19.30-source.tar.gz`
- Upload remotely: `/var/www/codex-network/v1.19.30/LICENSE-GPL-3.0.txt`
- Modify remotely: active Nginx HTTPS server configuration

**Interfaces:**
- Consumes: exact official artifacts and hashes recorded in the spec.
- Produces: HTTPS static URLs beneath `/xxx/codex-network/v1.19.30/`.

- [ ] Build `manifest.json` locally with version, byte sizes, SHA-256 values, upstream URLs, source URL, and `GPL-3.0-only` license identifier.
- [ ] Upload all four files to a temporary server directory and verify their hashes on the server.
- [ ] Back up the active Nginx configuration, add a read-only alias for `/xxx/codex-network/`, add `nosniff` and bounded cache headers, run `nginx -t`, and reload only after success.
- [ ] Verify binary, source, license, and manifest over public HTTPS and compare downloaded hashes to the pinned local values.

### Task 3: Implement pinned runtime download and subscription import

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/Proxy/ProxyRuntimeOptions.cs`
- Create: `src/CodexDeepSeekSetup.Core/Proxy/ProxyRuntimeDownloader.cs`
- Create: `src/CodexDeepSeekSetup.Core/Proxy/SubscriptionImporter.cs`
- Create: `src/CodexDeepSeekSetup.Core/Proxy/MihomoConfigWriter.cs`
- Test: `tests/CodexDeepSeekSetup.Core.Tests/Proxy/ProxyRuntimeDownloaderTests.cs`
- Test: `tests/CodexDeepSeekSetup.Core.Tests/Proxy/SubscriptionImporterTests.cs`
- Test: `tests/CodexDeepSeekSetup.Core.Tests/Proxy/MihomoConfigWriterTests.cs`

**Interfaces:**
- Produces: `Task<OperationResult<string>> EnsureRuntimeAsync(ProxyRuntimeOptions, IProgress<ProxyDownloadProgress>?, CancellationToken)`, `Task<OperationResult<string>> ImportAsync(Uri, string, CancellationToken)`, and `OperationResult<Unit> Write(string dataDirectory, int mixedPort)`.

- [ ] Write failing tests for primary/fallback behavior, final-host validation, pinned hash mismatch deletion, atomic promotion, ZIP traversal, HTTPS-only subscriptions, 8 MiB limit, HTML rejection, prior-provider preservation, and deterministic safe YAML.
- [ ] Run the focused Core tests and confirm failures identify missing proxy types.
- [ ] Implement the minimal downloader, importer, archive extractor, and YAML writer without adding a YAML dependency.
- [ ] Run the focused tests, then all Core tests, and confirm zero failures.
- [ ] Commit as `feat: add pinned mihomo runtime provisioning`.

### Task 4: Implement reversible Windows proxy ownership

**Files:**
- Create: `src/CodexDeepSeekSetup.Windows/Proxy/UserProxySnapshot.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Proxy/WindowsUserProxyManager.cs`
- Create: `src/CodexDeepSeekSetup.Windows/Proxy/ProxyProcessSupervisor.cs`
- Test: `tests/CodexDeepSeekSetup.Windows.Tests/Proxy/WindowsUserProxyManagerTests.cs`
- Test: `tests/CodexDeepSeekSetup.Windows.Tests/Proxy/ProxyProcessSupervisorTests.cs`

**Interfaces:**
- Produces: `CaptureAndApplyAsync(int port, CancellationToken)`, `RestoreAsync(CancellationToken)`, `RepairIfStaleAsync(CancellationToken)`, `StartAsync(string executable, string dataDirectory, int port, CancellationToken)`, and `StopAsync(CancellationToken)`.

- [ ] Write failing tests using registry/process/port abstractions for exact snapshot restoration, idempotent restore, stale marker recovery, occupied port refusal, process path/PID validation, bounded stderr, and never killing unrelated processes.
- [ ] Run focused Windows tests and confirm failure before implementation.
- [ ] Implement registry snapshot serialization, `WM_SETTINGCHANGE`, recovery markers, direct process launch, loopback readiness, and verified termination.
- [ ] Run focused and full Windows tests with zero failures.
- [ ] Commit as `feat: manage reversible windows user proxy`.

### Task 5: Add the trimmed network helper and internal UI flow

**Files:**
- Create: `src/CodexDeepSeekSetup.NetworkHelper/CodexDeepSeekSetup.NetworkHelper.csproj`
- Create: `src/CodexDeepSeekSetup.NetworkHelper/Program.cs`
- Create: `src/CodexDeepSeekSetup.NetworkHelper/NetworkHelperCommandRunner.cs`
- Modify: `CodexDeepSeekSetup.sln`
- Modify: `src/CodexDeepSeekSetup.App.Logic/BuildFlavor.cs`
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`
- Test: `tests/CodexDeepSeekSetup.App.Tests/ViewModels/MainWindowViewModelTests.cs`
- Test: `tests/CodexDeepSeekSetup.App.Windows.Tests/MainWindowStartupTests.cs`

**Interfaces:**
- Consumes: proxy provisioning and Windows ownership APIs from Tasks 3–4.
- Produces: internal-only consent, subscription, download/test/start/stop state and `CodexNetworkHelper.exe`.

- [ ] Write failing view-model and real-WPF structure tests for internal-only visibility, explicit consent, masked subscription field, button gating, progress, classified errors, and stop/restore.
- [ ] Add helper command validation tests proving only `start`, `stop`, and `repair` with owned local state files are accepted.
- [ ] Implement build-flavor options, WPF card, action wiring, credential storage, startup repair, and helper lifecycle.
- [ ] Publish the helper trimmed, self-contained, single-file, compressed, and below 15 MB.
- [ ] Run all .NET tests and Windows cross-builds with zero failures.
- [ ] Commit as `feat: add optional internal network step`.

### Task 6: Extend inventory, cleanup, release, and documentation

**Files:**
- Modify: `src/CodexDeepSeekSetup.Windows/Cleanup/CleanupRoots.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Cleanup/CodexArtifactInventory.cs`
- Modify: `src/CodexDeepSeekSetup.Windows/Cleanup/SelectiveCleanupService.cs`
- Modify: `src/CodexDeepSeekSetup.App/DesktopSetupActions.cs`
- Modify: `src/CodexDeepSeekSetup.App/MaintenanceWindow.xaml`
- Modify: `build/Publish-Internal.ps1`
- Modify: `build/Publish-OpenSource.ps1`
- Modify: `README.md`
- Modify: `docs/windows-test-matrix.md`
- Test: corresponding cleanup and WPF test files

**Interfaces:**
- Consumes: owned proxy paths, credential target, process state, and recovery API.
- Produces: safe ordered cleanup and refreshed internal/open-source release archives.

- [ ] Write failing cleanup tests proving restore and process stop occur before credential/runtime deletion and unknown neighboring files survive.
- [ ] Implement inventory entries, danger flags, ordered removal, active-state UI, and self-delete protection.
- [ ] Update publish scripts so only the internal build contains `CodexNetworkHelper.exe`; fail if the open-source archive contains proxy runtime endpoints or the network helper.
- [ ] Document consent, privacy, GPL/source mirror, recovery, manual stop, and Windows test cases.
- [ ] Run full .NET/Go/PowerShell verification, publish both ZIPs, record sizes and SHA-256, and perform Windows 10/11 smoke tests before calling the proxy build release-ready.
- [ ] Commit as `feat: complete optional mihomo network integration`.
