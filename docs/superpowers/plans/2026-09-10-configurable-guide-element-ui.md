# Configurable Guide and Element-Style UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the embedded four-card DeepSeek illustration with a remotely configurable, dismissible native step dialog and restyle the WPF application and admin page with restrained Element Plus-inspired tokens.

**Architecture:** A new Core guide client validates a small JSON document and falls back to a built-in catalog. The existing Go promotion service persists and serves guide configuration through authenticated admin and anonymous public endpoints. WPF renders the selected guide in a dedicated modal window and keeps the API key workflow independent of network availability.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit, Go 1.27, `net/http`, embedded HTML/CSS/JavaScript, PowerShell publishing.

**Spec:** `docs/superpowers/specs/2026-09-10-configurable-guide-element-ui-design.md`

## Global Constraints

- Windows target remains Windows 10 build 19041 x64 or newer, with Windows 10 build 19045 recommended.
- Open-source builds never call the remote guide or promotion endpoints.
- Remote guide requests time out after 3 seconds and failure never blocks installation, configuration, or launch.
- Remote content is text plus HTTPS links and PNG/JPEG/WebP images only; no HTML or executable content is accepted.
- A guide contains 1–8 uniquely identified steps; invalid documents are rejected as a whole.
- Existing credential, install, cleanup, proxy/VPN exclusion, advertising privacy, and 85 MB ZIP constraints remain unchanged.

---

### Task 1: Guide document validation and client

**Files:**
- Create: `src/CodexDeepSeekSetup.Core/Guides/GuideModels.cs`
- Create: `src/CodexDeepSeekSetup.Core/Guides/GuideClient.cs`
- Create: `tests/CodexDeepSeekSetup.Core.Tests/Guides/GuideClientTests.cs`

**Interfaces:**
- Produces: `IGuideClient.GetCurrentAsync(CancellationToken)`, `GuideDocument`, `GuideStep`.
- Consumes: `HttpClient`; no dependency on WPF or advertisement types.

- [ ] **Step 1: Write failing client tests**

Cover valid JSON, `204`, timeout/cancellation-safe fallback, malformed JSON, HTTP action/image URLs, duplicate step IDs, zero/nine steps, and field limits. Assert invalid responses return `null` and never expose partial steps.

- [ ] **Step 2: Verify the tests fail for missing guide types**

Run: `dotnet test tests/CodexDeepSeekSetup.Core.Tests/CodexDeepSeekSetup.Core.Tests.csproj -c Release --filter FullyQualifiedName~GuideClientTests`

Expected: compile failure because `CodexDeepSeekSetup.Core.Guides` does not exist.

- [ ] **Step 3: Implement immutable models and the three-second HTTP client**

`GuideDocument` contains `Version`, `Enabled`, `Title`, and `IReadOnlyList<GuideStep>`. `GuideStep` contains `Id`, `Title`, `Body`, `CompletionHint`, `ActionText`, `ActionUrl`, and optional `ImageUrl`. Validate all exact limits from the spec and return `null` for disabled or invalid documents.

- [ ] **Step 4: Run focused and Core tests**

Run the filtered command, then `dotnet test tests/CodexDeepSeekSetup.Core.Tests/CodexDeepSeekSetup.Core.Tests.csproj -c Release`.

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CodexDeepSeekSetup.Core/Guides tests/CodexDeepSeekSetup.Core.Tests/Guides
git commit -m "feat: add configurable onboarding guide client"
```

### Task 2: Guide persistence, API, and admin editor

**Files:**
- Modify: `server/codex-ad/internal/adserver/models.go`
- Modify: `server/codex-ad/internal/adserver/store.go`
- Modify: `server/codex-ad/internal/adserver/server.go`
- Modify: `server/codex-ad/internal/adserver/server_test.go`
- Modify: `server/codex-ad/internal/adserver/admin.html`

**Interfaces:**
- Produces: `GET /api/v1/guide`, `GET/PUT /api/admin/guide`, atomic `guide.json` persistence.
- Consumes: existing authenticated session, CSRF middleware, media upload, `writeJSON`, and atomic JSON writer.

- [ ] **Step 1: Add failing Go API tests**

Test disabled guide returns `204`; authenticated CSRF-protected PUT persists ordered valid steps; anonymous GET returns them in order; invalid HTTP URL, duplicate ID, empty steps and nine steps return `400`; stored file remains unchanged after a rejected update.

- [ ] **Step 2: Verify focused Go tests fail with missing routes**

Run: `go test ./internal/adserver -run Guide -count=1`

Expected: `404` or route-related assertion failures.

- [ ] **Step 3: Implement guide models, atomic store, and handlers**

Use `guide.json` in the existing data directory. Initialize a disabled default document, copy slices when reading, validate before saving, and expose enabled documents only.

- [ ] **Step 4: Verify server behavior**

Run: `go test ./... -count=1 && go vet ./...`.

Expected: all pass with no vet findings.

- [ ] **Step 5: Extend the embedded admin page**

Add Promotion/Operation Guide tabs, Element-style CSS variables, a step editor with add/delete/up/down controls, media upload reuse, live current-step preview, character counters, and concrete server error display. Use `textContent` and form values only; never render configured HTML.

- [ ] **Step 6: Add an embedded-page structural assertion and rerun Go tests**

Assert the served page includes the guide editor IDs and the existing promotion editor. Run `go test ./... -count=1`.

- [ ] **Step 7: Commit**

```bash
git add server/codex-ad/internal/adserver
git commit -m "feat: manage remote onboarding guides"
```

### Task 3: Guide selection and dialog state

**Files:**
- Modify: `src/CodexDeepSeekSetup.App.Logic/BuildFlavor.cs`
- Modify: `src/CodexDeepSeekSetup.App.Logic/DeepSeekGuideCatalog.cs`
- Create: `src/CodexDeepSeekSetup.App.Logic/GuideDialogViewModel.cs`
- Modify: `src/CodexDeepSeekSetup.App.Logic/MainWindowViewModel.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Tests/BuildFlavorTests.cs`
- Create: `tests/CodexDeepSeekSetup.App.Tests/GuideDialogViewModelTests.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Produces: `BuildFlavorOptions.GuideEndpoint`, built-in `GuideDocument`, `GuideDialogViewModel` navigation state, and `MainWindowViewModel.LoadGuideAsync`/auto-open state.
- Consumes: Task 1 `IGuideClient` and guide models.

- [ ] **Step 1: Write failing flavor, fallback, and navigation tests**

Assert internal builds have the guide endpoint, open-source builds have none, fallback always has four valid steps, first/previous/next/last behavior is bounded, and dismissal prevents a second automatic opening during the same process.

- [ ] **Step 2: Verify tests fail for missing APIs**

Run: `dotnet test tests/CodexDeepSeekSetup.App.Tests/CodexDeepSeekSetup.App.Tests.csproj -c Release --filter "FullyQualifiedName~Guide|FullyQualifiedName~BuildFlavor"`.

Expected: compile failures for the new properties and view model.

- [ ] **Step 3: Implement flavor routing, fallback catalog, and dialog state**

Internal endpoint is `https://www.qiuqiuqiu.top/xxx/codex-ad/`; open-source endpoint is `null`. The state object exposes current item/index, `CanGoPrevious`, `IsLastStep`, and non-repeating auto-open state without any WPF dependency.

- [ ] **Step 4: Run App.Logic tests**

Run: `dotnet test tests/CodexDeepSeekSetup.App.Tests/CodexDeepSeekSetup.App.Tests.csproj -c Release`.

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CodexDeepSeekSetup.App.Logic tests/CodexDeepSeekSetup.App.Tests
git commit -m "feat: add onboarding guide workflow state"
```

### Task 4: Native step dialog and Element-style WPF shell

**Files:**
- Delete: `src/CodexDeepSeekSetup.App/DeepSeekVisualGuide.xaml`
- Delete: `src/CodexDeepSeekSetup.App/DeepSeekVisualGuide.xaml.cs`
- Create: `src/CodexDeepSeekSetup.App/ElementStyles.xaml`
- Create: `src/CodexDeepSeekSetup.App/GuideDialog.xaml`
- Create: `src/CodexDeepSeekSetup.App/GuideDialog.xaml.cs`
- Modify: `src/CodexDeepSeekSetup.App/App.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml`
- Modify: `src/CodexDeepSeekSetup.App/MainWindow.xaml.cs`
- Modify: `tests/CodexDeepSeekSetup.App.Windows.Tests/WindowsUiStructureTests.cs`

**Interfaces:**
- Produces: reusable WPF Element-style resources and a dismissible guide dialog.
- Consumes: Task 3 state and Task 1 guide/image validation.

- [ ] **Step 1: Change the Windows UI structural test first**

Require `GuideDialog`, close/previous/next/open controls, the “查看操作引导” entry, 4–6px card radii, `#409EFF`, and absence of `DeepSeekVisualGuide` from `MainWindow.xaml`.

- [ ] **Step 2: Verify the structural test fails**

On a Windows-capable runtime run the Windows UI test; on macOS run its source/XAML assertions through the existing cross-platform structural test project if available. Expected: missing dialog/resource assertions fail.

- [ ] **Step 3: Create styles and dialog, then wire the guide lifecycle**

Load guide asynchronously when entering the DeepSeek step, auto-open once, allow immediate close, reopen from the main page, load each image on demand with a visible text fallback, open only HTTPS action URLs on explicit click, and return focus to the API key box after completion.

- [ ] **Step 4: Restyle all main sections**

Replace the black masthead and 10–14px cards with the design tokens from the spec. Preserve existing control names, bindings, progress visibility, maintenance behavior, and event handlers unless the structural test explicitly replaces them.

- [ ] **Step 5: Cross-build WPF and run available tests**

Run: `dotnet build src/CodexDeepSeekSetup.App/CodexDeepSeekSetup.App.csproj -c Release -r win-x64 --self-contained true` and the full solution tests.

Expected: zero warnings/errors and all executable tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/CodexDeepSeekSetup.App tests/CodexDeepSeekSetup.App.Windows.Tests
git commit -m "feat: refresh installer UI and onboarding dialog"
```

### Task 5: Documentation, deployment, and release verification

**Files:**
- Modify: `README.md`
- Modify: `server/codex-ad/README.md`
- Modify: `docs/windows-test-matrix.md`

**Interfaces:**
- Consumes all prior tasks; produces operator instructions and release evidence.

- [ ] **Step 1: Update user and operator documentation**

Document the dismissible/reopenable guide, internal remote configuration, open-source/offline fallback, admin guide editor, privacy guarantees, and Windows regression cases.

- [ ] **Step 2: Run the complete verification suite**

Run full .NET tests, WPF cross-build, Go tests, `go vet`, Linux static server build, PowerShell syntax parse, `git diff --check`, then internal publish followed by open-source publish to prove obfuscation isolation.

- [ ] **Step 3: Verify release reports and size budgets**

Confirm helper ≤25 MB and both ZIPs ≤85 MB without bundled official MSIX. Record exact SHA-256 values from generated files.

- [ ] **Step 4: Deploy the updated Go server safely**

Build Linux amd64 binary, upload it without placing credentials in the repository or command history, replace the service binary, restart systemd, and verify public health/admin/guide endpoints. Do not change Nginx unless routing requires it; if changed, back up and run `nginx -t` before reload.

- [ ] **Step 5: Commit documentation and report the Windows-only validation boundary**

```bash
git add README.md server/codex-ad/README.md docs/windows-test-matrix.md
git commit -m "docs: explain configurable onboarding guide"
```

Do not claim native Windows interaction testing from macOS. Provide the exact Windows 10/11 scenarios still requiring physical/VM verification.
