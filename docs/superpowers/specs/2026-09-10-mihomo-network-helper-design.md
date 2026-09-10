# Mihomo Network Helper Design

## Goal

Add an optional, reversible network connection step to the Windows installer so a user can import their own HTTPS subscription and launch Codex through a local Mihomo proxy without needing direct GitHub access. Publish the installer source and release on GitHub without advertisements, embedded subscriptions, nodes, credentials, or server secrets.

## Product boundary

- The feature is available in both builds; the open-source build remains free of advertisements and telemetry.
- No subscription URL, node, route, VPN driver, DNS override, or proxy server credential is embedded in either build.
- The user must explicitly enable the feature and paste a subscription URL they are authorized to use.
- The assistant does not bypass OpenAI, Google, DeepSeek, or Microsoft authentication and does not claim to bypass regional or legal restrictions.
- Mihomo runs without TUN mode. The helper changes only the current user's Windows Internet Settings proxy, never WinHTTP machine settings, DNS, routes, firewall rules, or network adapters.

## Distribution

Use the official unmodified Mihomo `v1.19.30` Windows compatible x64 archive:

- Filename: `mihomo-windows-amd64-compatible-v1.19.30.zip`
- SHA-256: `289fde5e29d37a5b3326480590d8b3551c5bf7f8737290355c19bce74d57a563`
- Primary URL: `https://www.qiuqiuqiu.top/xxx/codex-network/v1.19.30/mihomo-windows-amd64-compatible-v1.19.30.zip`
- Official fallback: `https://github.com/MetaCubeX/mihomo/releases/download/v1.19.30/mihomo-windows-amd64-compatible-v1.19.30.zip`

The server directory also exposes `mihomo-v1.19.30-source.tar.gz`, `LICENSE-GPL-3.0.txt`, and `manifest.json`. The manifest contains version, filenames, byte sizes, SHA-256 hashes, upstream URLs, and license metadata. Nginx serves this directory as static files over HTTPS. The assistant pins the archive hash in its build configuration; it never trusts a remote hash alone.

## User flow

The installer adds a “网络连接（可选）” card between Codex installation and DeepSeek configuration:

1. The user selects “启用网络连接” and pastes an HTTPS subscription URL.
2. “下载并测试” downloads Mihomo from the primary mirror, falls back to the official URL, verifies the pinned hash, safely extracts only the expected executable, and downloads the subscription.
3. The assistant creates a local provider file and a generated Mihomo configuration with `mixed-port: 17890`, loopback-only access, TUN disabled, LAN disabled, an automatic latency group, private-address direct rules, and a final proxy rule.
4. “启动网络连接” launches a separate `CodexNetworkHelper.exe`. The helper starts Mihomo, waits for the mixed port, records the user's existing proxy settings, applies `127.0.0.1:17890`, and broadcasts the Windows settings change.
5. The normal Codex flow continues. The UI always shows whether the proxy is stopped, starting, active, or needs repair.
6. “停止并恢复” stops Mihomo and restores exactly the captured proxy values. A stale recovery marker is repaired automatically the next time either executable starts.

The subscription URL is stored in Windows Credential Manager under `CodexDeepSeekSetup:ProxySubscription`. It is never written to logs, TOML, JSON state, command-line arguments, or crash messages. The downloaded provider file necessarily contains proxy credentials; its directory is restricted to the current Windows user and is clearly listed in maintenance cleanup.

## Components

### Core

- `ProxyRuntimeOptions` describes the pinned version, mirror URL, fallback URL, expected hash, mixed port, and data paths.
- `ProxyRuntimeDownloader` downloads to `.partial`, rejects redirects outside the two approved HTTPS hosts, verifies SHA-256, and atomically promotes the archive.
- `SubscriptionImporter` accepts only HTTPS, downloads with a size limit, rejects HTML/error pages, and atomically saves the provider content.
- `MihomoConfigWriter` generates deterministic YAML. It does not concatenate unescaped subscription content into YAML.

### Windows integration

- `WindowsUserProxyManager` snapshots `ProxyEnable`, `ProxyServer`, `ProxyOverride`, and `AutoConfigURL`, writes a recovery marker, applies the local proxy, broadcasts `WM_SETTINGCHANGE`, and restores idempotently.
- `ProxyProcessSupervisor` starts Mihomo with a fixed executable and data directory, never invokes a shell, waits for the loopback port, and terminates only the process whose PID and executable path match its state file.
- `ProxySecretStore` reuses the existing Windows Credential Manager boundary for the subscription URL.

### Network helper

`CodexDeepSeekSetup.NetworkHelper.exe` is a trimmed, self-contained console/hidden-window process. It accepts only fixed verbs (`start`, `stop`, `repair`, `remove`). The subscription URL is read directly from Windows Credential Manager and never appears in its command line. A per-user Run entry restarts the owned Mihomo process after login; each restart restores any stale snapshot before applying the new local proxy.

### WPF integration

The existing Element-style UI gains a compact optional card, explicit consent, a masked subscription field with show/hide control, progress, a connection test result, and stop/restore action. Proxy failure never blocks Codex installation or DeepSeek configuration. Closing the installer while the helper is active warns that the proxy will continue until Codex exits or the user stops it.

### Cleanup

Maintenance inventory lists the Mihomo archive, runtime directory, provider cache, credential, recovery marker, helper, and active process separately. Removing the runtime first stops the matching process and restores the prior system proxy. Self-delete never leaves Windows pointing at a dead local proxy.

## Open-source publication

Create public repository `qiu-q/codex-deepseek-setup` using the existing MIT license. Before each push, scan tracked files and Git history for API keys, subscription URLs, passwords, payloads, downloaded MSIX files, and generated artifacts. Publish source on the `main` branch and create release `v1.2.0` with the open-source Windows ZIP and its SHA-256 file. The release description states that binaries are unsigned development builds, the proxy is optional and user-supplied, and the open-source build has no advertisement or telemetry.

## Error handling

- Mirror unavailable: try the pinned official URL, then show both failures without leaking URLs containing credentials.
- Hash mismatch: delete the partial/archive and refuse to run it.
- Subscription invalid or oversized: preserve the previous working provider and show a classified error.
- Port occupied: do not change Windows proxy settings.
- Mihomo exits during startup: restore settings immediately and show the last bounded, redacted stderr lines.
- Restore fails: keep the recovery marker, show the exact manual Windows path, and retry on next launch.
- Existing user proxy/PAC: preserve it and restore byte-for-byte; never merge it with the local proxy.

## Verification

- Unit tests cover URL allowlisting, redirect rejection, hash verification, ZIP traversal rejection, subscription size/content validation, deterministic YAML, secret redaction, proxy snapshot/restore, stale recovery, PID/path validation, and cleanup ordering.
- WPF tests cover optional visibility, masked input, state transitions, and non-blocking failures.
- Windows 10 19045 and Windows 11 smoke tests cover direct mirror download, official fallback, import, startup, system proxy activation, Codex launch, normal stop, crash recovery, reboot recovery, existing PAC restoration, cleanup, and self-delete.
- Release verification checks ZIP size, hashes, absence of subscription material, absence of Mihomo in the open-source ZIP, and correct GPL/source links on the mirror.
