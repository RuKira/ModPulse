using System.Reflection;
using System.Runtime.InteropServices;

namespace ModPulse.Services;

/// <summary>
/// Custom assembly resolver that handles missing assemblies gracefully for BepInEx plugin scanning.
/// </summary>
/// <param name="dllPath">Path to the DLL being analyzed.</param>
public sealed class AssemblyResolver(string dllPath) : MetadataAssemblyResolver
{
    private readonly PathAssemblyResolver _pathResolver = new(BuildMinimalAssemblySearchPaths(dllPath));

    /// <summary>
    /// Resolves assembly references, returning null for missing assemblies to allow inspection to continue.
    /// </summary>
    /// <param name="context">The metadata load context.</param>
    /// <param name="assemblyName">The assembly name to resolve.</param>
    public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
    {
        try
        {
            return _pathResolver.Resolve(context, assemblyName);
        }
        catch
        {
            // Return null for missing assemblies.
            return null;
        }
    }

    /// <summary>
    /// Builds a minimal list of assembly search paths required for MetadataLoadContext. Includes the target DLL, the
    /// game's own managed assemblies (the Mono core library client plugins are built against), BepInEx core
    /// assemblies, and the .NET runtime assemblies.
    /// </summary>
    /// <param name="dllPath">Path to the DLL being analyzed.</param>
    private static IEnumerable<string> BuildMinimalAssemblySearchPaths(string dllPath)
    {
        List<string> assemblyPaths = [dllPath];

        var gameRoot = FindGameRoot(dllPath);
        if (gameRoot != null)
        {
            // The plugin's own mod folder (with subfolders), so a plugin built on a sibling DLL resolves it:
            // GamePanelHUD's Compass/Health/... plugins derive from core/GamePanelHUDCore.dll.
            var modFolder = FindModFolder(dllPath, Path.Combine(gameRoot, "BepInEx", "plugins"));
            if (modFolder is not null)
            {
                AddDlls(assemblyPaths, modFolder, SearchOption.AllDirectories);
            }

            // The game's Managed folder supplies mscorlib/netstandard (the Mono core library plugins are built
            // against). It is also what makes scanning work in a self-contained single-file build, where the .NET
            // runtime assemblies are inside the exe rather than on disk.
            AddDlls(assemblyPaths, Path.Combine(gameRoot, "EscapeFromTarkov_Data", "Managed"));
            AddDlls(assemblyPaths, Path.Combine(gameRoot, "BepInEx", "core"));
        }

        // .NET runtime assemblies. Present for framework-dependent builds; in a self-contained single-file build this
        // is the exe's folder and holds no runtime assemblies.
        AddDlls(assemblyPaths, RuntimeEnvironment.GetRuntimeDirectory());

        // One file per assembly name, earlier sources first. Two files with the same identity (the game's Mono
        // mscorlib and .NET's mscorlib facade are both "mscorlib, 4.0.0.0") make MetadataLoadContext fail with
        // "already loaded", which made every plugin touching mscorlib/netstandard unreadable.
        return assemblyPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>The mod folder a plugin DLL lives in (the direct child of BepInEx/plugins), or null for a loose DLL.</summary>
    private static string? FindModFolder(string dllPath, string pluginsDir)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dllPath));
        var plugins = Path.GetFullPath(pluginsDir).TrimEnd(Path.DirectorySeparatorChar);

        while (!string.IsNullOrEmpty(directory))
        {
            var parent = Path.GetDirectoryName(directory);
            if (string.Equals(directory, plugins, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(parent, plugins, StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }

            directory = parent;
        }

        return null;
    }

    private static void AddDlls(
        List<string> assemblyPaths,
        string directory,
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        if (Directory.Exists(directory))
        {
            assemblyPaths.AddRange(Directory.GetFiles(directory, "*.dll", searchOption));
        }
    }

    /// <summary>
    /// Finds the game root by walking up from the DLL path to the first ancestor that contains a BepInEx/core folder.
    /// </summary>
    /// <param name="dllPath">Path to the DLL being analyzed.</param>
    /// <returns>The game root directory, or null if not found.</returns>
    private static string? FindGameRoot(string dllPath)
    {
        var currentDir = Path.GetDirectoryName(dllPath);

        while (!string.IsNullOrEmpty(currentDir))
        {
            if (Directory.Exists(Path.Combine(currentDir, "BepInEx", "core")))
            {
                return currentDir;
            }

            currentDir = Path.GetDirectoryName(currentDir);
        }

        return null;
    }
}
