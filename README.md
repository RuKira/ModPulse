# ModPulse

SPT mod checker: checks your installed mods for updates, SPT compatibility and dependency problems against the
Forge API. It only checks and links; it is not a mod manager.

Two front ends, same checks (both built on `ModPulse.Core`):

- **ModPulseGUI**: a desktop app. Pick your SPT folder, press *Check mods*, and browse the results by tab: Updates,
  Incompatible, Dependencies, Not on Forge, Install notes, All mods.
- **ModPulseCLI**: the command-line tool.

Based on SPT-Check-Mods by Refringe (MIT).

## Supported SPT versions

| SPT | Server folder | Version read from | Support |
|---|---|---|---|
| 4.1.x | `SPT_Runtime/` | `SPTarkov.Server.Core.dll` | Full |
| 4.0.x | `SPT/` | `SPTarkov.Server.Core.dll` | Full |
| 3.11.x | game root | `SPT_Data/Server/configs/core.json` | Full |
| 3.10.x | game root | `SPT_Data/Server/configs/core.json` | **Legacy** |
| 3.9.x | game root | `SPT_Data/Server/configs/core.json` | **Legacy** |

Run it from the game root (or pass the game root or the 4.x server folder as the first argument). Server mods are
read from `<server>/user/mods`: `package.json` mods on 3.x, DLL mods on 4.x (both the 4.0 `AbstractModMetadata`
and 4.1 `IModMetadata` styles). Client mods are read from `BepInEx/plugins`.

- If both `SPT/` and `SPT_Runtime/` exist (e.g. a 4.0 folder left behind after upgrading), the newer server is used,
  and a folder that doesn't match its version is called out.
- A 3.x mod left in a 4.x install, or a 4.x mod in a 3.x install, is flagged because the server can't load it.

### Legacy (below 3.11)

- Every mod is labeled **(Legacy)**.
- The installed mod version is assumed to match your SPT version, so legacy mods are never flagged incompatible.
- An update is only offered when Forge confirms that exact version supports your SPT version. Builds made for
  3.11+ are never suggested.

## Forge cache

Forge rate-limits requests, so responses are cached on disk in `%AppData%/ModPulse/cache/forge`. A repeat check
mostly runs from the cache (seconds instead of minutes). Update status is refreshed after 30 minutes; lookups,
searches and dependencies keep for 12–24 hours. Errors are never cached.

```
ModPulseCLI.exe [SptPath] [--refresh]
```

`--refresh` ignores the cache for that run (fresh answers are still saved for next time).

## Requirements

None. Releases are a standalone single-file exe with the .NET 10 runtime bundled (SPT 4.1 server mods are built for
.NET 10, and ModPulse loads them to read their metadata). Building from source needs the .NET 10 SDK.

## Build

```
dotnet build ModPulse.slnx
dotnet test ModPulse.slnx
dotnet publish src/ModPulseCLI/ModPulseCLI.csproj -c Release -r win-x64
dotnet publish src/ModPulseGUI/ModPulseGUI.csproj -c Release -r win-x64
```

`publish` with `-r` produces one standalone exe per front end (plus a zip of it). `build` and `test` stay
framework-dependent.

Layout: `src/ModPulse.Core` (all checking logic), `src/ModPulseCLI`, `src/ModPulseGUI` (WPF), `tests/`.

The GUI's colors come from `src/ModPulseGUI/Themes/Colors.default.xaml`. A personal palette with the same keys can go in
`Themes/Colors.local.xaml` (gitignored); when that file exists it is compiled in and used instead.

## Releasing

Push a version tag; GitHub Actions (`.github/workflows/release.yml`) does the rest:

```
git tag -s v1.0.1 -m "ModPulse 1.0.1"
git push origin v1.0.1
```

The workflow runs the tests, publishes both exes stamped with the tag's version, and creates a GitHub release with
`ModPulseCLI-win-x64.zip` and `ModPulseGUI-win-x64.zip` attached and auto-generated notes. A tag with a suffix
(`v1.1.0-beta.1`) becomes a pre-release. The self-update check reads the latest release's tag, so tags must look like
`v1.2.3`.

The GUI palette for release builds comes from the repository secret `GUI_COLORS_XAML` (the full contents of a
`Colors.local.xaml`). Without the secret, releases use the neutral `Colors.default.xaml`.

## Identity

`src/ModPulse.Core/Configuration/AppIdentity.cs` holds the name, GUID and GitHub owner/repo used for self-update checks (GitHub
Releases) and issue links. The GUID is a placeholder until the final one is chosen.
