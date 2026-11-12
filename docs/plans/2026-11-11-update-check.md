# Update Check Implementation Plan

> **For implementers:** Execute this plan task by task, checking off each
> step. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app checks GitHub once a day for a newer release and updates
itself in one click.

**Architecture:** Pure logic lives in the Core project (tag parsing, release
JSON parsing, hash verification, last-check state) so it is unit tested. A
new `UpdateService` in the app project owns the network call, the download,
and the exe swap. The tray and settings window read a `PendingUpdate` on
`AppState`.

**Tech Stack:** .NET 8 WPF, WinForms tray, `System.Text.Json`,
`System.Net.Http.HttpClient`, `System.Security.Cryptography`. No new packages.

**Spec:** `docs/design/2026-11-11-update-check-design.md`

## Global Constraints

- No new NuGet packages.
- The only network hosts are api.github.com and github.com release assets.
  Nothing is sent except the HTTP requests themselves. User-Agent:
  `PowerPlanSwitcher/<version>`.
- `checkForUpdates` defaults to true. When false, no network call happens.
- All update failures are swallowed and surfaced as text in the status area;
  none may crash or block the app.
- Repo copy rules: plain language, no tool names in committed files.
- The debug app may be running and locking the output exe; stop it before
  building (`Stop-Process -Name PowerPlanSwitcher -ErrorAction SilentlyContinue`).
- Commit after every task. Match the existing commit message style (plain
  sentence, no prefixes).

## Review Focus

1. A tag that is not `X.Y.Z` (`v0.3`, `development`, `v0.3.0-beta` numerics
   equal to current) must not offer an update. — Task 1 tests.
2. A release missing the `.sha256` asset or the exe asset must be ignored,
   not crash the parser. — Task 2 tests.
3. A corrupt or truncated download (hash mismatch) must cancel the update
   and leave the app running. — Task 3 tests.
4. A settings file written before this feature (no `checkForUpdates` field)
   must load with the default true and no problem reported. — Task 5 tests.
5. A half-done swap must not destroy the exe: if moving the new exe in
   fails after the old one was renamed, restore the old name before
   reporting failure. — Task 6 code step includes the restore.

---

### Task 1: Version tag parsing (`PowerPlanSwitcher.Core`)

**Files:**
- Create: `src/PowerPlanSwitcher.Core/UpdateVersion.cs`
- Test: `tests/PowerPlanSwitcher.Core.Tests/UpdateVersionTests.cs`

**Interfaces:**
- Produces: `public static class UpdateVersion` with
  `public static bool TryParse(string? text, out Version version)` and
  `public static bool IsNewer(string? tag, string currentVersionText)`.

- [ ] **Step 1: Write failing tests** — `TryParse` accepts `v0.3.1`, `0.3.1`,
  and `v0.3.1-beta` (suffix stripped); rejects `v0.3`, `development`, empty,
  null. `IsNewer("v0.3.1", "0.3.0")` is true; equal versions false;
  `IsNewer("v0.2.9", "0.3.0")` false; unparseable tag false.
- [ ] **Step 2: Run** `dotnet test powerplan.slnx` — fails to compile:
  `UpdateVersion` does not exist.
- [ ] **Step 3: Implement** — regex `^v?(\d+)\.(\d+)\.(\d+)` anchored at the
  start; build `new Version(major, minor, build)`. `IsNewer` parses both
  sides and compares.
- [ ] **Step 4: Run tests** — pass.
- [ ] **Step 5: Commit** — `Update check: parse release tags`

### Task 2: Release feed parsing (`PowerPlanSwitcher.Core`)

**Files:**
- Create: `src/PowerPlanSwitcher.Core/ReleaseFeed.cs`
- Test: `tests/PowerPlanSwitcher.Core.Tests/ReleaseFeedTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public sealed record ReleaseAssets(string Tag, string ExeUrl,
  string HashUrl, string ReleasePageUrl);` and
  `public static ReleaseAssets? Parse(string json)` in the same file.
  Returns null on any problem; never throws.

- [ ] **Step 1: Write failing tests** using embedded JSON strings: a normal
  release with `tag_name`, `html_url`, and two assets
  (`PowerPlanSwitcher.exe`, `PowerPlanSwitcher.exe.sha256`) parses to the
  four fields; missing the sha256 asset → null; missing the exe asset →
  null; empty `assets` → null; malformed JSON → null; empty string → null.
- [ ] **Step 2: Run** — compile failure: `ReleaseAssets` missing.
- [ ] **Step 3: Implement** — `JsonDocument.Parse` inside try/catch; read
  `tag_name`, `html_url`; walk `assets`, match exact asset names, collect
  `browser_download_url` for each; require both.
- [ ] **Step 4: Run tests** — pass.
- [ ] **Step 5: Commit** — `Update check: read a GitHub release payload`

### Task 3: Hash verification (`PowerPlanSwitcher.Core`)

**Files:**
- Create: `src/PowerPlanSwitcher.Core/HashCheck.cs`
- Test: `tests/PowerPlanSwitcher.Core.Tests/HashCheckTests.cs`

**Interfaces:**
- Produces: `public static class HashCheck` with
  `public static bool Verify(string sha256AssetText, byte[] fileBytes)`.

- [ ] **Step 1: Write failing tests** — hash of known bytes (compute in the
  test with `SHA256.HashData`) written as
  `<hex>  PowerPlanSwitcher.exe` verifies; wrong hash → false; uppercase
  hex → true; text with no hex token → false; null text → false.
- [ ] **Step 2: Run** — compile failure: `HashCheck` missing.
- [ ] **Step 3: Implement** — take the first whitespace-separated token,
  `Convert.FromHexString` (trim trailing whitespace, case-insensitive by
  lowercasing), length check 32 bytes, compare fixed-time-ish with
  `CryptographicOperations.FixedTimeEquals`.
- [ ] **Step 4: Run tests** — pass.
- [ ] **Step 5: Commit** — `Update check: verify the downloaded hash`

### Task 4: Last-check state (`PowerPlanSwitcher.Core`)

**Files:**
- Create: `src/PowerPlanSwitcher.Core/UpdateStateStore.cs`
- Test: `tests/PowerPlanSwitcher.Core.Tests/UpdateStateStoreTests.cs`

**Interfaces:**
- Produces: `public sealed record UpdateState(DateTimeOffset? LastCheckUtc);`
  and `public sealed class UpdateStateStore(string directory)` with
  `public UpdateState Load()` (never throws; missing or corrupt file →
  `new UpdateState(null)`) and `public void Save(UpdateState state)`
  (atomic: temp file then `File.Replace`, best effort, never throws).
  File name: `update.json`, camelCase JSON.

- [ ] **Step 1: Write failing tests** — roundtrip through a temp directory;
  missing file → null LastCheckUtc; corrupt JSON → null; save twice in a
  row leaves a loadable file (covers the replace path).
- [ ] **Step 2: Run** — compile failure: `UpdateStateStore` missing.
- [ ] **Step 3: Implement** — mirror `ConfigStore`'s temp-plus-replace shape;
  simpler schema, same tolerance rules.
- [ ] **Step 4: Run tests** — pass.
- [ ] **Step 5: Commit** — `Update check: remember the last check time`

### Task 5: `checkForUpdates` setting

**Files:**
- Modify: `src/PowerPlanSwitcher.Core/AppConfig.cs`
- Modify: `src/PowerPlanSwitcher.Core/ConfigStore.cs` (KnownRootFields,
  FromJson parse block)
- Test: extend `tests/PowerPlanSwitcher.Core.Tests/ConfigStoreTests.cs` and
  `AppConfigTests.cs`

**Interfaces:**
- Produces: `public bool CheckForUpdates { get; set; } = true;` on
  `AppConfig` (serialized as `checkForUpdates` by the existing camelCase
  policy).

- [ ] **Step 1: Write failing tests** — default is true; a settings file
  with `"checkForUpdates": false` loads false; a file without the field
  loads true with no problem; a wrong-typed value reports the field name and
  keeps the default; saving a config round-trips the value.
- [ ] **Step 2: Run** — fails: property missing / value not honored.
- [ ] **Step 3: Implement** — add the property with default; add
  `"checkForUpdates"` to `KnownRootFields`; add a bool parse block in
  `FromJson` modeled on `startWithWindows`.
- [ ] **Step 4: Run tests** — pass (whole suite, not just the new file).
- [ ] **Step 5: Commit** — `Update check: checkForUpdates setting`

### Task 6: `UpdateService` and `PendingUpdate` state

**Files:**
- Modify: `src/PowerPlanSwitcher/Services/AppState.cs` — add
  `public sealed record PendingUpdate(string Version, string ExeUrl, string
  HashUrl, string ReleasePageUrl);`, `public PendingUpdate? PendingUpdate {
  get; set; }`, `public string? UpdateStatus { get; set; }`, and both in
  `GetChangeSignature()`.
- Create: `src/PowerPlanSwitcher/Services/UpdateService.cs`
- Test: extend `tests/PowerPlanSwitcher.App.Tests/AppStateTests.cs` —
  signature changes when `PendingUpdate` or `UpdateStatus` changes.

**Interfaces:**
- Consumes: `UpdateVersion`, `ReleaseAssets.Parse`, `HashCheck.Verify`,
  `UpdateStateStore`, `AppInfo.Version`, `AppPaths.DataDirectory`.
- Produces: `public sealed class UpdateService(AppState state, string
  dataDirectory)` with:
  - `public Task CheckAsync()` — no-op when `state.Config.CheckForUpdates`
    is false or the last check is under 24 hours ago (inject a clock for
    tests: optional ctor param `Func<DateTimeOffset>? now = null`). GETs
    `https://api.github.com/repos/SquirrelRat/PowerPlanSwitcher/releases/latest`
    with `HttpClient` (15 s timeout, UA header), parses with
    `ReleaseAssets.Parse`, sets `PendingUpdate` when
    `UpdateVersion.IsNewer(tag, AppInfo.Version)` and null otherwise, saves
    the check time, sets `UpdateStatus` to null or a short failure note.
    Never throws.
  - `public Task<bool> ApplyAsync()` — downloads the exe and sha256 assets
    to `<dataDirectory>\update-download.exe(.sha256)`, verifies with
    `HashCheck.Verify`, then swaps: `Environment.ProcessPath` renamed to
    `<exe>.old`, new file moved into its place. If the move-in fails after
    the rename, rename the old file back before reporting. On success:
    `Process.Start` the new exe and return true (the caller shuts down).
    On any failure: set `UpdateStatus` to a short reason; if the failure was
    an unwritable location, also open `ReleasePageUrl` in the browser.
    Returns false. Never throws.
  - `public static void TryDeleteOldExe()` — on startup, best-effort delete
    of `<exe>.old` next to `Environment.ProcessPath`.

- [ ] **Step 1: Write the failing AppState tests** (signature tracks the
  pending update and the status text).
- [ ] **Step 2: Run** — fails: `PendingUpdate` missing.
- [ ] **Step 3: Implement the AppState additions** — run tests, pass, commit
  `Update check: pending-update state`.
- [ ] **Step 4: Implement `UpdateService`** per the Interfaces block; single
  static `HttpClient` created lazily; all IO in `try/catch`; the 24-hour
  gate reads `UpdateStateStore`.
- [ ] **Step 5: Build** — `dotnet build powerplan.slnx`, 0 warnings,
  0 errors (stop the running app first if the exe is locked).
- [ ] **Step 6: Commit** — `Update check: download, verify, and swap`

### Task 7: Tray, settings UI, and wiring

**Files:**
- Modify: `src/PowerPlanSwitcher/Services/TrayIconManager.cs`
- Modify: `src/PowerPlanSwitcher/Settings/SettingsViewModel.cs`
- Modify: `src/PowerPlanSwitcher/Settings/SettingsWindow.xaml` and
  `.xaml.cs`
- Modify: `src/PowerPlanSwitcher/App.xaml.cs`
- Test: extend `tests/PowerPlanSwitcher.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `UpdateService` methods, `AppState.PendingUpdate`,
  `AppConfig.CheckForUpdates`.
- Produces: tray event `public event Action? UpdateRequested;`; VM property
  `public bool CheckForUpdates` (StartWithWindows pattern); VM property
  `public string UpdateLine` — `$"Version {v} available"` when pending,
  else `"Up to date ({AppInfo.Version})"`; VM property
  `public bool CanUpdate`; SettingsWindow ctor gains an `Action
  installUpdate` parameter next to `saveConfig`.

- [ ] **Step 1: Write failing VM tests** — `CheckForUpdates` round-trips
  into `Config.CheckForUpdates`; `UpdateLine` shows the pending version and
  the up-to-date form; `CanUpdate` follows `PendingUpdate`.
- [ ] **Step 2: Run** — fails: members missing.
- [ ] **Step 3: Implement the VM members** (mirror `StartWithWindows`;
  include the new properties in `RefreshFromState`).
- [ ] **Step 4: Tray** — an `Update to X…` item above `Settings...`, visible
  only with a pending update (set in `Refresh`), click raises
  `UpdateRequested`; one balloon per pending version per session
  (`_lastUpdateBalloon` field, mirrors `_lastBalloonedProblem`).
- [ ] **Step 5: Settings window** — a `Check for updates` checkbox under
  General bound like Start with Windows; an update row showing `UpdateLine`
  with an `Install update` button enabled by `CanUpdate`, both with
  automation names and an access key on the button; the button calls the
  `installUpdate` delegate. Placement follows the status card.
- [ ] **Step 6: App wiring** — construct `UpdateService` in
  `InitializeApp`; `TryDeleteOldExe()` on startup; an async check just after
  `AppLog.Info("ready")`; a `DispatcherTimer` at 1 hour calling `CheckAsync`
  (the 24-hour gate lives in the service); tray `UpdateRequested` and the
  window's `installUpdate` both run `ApplyAsync` on the dispatcher and
  `Shutdown()` when it returns true.
- [ ] **Step 7: Full suite + build** — all tests pass, 0 warnings.
- [ ] **Step 8: Commit** — `Update check: tray prompt, settings row, wiring`

### Task 8: Version 0.3.0, docs, release

**Files:**
- Modify: `src/PowerPlanSwitcher/PowerPlanSwitcher.csproj` (Version 0.3.0,
  AssemblyVersion/FileVersion 0.3.0.0)
- Modify: `src/PowerPlanSwitcher/app.manifest` (0.3.0.0)
- Modify: `src/PowerPlanSwitcher/Services/AppInfo.cs` (fallback "0.3.0")
- Modify: `README.md`, `CHANGELOG.md`

- [ ] **Step 1: Version bump** in the four files.
- [ ] **Step 2: README** — version line; a What-it-does bullet; a Settings
  bullet for the toggle and update row; `checkForUpdates` in the config
  sample; Privacy paragraph reworded: contacts GitHub once a day to check
  for updates, no telemetry, toggle under General; two manual-checklist
  entries (pending update shows and installs; toggle off means no request —
  watch the log).
- [ ] **Step 3: CHANGELOG** — new `## 0.3.0` section at the top.
- [ ] **Step 4: Full verification** — stop the running app, `dotnet build`
  0 warnings, `dotnet test` all green, restart the app, confirm the log
  shows the update check.
- [ ] **Step 5: Commit** — `Version 0.3.0: automatic update check and self-update`
- [ ] **Step 6: Tag and push** — `git tag -a v0.3.0 -m "Version 0.3.0"`,
  `git push origin main v0.3.0`, watch the release workflow, verify the
  published assets with `gh release view v0.3.0`.

### Manual smoke test after release

Install the released 0.3.0 exe, wait for the check, then push a hypothetical
0.3.1 tag later and watch the prompt appear — the real end-to-end proof
needs a genuinely newer release, which the next release after this one will
provide.
