# ModPulse

SPT mod checker: checks your installed mods for updates, SPT compatibility and dependency problems against the
Forge API. It only checks and links; it is not a mod manager. Command-line tool.

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
ModPulse.exe [SptPath] [--refresh]
```

`--refresh` ignores the cache for that run (fresh answers are still saved for next time).

## Requirements

None. Releases are a standalone single-file exe with the .NET 10 runtime bundled (SPT 4.1 server mods are built for
.NET 10, and ModPulse loads them to read their metadata). Building from source needs the .NET 10 SDK.

## Build

```
dotnet build ModPulse.slnx
dotnet test Tests/ModPulse.Tests
dotnet publish ModPulse.csproj -c Release -r win-x64
```

`publish` with `-r` produces one standalone exe (plus a zip of it). `build` and `test` stay framework-dependent.

## Identity

`Configuration/AppIdentity.cs` holds the name, GUID and GitHub owner/repo used for self-update checks (GitHub
Releases) and issue links. The GUID is a placeholder until the final one is chosen.
