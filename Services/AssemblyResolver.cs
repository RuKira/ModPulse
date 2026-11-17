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
            // The game's Managed folder supplies mscorlib/netstandard. This is what makes client scanning work in a
            // self-contained single-file build, where the .NET runtime assemblies are bundled inside the exe and are
            // not on disk.
            AddDlls(assemblyPaths, Path.Combine(gameRoot, "EscapeFromTarkov_Data", "Managed"));
            AddDlls(assemblyPaths, Path.Combine(gameRoot, "BepInEx", "core"));
        }

        // .NET runtime assemblies. Present for framework-dependent builds; in a self-contained single-file build this
        // is the exe's folder and holds no runtime assemblies.
        AddDlls(assemblyPaths, RuntimeEnvironment.GetRuntimeDirectory());

        return assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddDlls(List<string> assemblyPaths, string directory)
    {
        if (Directory.Exists(directory))
        {
            assemblyPaths.AddRange(Directory.GetFiles(directory, "*.dll"));
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
