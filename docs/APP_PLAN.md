# ModPulse App: Plan

Branch: `app` (local only, uncommitted).

**Status (2026-11-17): phases 1–3 built.**
- **Phase 1 (core split):** done. CLI output on Fika2 matched the pre-split build line for line, apart from the random banner tagline.
- **Phase 2 (shell):** done.
- **Phase 3 (screens):** done, with these exceptions:
  - The ignore list is shown as Ignore/Stop ignoring actions and an "ignored" count, not a separate tab.
  - Settings live in the header checkboxes and the footer links rather than a settings page.

**As built**, the run is split between two channels:
- **Result object.** `ApplicationService.RunAsync` returns a `ModCheckResult`, and both front ends render their results from it.
- **Live channel.** `IModCheckReporter` stays the live channel. The CLI prints through it exactly as before. The GUI's
  `GuiModCheckReporter` turns it into stage, progress and activity-log updates, and ignores the "display results" calls.
- **The one mid-run prompt** (fetch the community list) gets its answer from the GUI's checkbox. This is the middle
  ground between the two options below: results never depend on reporter call order, and the CLI didn't need rewriting.

## Decisions

| | Decision |
|---|---|
| UI framework | **WPF** (.NET 10, `net10.0-windows`). SPT is Windows-only, so cross-platform buys nothing. |
| Shipping | **Two exes, one shared core.** **ModPulseCLI** (`ModPulseCLI.exe`, today's console tool) and **ModPulseGUI** (`ModPulseGUI.exe`, the WPF app), both standalone single-file. Users pick whichever they want. |
| Scope | Same as the CLI: a checker that reports and links. Not a mod manager (no installing, deleting or moving mods). |
| Results | The app and the CLI must give identical results for the same install. All logic lives in the core; the front ends only display. |

## Target layout

```
ModPulse.slnx
src/
  ModPulse.Core/      net10.0 class library: scanning, SPT layout, Forge API + cache, matching,
                      enrichment, legacy policy, compatibility, dependencies, ignore list, self-update check
  ModPulseCLI/        net10.0 exe (ModPulseCLI.exe): Spectre.Console output, prompts, Program.cs
  ModPulseGUI/        net10.0-windows WPF exe (ModPulseGUI.exe)
tests/
  ModPulse.Core.Tests/   today's 250 tests, retargeted at Core
  ModPulseGUI.Tests/     view-model tests (no UI automation)
```

Both exes share the same `%AppData%/ModPulse` folder, so the Forge cache, ignore list and logs carry over between
them. The cache already writes atomically (temp file + move), so running both at once is safe.

## Phase 1: extract the core (no behavior change)

This is the real work. Everything after it is mostly UI.

**The problem:** `ApplicationService` runs the whole check *and* talks to the console through `IModCheckReporter`.
That interface has ~30 methods: progress output, result printing, and interactive prompts
(`PromptFetchRemoteIgnores`, `PromptEndOfRun`, `SelectUpdatesToIgnore`, `PromptReportIgnores`). The pipeline also
checks `Console.IsInputRedirected` directly. A WPF app can't reuse it in that shape.

**The change:**

1. **`ModCheckPipeline` (Core)** replaces the body of `ApplicationService.RunAsync`. It runs the same steps in the
   same order and returns a `ModCheckResult` instead of printing:
   - installation (game root, server folder, kind, SPT version, legacy flag, warnings)
   - SPT update info, ModPulse self-update info
   - misplaced-mod report
   - reconciliation result (server/client pairs, load warnings, unverified mods)
   - the final `List<Mod>` (matched, enriched, legacy-labeled, ignores applied, compatibility checked)
   - dependency analysis
   - a list of diagnostics (warnings/errors raised along the way, each with a severity and message)
2. **Progress** goes through `IProgress<ModCheckProgress>`: the current stage (Validating, Scanning, Matching,
   Enriching, Compatibility, Dependencies) plus done/total for the Forge-bound stages. WPF's `Progress<T>` marshals
   to the UI thread automatically; the CLI maps it onto the existing Spectre progress bars.
3. **Prompts become separate calls** instead of happening mid-run:
   - fetching the community ignore list: `IgnoreListService.FetchCommunityAsync()`, called before the run (CLI asks
     first as it does now; the app has a button and a setting)
   - ignoring updates, opening update pages, reporting ignores: operations on the result, called after the run
4. **`SpectreModCheckReporter` stays in the CLI** and renders a `ModCheckResult`. The CLI's output should not change.
5. **DI:** Core exposes `services.AddModPulseCore()`; each exe adds its own front end on top. Today
   `AddModPulseServices` scans `Assembly.GetExecutingAssembly()` for `[Injectable]` types and also sets the
   User-Agent version from it. After the split, both must point at the Core assembly explicitly, and the CLI
   registers `SpectreModCheckReporter` itself.
6. **Files that stay CLI-only:** `Program.cs` (Ctrl+C handling, end-of-run key prompt), `SpectreModCheckReporter`,
   and its tests. Everything else under `Configuration/`, `Extensions/`, `Logging/`, `Models/`, `Services/` and
   `Utils/` moves to Core with namespaces unchanged, so the move is mostly mechanical.

**Shortcut considered and rejected:** the app could implement `IModCheckReporter` itself and collect the data the
pipeline passes to it (`VersionTable(mods)`, `DependencyResults(...)`), with no refactor. It would work, but it
ties the app to the console's call order and prompt timing, and every future CLI output change would risk breaking
the app. The result-object split is worth doing once, up front.

**Done when:** the CLI's output on `F:\SPT - 4.1-Fika2` matches what it printed before the split (same mod counts,
same incompatible/update/dependency sections), and all existing tests pass against Core.

## Phase 2: app shell

- WPF + **CommunityToolkit.Mvvm** (source-generated view models and commands), **Microsoft.Extensions.Hosting**
  for DI/logging, reusing `AddModPulseCore()`.
- One window, a header with the install picker, and a tab control below for results.
- Theme: your standard app color scheme as a single `ResourceDictionary` (`Themes/ModPulse.xaml`) with styles for
  Window, Button, ComboBox, TextBox, TabItem, DataGrid, ScrollBar and ToolTip. Status colors (update, incompatible,
  legacy, error) are picked to stay readable on the dark background.
- Standalone single-file publish with `EnableCompressionInSingleFile` to keep the WPF exe size down.

## Phase 3: screens (MVP)

**Header / install picker**
- Folder picker + recent installs (stored in `%AppData%/ModPulse/app-settings.json`).
- Auto-detect when the app sits in a game folder, like the CLI.
- Once a folder is picked, show: SPT version, server folder (`SPT/` or `SPT_Runtime/`), a **Legacy** badge for
  3.9/3.10, and any layout warnings (folder mismatch, unsupported version).
- **Check mods** button, plus **Refresh from Forge** (same as `--refresh`) and **Cancel** while running.

**Progress**
- Stage list with the active stage highlighted and a progress bar for the Forge stages. Shows "from cache" when a
  stage made no network calls.

**Result tabs** (one `TabItem` each, with a count in the header)
| Tab | Contents |
|---|---|
| Updates | Mods with an update: installed vs latest, "Open download" / "Open Forge page", "Ignore" (false positive) |
| Incompatible | Installed version doesn't support this SPT: reason, latest compatible version, download link |
| Dependencies | Tree of mod dependencies; missing or conflicting dependencies flagged |
| Not on Forge | Unmatched and unverified mods, with "search on Forge" links |
| Problems | Misplaced mods, cross-installed mods, DLLs that couldn't be read, load warnings |
| All mods | Sortable/filterable grid of everything: name, author, side (server/client), installed, latest, status, Legacy tag |

- Selecting a mod opens a details pane: GUID, file path(s), Forge match, versions and their SPT constraints, the
  reason for its status.
- Ignored updates are shown dimmed with an "Un-ignore" action, rather than hidden.

**Footer / settings**
- Community ignore list: last fetched, "Fetch now", "Fetch automatically before each check" toggle.
- Cache: size, "Clear cache".
- Open log folder, ModPulse version, update banner when a newer release exists on GitHub.

## Phase 4: polish (after MVP)

- Export results (Markdown for Discord / plain text), the app version of sharing the CLI log.
- Remember window size, last tab and sort order.
- Keyboard navigation and screen-reader names on all controls.

## Core work that helps both front ends

These are already known issues. They belong in Core, so the CLI and app get them at the same time. They can happen
on `main` in parallel with this branch.

- **Addons as their own entries.** A mod and its addon in the same plugin folder (BlackDiv `com.blackdiv.tacticaltoaster`
  + Black Div Notifier `ciallo.blackdivinfo`) must both show; the mod must not be hidden by the addon.
- **Addon compatibility.** Forge's addon API has no GUID filter, so addons need another lookup (parent mod's addon
  list or name search).
- **Batch GUID lookups** (Forge accepts comma-separated GUIDs) to cut cold-run time.

## Release

- One GitHub release per version with two assets: `ModPulseCLI-win-x64.zip` and `ModPulseGUI-win-x64.zip`.
- Renaming the CLI's exe from `ModPulse.exe` to `ModPulseCLI.exe` happens with the core split, not before; the
  README's usage line and build commands change with it.
- The self-update check (`UpdateCheckService`) already reads GitHub Releases; it just reports a newer version, so it
  works for both exes unchanged.

## Testing

- Core: existing tests move with the code; add tests for `ModCheckPipeline` stage ordering and for `ModCheckResult`
  contents using the existing fakes (`FakeForgeApiService`).
- App: view-model tests (tab counts, filtering, ignore/un-ignore, cancel) against a fake pipeline.
- Manual: run both exes against `F:\SPT - 4.1-Fika2` (4.1.6) and `F:\SPT - DEV` and compare.

## Open questions

1. **Theme file in the repo.** Your color scheme is kept out of the repo on purpose. Proposed: a committed
   `Themes/Colors.default.xaml` (neutral dark palette) and a gitignored `Themes/Colors.local.xaml` with your colors.
   The project compiles the local file when it exists, otherwise the default, so your builds get your colors and the
   repo never contains them. The alternative is to simply commit your colors as the app's theme.
2. **App icon.** Reuse `check-mods.ico` (Refringe's) or a new ModPulse icon?
