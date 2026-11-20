# ModPulse

An SPT mod checker. It reads the mods you have installed and checks them against the Forge for updates, SPT
compatibility and dependency problems, then links you to the right pages. It only checks and links: it never
downloads, installs or changes anything in your game folder.

It comes in two versions with the same checks:

- **ModPulseGUI**: a desktop app. Pick your SPT folder, press *Check mods*, and browse the results by tab.
- **ModPulseCLI**: a command-line tool, for those who prefer a console.

Supports SPT **3.9 through 4.1**. Based on SPT-Check-Mods by Refringe (MIT).

## AI Notice
AI Assisted with the planning of the GUI version, the Front-end of the WPF app, and the creation of the workflow. (and this README)

## Install

1. Download `ModPulseGUI-win-x64.zip` and/or `ModPulseCLI-win-x64.zip` from
   [Releases](https://github.com/RuKira/ModPulse/releases/latest).
2. Unzip. Each is a single standalone exe with nothing else to install (the .NET runtime is bundled).
3. Run it:
   - **GUI**: run `ModPulseGUI.exe` from anywhere, choose your SPT folder and press *Check mods*. Recent folders are
     remembered.
   - **CLI**: put `ModPulseCLI.exe` in your SPT folder (next to `EscapeFromTarkov.exe`) and run it, or run it from
     anywhere with the folder as the first argument:

     ```
     ModPulseCLI.exe "F:\SPT" [--refresh]
     ```

The first check takes a while because the Forge limits how many requests can be made; repeat checks mostly come from
the cache and take seconds (see [Cache and data](#cache-and-data)).

## What it checks

- **Updates.** Newer versions on the Forge that support your SPT version, with Download and Forge page links. An
  update that needs a newer SPT, or that would break another installed mod, is shown as blocked with the reason.
- **Incompatible mods.** Mods with no release for your SPT version.
- **Dependencies.** Missing dependencies, both the ones listed on the Forge and the ones a mod declares in its own DLL
  (some mods need another mod without listing it on the Forge). Also known conflicts between installed mods, and
  dependencies that a pending update adds or drops.
- **Mods it can't find on the Forge**, listed separately so you know which ones to check by hand.
- **Addons and bundled parts.** Forge addons are matched to their parent mod, and mods shipped inside another mod
  aren't reported as unknown.
- **Library mods.** DLLs with no BepInEx plugin of their own that other mods depend on (e.g. Effects Manager Framework)
  are still recognized.
- **Install problems.**
  - Server mods in the client folder or the other way round.
  - 3.x mods in a 4.x install (and vice versa).
  - A leftover `SPT/` folder after upgrading to 4.1.
  - Server and client halves of a mod whose versions or GUIDs don't match.
- **SPT and ModPulse updates.** A newer SPT patch for your version, and a newer ModPulse release.

In the GUI the results are split into tabs: Overview, Updates, Incompatible, Dependencies, Not on Forge, Install notes
and All mods. Each mod has a details pane with its Forge page, download link, source code and its folder on disk.
*Copy summary* puts a plain-text report on your clipboard.

## Supported SPT versions

| SPT | Server folder | Version read from | Support |
|---|---|---|---|
| 4.1.x | `SPT_Runtime/` | `SPTarkov.Server.Core.dll` | Full |
| 4.0.x | `SPT/` | `SPTarkov.Server.Core.dll` | Full |
| 3.11.x | game root | `SPT_Data/Server/configs/core.json` | Full |
| 3.10.x | game root | `SPT_Data/Server/configs/core.json` | **Legacy** |
| 3.9.x | game root | `SPT_Data/Server/configs/core.json` | **Legacy** |

Server mods are read from `<server>/user/mods` (`package.json` mods on 3.x, DLL mods on 4.x). Client mods are read from
`BepInEx/plugins`.

### How compatibility is decided

- **Same minor version counts.** A mod released for any 4.1.x is treated as working on every 4.1.x, even if the Forge
  only tags it for one exact patch. A mod for 4.0 is *not* treated as working on 4.1.
- **When the Forge doesn't list your SPT patch yet**, mods are checked against the newest patch of your version the
  Forge does know, at or below yours. For example, 3.11.5 is checked as 3.11.4. ModPulse says when it does this ("Checked
  as SPT 3.11.4" in the GUI).
- **Legacy (3.9 and 3.10).** Every mod is labeled *(Legacy)* and assumed to match your SPT version, so it's never
  flagged incompatible. An update is only offered when it was published for your SPT version (3.10.x, up to your
  installed patch); builds made for 3.11 or newer are never suggested.

## Ignoring updates

Sometimes a mod's DLL reports a different version than its Forge release, so ModPulse keeps offering an "update" you
already have. You can ignore it:

- **GUI**: *Ignore update* on the mod (and *Stop ignoring* to undo).
- **CLI**: choose *Manage ignored updates* at the end of a run and tick the mods.

An ignore is tied to the exact versions involved. When the mod really updates, or the author fixes the version, the
update shows up again.

### Community ignore list

Known version mismatches are collected in [community/ignored-updates.json](community/ignored-updates.json).

- ModPulse ships with the list as it was at release, so those mismatches are hidden with no download.
- You can also fetch the latest list (the GUI's *Fetch the latest community ignore list* option, or answer yes when
  the CLI asks). New entries are added to your own ignore list.
- After you ignore something in the CLI that the list doesn't have yet, it offers to report it: your browser opens a
  pre-filled GitHub issue for you to review and submit. Entries are checked before they're added to the list.

## Cache and data

Everything ModPulse keeps is in `%AppData%\ModPulse`:

- `cache\forge`: Forge responses. Update status is refreshed after 30 minutes; lookups, searches and dependencies keep
  for 12–24 hours. Errors are never cached. Use *Fresh from Forge* (GUI) or `--refresh` (CLI) to ignore the cache for
  one run, or *Clear Forge cache* in the GUI.
- `ignored-updates.json`: your ignored updates.
- Logs (the GUI's *Open log* opens the folder).

## Privacy

ModPulse reads your mod folders on your PC. Over the network it only:

- queries the Forge API for the mods it finds (name, GUID, version);
- reads the community ignore list and the latest ModPulse release from GitHub.

It sends no account details, telemetry or information about your install anywhere else. Reporting an ignore only opens
a GitHub page in your browser; nothing is sent until you submit it yourself.

## Reporting problems

Open an [issue](https://github.com/RuKira/ModPulse/issues) with your SPT version and what you expected. The log
(`%AppData%\ModPulse`, or *Open log* in the GUI) and the GUI's *Copy summary* help a lot.

---

## Development

Requires the .NET 10 SDK.

```
dotnet build ModPulse.slnx
dotnet test ModPulse.slnx
dotnet publish src/ModPulseCLI/ModPulseCLI.csproj -c Release -r win-x64
dotnet publish src/ModPulseGUI/ModPulseGUI.csproj -c Release -r win-x64
```

`publish` with `-r` produces one standalone single-file exe per front end, plus a zip of it. `build` and `test` stay
framework-dependent. The bundled runtime is needed because SPT 4.1 server mods are built for .NET 10 and ModPulse
loads them to read their metadata.

Layout: `src/ModPulse.Core` (all checking logic), `src/ModPulseCLI` (Spectre.Console), `src/ModPulseGUI` (WPF),
`tests/`.

The GUI's colors come from `src/ModPulseGUI/Themes/Colors.default.xaml`. A personal palette with the same keys can go in
`Themes/Colors.local.xaml` (gitignored); when that file exists it is compiled in instead.

### Releasing

Push a signed version tag; GitHub Actions (`.github/workflows/release.yml`) does the rest:

```
git tag -s v1.2.3 -m "ModPulse 1.2.3"
git push origin v1.2.3
```

The workflow runs the tests, publishes both exes stamped with the tag's version, and creates a GitHub release with
both zips and auto-generated notes. A tag with a suffix (`v1.2.3-beta.1`) becomes a pre-release. The self-update
check reads the latest release's tag, so tags must look like `v1.2.3`. Release builds take their GUI palette from the
`GUI_COLORS_XAML` repository secret (the contents of a `Colors.local.xaml`); without it they use the default palette.

App name, GitHub repo and links live in `src/ModPulse.Core/Configuration/AppIdentity.cs`.
