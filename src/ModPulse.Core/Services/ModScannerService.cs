using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SPTarkov.DI.Annotations;

namespace ModPulse.Services;

/// <summary>
/// Unified service for scanning both server and client mods from disk. Returns unified Mod objects with validation warnings.
/// </summary>
[Injectable(InjectionType.Transient)]
public sealed class ModScannerService(
    IOptions<ModScannerOptions> options,
    IModCheckReporter reporter,
    ILogger<ModScannerService> logger
) : IModScannerService
{
    private readonly ModScannerOptions _options = options.Value;

    /// <inheritdoc />
    public List<Mod> ScanServerMods(string sptPath, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Scanning server mods at: {SptPath}", sptPath);

        List<Mod> mods = [];

        var installation = SptLayout.Resolve(sptPath);
        if (installation is null || !Directory.Exists(installation.ServerModsDirectory))
        {
            logger.LogDebug("Server mods directory not found for: {SptPath}", sptPath);
            reporter.Status("Scanning server mods... none found.");
            return mods;
        }

        var modDirs = Directory.GetDirectories(installation.ServerModsDirectory);
        logger.LogDebug("Found {DirCount} mod directories", modDirs.Length);
        reporter.Blank();
        reporter.Status($"Scanning {modDirs.Length} mod directories for server mods...");

        foreach (var modDir in modDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mod = installation.Kind == SptServerKind.Node
                ? ScanNodeServerModDirectory(modDir, installation.ServerRoot)
                : ScanDotNetServerModDirectory(modDir, installation.ServerRoot);

            if (mod is not null)
            {
                mods.Add(mod);
            }
        }

        logger.LogInformation("Found {ModCount} server mods", mods.Count);
        reporter.Status($"Found {mods.Count} server mods.");
        return mods;
    }

    /// <summary>
    /// SPT 3.x: a server mod is a folder with a package.json. DLLs inside it (node_modules, bundled tools) are never
    /// server mods. A 4.x-style DLL mod without a package.json is reported, since a 3.x server can't load it.
    /// </summary>
    private Mod? ScanNodeServerModDirectory(string modDir, string serverRoot)
    {
        var packageMod = ReadPackageJsonMod(modDir);
        if (packageMod is not null)
        {
            return packageMod;
        }

        var dllMod = ScanDotNetServerModDirectory(modDir, serverRoot);
        dllMod?.LoadWarnings.Add("SPT 4.x (C#) server mod in an SPT 3.x install. SPT 3.x can't load it.");
        return dllMod;
    }

    /// <summary>
    /// SPT 4.x: a server mod is a DLL carrying mod metadata (one mod per folder). A leftover 3.x package.json mod is
    /// reported, since a 4.x server can't load it.
    /// </summary>
    private Mod? ScanDotNetServerModDirectory(string modDir, string serverRoot)
    {
        foreach (var dllPath in Directory.GetFiles(modDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var mod = ExtractServerModMetadata(dllPath, serverRoot);
                if (mod is not null)
                {
                    return mod; // Only one mod per directory
                }
            }
            catch (Exception ex)
            {
                reporter.CouldNotReadModDll(Path.GetFileName(dllPath), ex.Message);
            }
        }

        if (SptLayout.DetectKind(serverRoot) != SptServerKind.DotNet)
        {
            return null;
        }

        var legacyMod = ReadPackageJsonMod(modDir);
        legacyMod?.LoadWarnings.Add("SPT 3.x (TypeScript) server mod in an SPT 4.x install. SPT 4.x can't load it.");
        return legacyMod;
    }

    /// <summary>
    /// Reads an SPT 3.x server mod from its package.json (name, author, version, and the sptVersion/akiVersion
    /// range). The package name doubles as the GUID, which is how 3.x mods are listed on the Forge.
    /// </summary>
    private Mod? ReadPackageJsonMod(string modDir)
    {
        var packagePath = Path.Combine(modDir, "package.json");
        if (!File.Exists(packagePath))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(packagePath),
                new System.Text.Json.JsonDocumentOptions
                {
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }
            );
            var root = doc.RootElement;

            string? Text(string key)
            {
                if (!root.TryGetProperty(key, out var value))
                {
                    return null;
                }

                return value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => value.GetString(),
                    System.Text.Json.JsonValueKind.Number => value.GetRawText(),
                    // "author" is sometimes an object: { "name": "..." }
                    System.Text.Json.JsonValueKind.Object when value.TryGetProperty("name", out var n) => n.GetString(),
                    _ => null,
                };
            }

            var name = Text("name") ?? Path.GetFileName(modDir);
            var author = Text("author") ?? string.Empty;
            var version = Text("version") ?? string.Empty;
            var sptVersion = Text("sptVersion") ?? Text("akiVersion");

            return new Mod
            {
                Guid = name,
                FilePath = packagePath,
                IsServerMod = true,
                LocalName = name,
                LocalAuthor = author,
                LocalVersion = version,
                LocalSptVersion = sptVersion,
                LoadWarnings = ValidateModMetadata(name, author, version, name),
            };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not read package.json: {Path}", packagePath);
            reporter.CouldNotReadModDll(Path.GetFileName(packagePath), ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<Mod>> ScanClientModsAsync(string sptPath, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Scanning client mods at: {SptPath}", sptPath);

        var pluginsDir = Path.Combine(sptPath, "BepInEx", "plugins");
        List<Mod> mods = [];

        if (!Directory.Exists(pluginsDir))
        {
            logger.LogWarning("BepInEx plugins directory not found: {PluginsDir}", pluginsDir);
            reporter.PluginsDirectoryNotFound(pluginsDir);
            return mods;
        }

        var dllFiles = GetValidClientDllFiles(pluginsDir);
        if (dllFiles.Count == 0)
        {
            reporter.Status("No DLL files found in plugins directory.");
            return mods;
        }

        reporter.Status($"Scanning {dllFiles.Count} DLL files for BepInEx plugins...");

        var dllsByDirectory = GroupDllsByDirectory(dllFiles, pluginsDir);

        // Process loose DLLs (directly in plugins folder) as individual mods
        if (dllsByDirectory.TryGetValue(pluginsDir, out var looseDlls))
        {
            var looseResults = await ProcessClientDllsInParallelAsync(looseDlls, cancellationToken);
            mods.AddRange(FilterDuplicateClientMods(looseResults));
            dllsByDirectory.Remove(pluginsDir);
        }

        // Group each subdirectory's DLLs into the mods they belong to
        foreach (var (directory, directoryDlls) in dllsByDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mods.AddRange(ConsolidateDirectoryMods(directory, directoryDlls));
        }

        // A library whose GUID belongs to an installed plugin is part of that mod, not a mod of its own.
        var pluginGuids = mods.Where(m => !m.IsLibrary)
            .SelectMany(m => new[] { m.Guid }.Concat(m.AlternateGuids))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        mods = mods.Where(m => !m.IsLibrary || !pluginGuids.Contains(m.Guid))
            .DistinctBy(m => m.IsLibrary ? m.Guid.ToLowerInvariant() : m.FilePath)
            .ToList();

        foreach (var failure in DependencyReadFailures.Distinct())
        {
            logger.LogDebug("Could not read a [BepInDependency]: {Failure}", failure);
        }

        logger.LogInformation("Found {ModCount} client mods", mods.Count);
        reporter.Status($"Found {mods.Count} client mods.");
        return mods;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetBuiltInPluginGuids(string sptPath)
    {
        var sptPluginsDir = Path.Combine(sptPath, "BepInEx", "plugins", "spt");
        if (!Directory.Exists(sptPluginsDir))
        {
            return [];
        }

        List<string> guids = [];
        foreach (var dllPath in Directory.EnumerateFiles(sptPluginsDir, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                using var loadContext = CreateMetadataLoadContext(dllPath);
                var assembly = loadContext.LoadFromByteArray(File.ReadAllBytes(dllPath));
                if (ScanAssemblyForBepInPluginAttribute(assembly) is { } plugin)
                {
                    guids.Add(plugin.Guid);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping unreadable SPT plugin DLL: {DllPath}", dllPath);
            }
        }

        return guids;
    }

    /// <summary>
    /// Groups DLL files by their immediate parent directory.
    /// </summary>
    private static Dictionary<string, List<string>> GroupDllsByDirectory(List<string> dllFiles, string pluginsDir)
    {
        return dllFiles
            .GroupBy(dllPath => GetModDirectory(dllPath, pluginsDir), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the mod's root directory (immediate child of plugins, or plugins itself for loose DLLs).
    /// </summary>
    private static string GetModDirectory(string dllPath, string pluginsDir)
    {
        var directory = Path.GetDirectoryName(dllPath);

        if (directory is null || directory.Equals(pluginsDir, StringComparison.OrdinalIgnoreCase))
        {
            return pluginsDir;
        }

        // Walk up to find the immediate child of plugins
        while (true)
        {
            var parent = Path.GetDirectoryName(directory);
            if (parent is null || parent.Equals(pluginsDir, StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }

            directory = parent;
        }
    }

    /// <summary>
    /// Turns a directory's DLLs into mods. DLLs that reference each other become one mod, while unrelated DLLs (a mod
    /// copied into another's folder) stay separate.
    /// </summary>
    private List<Mod> ConsolidateDirectoryMods(string directory, List<string> dllPaths)
    {
        var allPlugins = ReadPluginDlls(dllPaths);
        var directoryName = Path.GetFileName(directory);

        var mods = allPlugins.Count == 0
            ? []
            : PartitionByRelatedness(allPlugins).Select(group => CreateConsolidatedMod(group, directoryName)).ToList();

        // A folder with no plugin at all may hold a library other mods depend on. DLLs next to a plugin are that mod's
        // own helpers (UniTask, VContainer, shared code...), never separate mods.
        if (allPlugins.Count == 0)
        {
            mods.AddRange(dllPaths.Select(TryCreateLibraryMod).OfType<Mod>());
        }

        return mods;
    }

    /// <summary>
    /// Reads plugin metadata and assembly references from each BepInPlugin DLL, skipping any that can't be read
    /// or that carry no BepInPlugin attribute.
    /// </summary>
    private List<PluginDll> ReadPluginDlls(List<string> dllPaths)
    {
        List<PluginDll> plugins = [];

        foreach (var dllPath in dllPaths)
        {
            try
            {
                using var loadContext = CreateMetadataLoadContext(dllPath);
                var assembly = loadContext.LoadFromByteArray(File.ReadAllBytes(dllPath));
                var plugin = ScanAssemblyForBepInPluginAttribute(assembly);

                if (plugin is null)
                {
                    continue;
                }

                var referencedNames = assembly
                    .GetReferencedAssemblies()
                    .Select(name => name.Name)
                    .OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                plugins.Add(new PluginDll(dllPath, plugin, assembly.GetName().Name, referencedNames));
            }
            catch (Exception ex)
            {
                // Skip DLLs that can't be read
                logger.LogDebug(ex, "Skipping unreadable plugin DLL: {DllPath}", dllPath);
            }
        }

        return plugins;
    }

    /// <summary>
    /// Builds one mod from a group of related DLLs; the primary plugin, plus the rest as alternate GUIDs.
    /// </summary>
    private Mod CreateConsolidatedMod(List<PluginDll> group, string directoryName)
    {
        var plugins = group.Select(item => (item.DllPath, item.Plugin)).ToList();

        var (primaryDll, primaryPlugin) = SelectPrimaryPlugin(plugins, directoryName);

        var mod = CreateModFromBepInPlugin(primaryPlugin, primaryDll);

        // Record the other plugins' GUIDs as alternates
        var alternateGuids = plugins
            .Select(item => item.Plugin.Guid)
            .Where(guid => !guid.Equals(primaryPlugin.Guid, StringComparison.OrdinalIgnoreCase))
            .Except(mod.AlternateGuids, StringComparer.OrdinalIgnoreCase);

        foreach (var guid in alternateGuids)
        {
            mod.AlternateGuids.Add(guid);
        }

        // Dependencies of every plugin in the group, minus the ones the group provides itself.
        var groupGuids = plugins.Select(item => item.Plugin.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declared = plugins
            .SelectMany(item => item.Plugin.Dependencies)
            .Where(d => !groupGuids.Contains(d.Guid))
            .GroupBy(d => d.Guid, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeclaredDependency(g.First().Guid, g.Any(d => d.IsHard)));
        mod.DeclaredDependencies.Clear();
        mod.DeclaredDependencies.AddRange(declared);

        return mod;
    }

    /// <summary>
    /// Groups plugin DLLs that belong to the same mod and separates those that don't. Two DLLs are treated as related
    /// when one references the other's assembly or when their GUIDs share an author namespace.
    /// </summary>
    private static List<List<PluginDll>> PartitionByRelatedness(List<PluginDll> plugins)
    {
        // Union-find; connect DLLs that are related, then group by component.
        var parents = Enumerable.Range(0, plugins.Count).ToArray();

        int Find(int node)
        {
            while (parents[node] != node)
            {
                parents[node] = parents[parents[node]];
                node = parents[node];
            }

            return node;
        }

        for (var i = 0; i < plugins.Count; i++)
        {
            for (var j = i + 1; j < plugins.Count; j++)
            {
                if (AreRelated(plugins[i], plugins[j]))
                {
                    parents[Find(i)] = Find(j);
                }
            }
        }

        return plugins
            .Select((plugin, index) => (plugin, Root: Find(index)))
            .GroupBy(item => item.Root)
            .Select(group => group.Select(item => item.plugin).ToList())
            .ToList();
    }

    /// <summary>
    /// Determines whether two co-located plugin DLLs belong to the same mod. Either one references the other's
    /// assembly, or their GUIDs share an author namespace.
    /// </summary>
    private static bool AreRelated(PluginDll a, PluginDll b)
    {
        return References(a, b) || References(b, a) || SameAuthorNamespace(a.Plugin.Guid, b.Plugin.Guid);
    }

    /// <summary>
    /// Determines whether <paramref name="from"/> references the assembly of <paramref name="to"/>.
    /// </summary>
    private static bool References(PluginDll from, PluginDll to)
    {
        return !string.IsNullOrEmpty(to.AssemblyName) && from.ReferencedAssemblyNames.Contains(to.AssemblyName);
    }

    /// <summary>
    /// Generic leading GUID segments (reverse-DNS TLDs and hosts) that, on their own, carry no author identity.
    /// </summary>
    private static readonly HashSet<string> _genericGuidSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "com",
        "org",
        "net",
        "io",
        "dev",
        "co",
        "app",
        "me",
        "gg",
        "xyz",
        "github",
        "gitlab",
        "gitee",
    };

    /// <summary>
    /// Determines whether two GUIDs share an author namespace. The namespace is the GUID without its final segment
    /// (the mod identifier), e.g. "com.janky.hollywoodfx" -> "com.janky". Two namespaces match when one is a
    /// segment-prefix of the other (loosely, so "com.author" matches "com.author.shared") and the shared prefix
    /// contains at least one non-generic segment... so "com.janky.*" pairs match on "janky", but unrelated "com.foo"
    /// and "com.bar.baz" do not match on the bare "com".
    /// </summary>
    private static bool SameAuthorNamespace(string guidA, string guidB)
    {
        var nsA = AuthorNamespaceSegments(guidA);
        var nsB = AuthorNamespaceSegments(guidB);

        if (nsA.Count == 0 || nsB.Count == 0)
        {
            return false;
        }

        var min = Math.Min(nsA.Count, nsB.Count);

        var shared = 0;
        while (shared < min && string.Equals(nsA[shared], nsB[shared], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        // One namespace must be a full segment-prefix of the other...
        if (shared < min)
        {
            return false;
        }

        // ...and the shared prefix must include at least one meaningful (non-generic) author segment.
        return nsA.Take(shared).Any(segment => !_genericGuidSegments.Contains(segment));
    }

    /// <summary>
    /// Splits a GUID into its author-namespace segments. All dot-delimited segments except the final one (the mod
    /// identifier). Returns an empty list for GUIDs with one or fewer segments, which carry no namespace.
    /// </summary>
    private static List<string> AuthorNamespaceSegments(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            return [];
        }

        var parts = guid.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length <= 1 ? [] : parts[..^1].ToList();
    }

    /// <summary>
    /// A BepInPlugin DLL plus the assembly-reference data used to group related DLLs.
    /// </summary>
    private sealed record PluginDll(
        string DllPath,
        BepInPluginAttribute Plugin,
        string? AssemblyName,
        IReadOnlySet<string> ReferencedAssemblyNames
    );

    /// <summary>
    /// Selects the primary plugin from a list of plugins in the same directory.
    /// Priority: filename matches directory name > "Core" in name > shortest GUID > alphabetically first.
    /// </summary>
    private static (string DllPath, BepInPluginAttribute Plugin) SelectPrimaryPlugin(
        List<(string DllPath, BepInPluginAttribute Plugin)> plugins,
        string directoryName
    )
    {
        // Normalize directory name for comparison
        var normalizedDirName = NormalizeModName(directoryName);

        // Try to find a plugin whose filename matches the directory name
        var directoryMatch = plugins
            .Where(p =>
            {
                var fileName = Path.GetFileNameWithoutExtension(p.DllPath);
                return fileName.Equals(directoryName, StringComparison.OrdinalIgnoreCase)
                    || fileName.Contains(normalizedDirName, StringComparison.OrdinalIgnoreCase)
                    || NormalizeModName(fileName).Equals(normalizedDirName, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(p => Path.GetFileNameWithoutExtension(p.DllPath).Length)
            .FirstOrDefault();

        if (directoryMatch.Plugin is not null)
        {
            return directoryMatch;
        }

        // Try to find a "Core" plugin
        var corePlugin = plugins
            .Where(p =>
            {
                var fileName = Path.GetFileNameWithoutExtension(p.DllPath);
                return fileName.Contains("Core", StringComparison.OrdinalIgnoreCase)
                    || p.Plugin.Name.Contains("Core", StringComparison.OrdinalIgnoreCase);
            })
            .FirstOrDefault();

        if (corePlugin.Plugin is not null)
        {
            return corePlugin;
        }

        // Fall back to the plugin with the shortest/simplest GUID, then alphabetically
        return plugins
            .OrderBy(p => p.Plugin.Guid.Split('.').Length)
            .ThenBy(p => p.Plugin.Guid.Length)
            .ThenBy(p => p.Plugin.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>
    /// Normalizes a mod name by removing common prefixes and suffixes.
    /// </summary>
    private static string NormalizeModName(string name)
    {
        // Remove common author-name prefixes (e.g., "kmyuhkyuk-GamePanelHUD" -> "GamePanelHUD")
        var dashIndex = name.IndexOf('-');
        if (dashIndex > 0 && dashIndex < name.Length - 1)
        {
            name = name[(dashIndex + 1)..];
        }

        // Remove common suffixes
        string[] suffixes = ["Client", "Plugin", "Mod", "BepInEx"];
        var matchingSuffix = suffixes.FirstOrDefault(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        if (matchingSuffix is not null)
        {
            name = name[..^matchingSuffix.Length];
        }

        return name.Trim();
    }

    /// <summary>
    /// Scans an assembly for a BepInPlugin attribute and returns it (without creating a Mod).
    /// </summary>
    private static BepInPluginAttribute? ScanAssemblyForBepInPluginAttribute(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            try
            {
                var bepInPlugin = ExtractBepInPluginAttribute(type);
                if (bepInPlugin is null)
                {
                    continue;
                }

                return bepInPlugin;
            }
            catch
            {
                // Skip types that can't be inspected
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<(List<Mod> ServerMods, List<Mod> ClientMods)> ScanAllModsAsync(
        string sptPath,
        CancellationToken cancellationToken = default
    )
    {
        var serverMods = ScanServerMods(sptPath, cancellationToken);
        var clientMods = await ScanClientModsAsync(sptPath, cancellationToken);
        return (serverMods, clientMods);
    }

    /// <inheritdoc />
    public string? GetSptVersion(string sptPath)
    {
        var installation = SptLayout.Resolve(sptPath);
        if (installation is null)
        {
            return null;
        }

        try
        {
            var reading = SptLayout.ReadVersion(installation.ServerRoot);
            if (reading is not null)
            {
                logger.LogDebug("SPT version {Version} read from {Source}", reading.Version, reading.Source);
            }

            return reading?.Version;
        }
        catch (Exception ex)
        {
            reporter.CouldNotReadSptVersion(ex.Message);
        }

        return null;
    }

    #region Misplaced Mod Detection

    /// <inheritdoc />
    public MisplacedModReport DetectMisplacedMods(string sptPath, CancellationToken cancellationToken = default)
    {
        List<MisplacedMod> wrongFolder = [];

        var installation = SptLayout.Resolve(sptPath);
        var serverRoot = installation?.ServerRoot ?? sptPath;

        // Client mods incorrectly placed in the server folder (<server>/user/mods). Scans recursively, including DLLs
        // loose in the mods root and nested in subfolders.
        var serverModsDir = Path.Combine(serverRoot, "user", "mods");
        if (Directory.Exists(serverModsDir))
        {
            foreach (var dllPath in Directory.GetFiles(serverModsDir, "*.dll", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var clientMod = TryDetectClientMod(dllPath);
                if (clientMod is not null)
                {
                    wrongFolder.Add(
                        new MisplacedMod(false, clientMod.Guid, clientMod.LocalName, clientMod.LocalVersion, dllPath)
                    );
                }
            }
        }

        // Server mods incorrectly placed in the client folder (BepInEx/plugins). All valid client DLLs (recursive,
        // excluding the SPT framework folder).
        var pluginsDir = Path.Combine(sptPath, "BepInEx", "plugins");
        if (Directory.Exists(pluginsDir))
        {
            foreach (var dllPath in GetValidClientDllFiles(pluginsDir))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var serverMod = TryDetectServerMod(dllPath, serverRoot);
                if (serverMod is not null)
                {
                    wrongFolder.Add(
                        new MisplacedMod(true, serverMod.Guid, serverMod.LocalName, serverMod.LocalVersion, dllPath)
                    );
                }
            }
        }

        var crossInstalled = DetectCrossInstalledDirectories(pluginsDir, cancellationToken);

        logger.LogDebug(
            "Detected {WrongFolder} misplaced mods and {CrossInstalled} cross-installed directories",
            wrongFolder.Count,
            crossInstalled.Count
        );

        return new MisplacedModReport(wrongFolder, crossInstalled);
    }

    /// <summary>
    /// Finds BepInEx/plugins subdirectories that contain two or more unrelated mods. Loose DLLs directly in the
    /// plugins root are not considered.
    /// </summary>
    private List<CrossInstalledDirectory> DetectCrossInstalledDirectories(
        string pluginsDir,
        CancellationToken cancellationToken
    )
    {
        List<CrossInstalledDirectory> crossInstalled = [];

        if (!Directory.Exists(pluginsDir))
        {
            return crossInstalled;
        }

        var dllsByDirectory = GroupDllsByDirectory(GetValidClientDllFiles(pluginsDir), pluginsDir);
        dllsByDirectory.Remove(pluginsDir);

        foreach (var (directory, directoryDlls) in dllsByDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plugins = ReadPluginDlls(directoryDlls);
            if (plugins.Count < 2)
            {
                continue;
            }

            var components = PartitionByRelatedness(plugins);
            if (components.Count < 2)
            {
                continue;
            }

            var attributed = AttributeCrossInstall(directory, components);
            if (attributed is not null)
            {
                crossInstalled.Add(attributed);
            }
        }

        return crossInstalled;
    }

    /// <summary>
    /// Decides which mods in a shared plugins folder are intruders (see <see cref="FindIntruders"/>). Returns null when
    /// every mod belongs there (e.g. a mod and its addon, both named after the folder).
    /// </summary>
    private CrossInstalledDirectory? AttributeCrossInstall(string directory, List<List<PluginDll>> components)
    {
        var directoryName = Path.GetFileName(directory);
        var attribution = FindIntruders(
            directoryName,
            components.Select(group => group.Select(plugin => Path.GetFileNameWithoutExtension(plugin.DllPath)).ToList()).ToList()
        );

        if (attribution is null)
        {
            return null;
        }

        var allMods = components.Select(group => ToMisplacedMod(group, directoryName)).ToList();
        if (attribution.Value.Ambiguous)
        {
            // Cannot tell which mod is the intruder. Surface the whole directory for review.
            return new CrossInstalledDirectory(directory, [], allMods, Ambiguous: true);
        }

        var misplaced = attribution.Value.Intruders.Select(index => allMods[index]).ToList();
        return new CrossInstalledDirectory(directory, misplaced, allMods, Ambiguous: false);
    }

    /// <summary>
    /// Given the unrelated mods (components) sharing one plugins folder, works out which ones don't belong.
    /// <list type="bullet">
    /// <item>
    /// Any component with a DLL named after the folder belongs there. That covers the folder's own mod and companions
    /// named after it, such as an addon installed into its parent's folder (folder <c>BlackDiv</c> with
    /// <c>BlackDiv.dll</c> and <c>BlackDivInfo.dll</c>). The rest are intruders; when there are none, the folder is
    /// fine (null).
    /// </item>
    /// <item>With no folder-name match, a single largest component (by DLL count) is the occupant.</item>
    /// <item>Otherwise the folder is ambiguous.</item>
    /// </list>
    /// </summary>
    /// <param name="directoryName">The plugins subfolder's name.</param>
    /// <param name="componentDllNames">Each component's DLL file names (without extension).</param>
    internal static (bool Ambiguous, IReadOnlyList<int> Intruders)? FindIntruders(
        string directoryName,
        IReadOnlyList<IReadOnlyList<string>> componentDllNames
    )
    {
        var belongs = componentDllNames
            .Select((names, index) => (names, index))
            .Where(item => item.names.Any(name => MatchesFolderName(name, directoryName)))
            .Select(item => item.index)
            .ToHashSet();

        if (belongs.Count > 0)
        {
            var intruders = Enumerable.Range(0, componentDllNames.Count).Where(i => !belongs.Contains(i)).ToList();
            return intruders.Count == 0 ? null : (false, intruders);
        }

        // The unique largest component (by DLL count) is taken to be the intended occupant.
        var maxSize = componentDllNames.Max(names => names.Count);
        var largest = Enumerable.Range(0, componentDllNames.Count).Where(i => componentDllNames[i].Count == maxSize).ToList();

        if (largest.Count == 1)
        {
            return (false, Enumerable.Range(0, componentDllNames.Count).Where(i => i != largest[0]).ToList());
        }

        return (true, []);
    }

    /// <summary>
    /// Describes a relatedness component as a single mod, using its primary plugin for the name, GUID, and path.
    /// </summary>
    private MisplacedMod ToMisplacedMod(List<PluginDll> group, string directoryName)
    {
        var (primaryDll, primaryPlugin) = SelectPrimaryPlugin(
            group.Select(plugin => (plugin.DllPath, plugin.Plugin)).ToList(),
            directoryName
        );

        var mod = CreateModFromBepInPlugin(primaryPlugin, primaryDll);
        return new MisplacedMod(false, mod.Guid, mod.LocalName, mod.LocalVersion, primaryDll);
    }

    /// <summary>
    /// Determines whether a DLL filename identifies its folder's intended occupant. Both are normalized (lower case,
    /// separators removed) and matched by prefix in either direction, so a folder named "Mod" owns "Mod.dll",
    /// "Mod.Core.dll", or "ModCore.dll", while an unrelated "Other.dll" does not.
    /// </summary>
    private static bool MatchesFolderName(string fileName, string directoryName)
    {
        var file = NormalizeIdentifier(fileName);
        var directory = NormalizeIdentifier(directoryName);

        if (file.Length == 0 || directory.Length == 0)
        {
            return false;
        }

        return file.StartsWith(directory, StringComparison.Ordinal)
            || directory.StartsWith(file, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lower-cases an identifier and strips separators (dots, dashes, underscores, spaces).
    /// </summary>
    private static string NormalizeIdentifier(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// Attempts to read the DLL as a client (BepInEx) mod. Returns the mod if a BepInPlugin attribute is found,
    /// otherwise null. All load failures are swallowed.
    /// </summary>
    private Mod? TryDetectClientMod(string dllPath)
    {
        try
        {
            using var loadContext = CreateMetadataLoadContext(dllPath);
            var assembly = loadContext.LoadFromByteArray(File.ReadAllBytes(dllPath));

            foreach (var type in GetLoadableTypes(assembly))
            {
                try
                {
                    var plugin = ExtractBepInPluginAttribute(type);
                    if (plugin is not null)
                    {
                        return CreateModFromBepInPlugin(plugin, dllPath);
                    }
                }
                catch
                {
                    // Skip types that can't be inspected
                }
            }
        }
        catch (Exception ex)
        {
            // Not a client mod
            logger.LogDebug(ex, "Could not inspect DLL as a client mod: {DllPath}", dllPath);
        }

        return null;
    }

    /// <summary>
    /// Attempts to read the DLL as a server (SPT) mod. Returns the mod if SPT mod metadata is found, otherwise null.
    /// All load failures are swallowed.
    /// </summary>
    private Mod? TryDetectServerMod(string dllPath, string serverRoot)
    {
        try
        {
            return ExtractServerModMetadata(dllPath, serverRoot);
        }
        catch (Exception ex)
        {
            // Not a server mod
            logger.LogDebug(ex, "Could not inspect DLL as a server mod: {DllPath}", dllPath);
            return null;
        }
    }

    #endregion

    #region Server Mod Extraction

    private Mod? ExtractServerModMetadata(string dllPath, string serverRoot)
    {
        var loadContext = new SptAssemblyLoadContext(serverRoot);

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(dllPath));
            var assembly = loadContext.LoadFromStream(stream);
            var metadata = LoadSptMetadataFromAssembly(assembly);

            if (metadata is null)
            {
                return null;
            }

            // Use reflection to access properties
            var metadataType = metadata.GetType();
            var modGuid = metadataType.GetProperty("ModGuid")?.GetValue(metadata)?.ToString();
            var name = metadataType.GetProperty("Name")?.GetValue(metadata)?.ToString();
            var author = metadataType.GetProperty("Author")?.GetValue(metadata)?.ToString();
            var modVersion = metadataType.GetProperty("Version")?.GetValue(metadata)?.ToString();
            var sptVersion = metadataType.GetProperty("SptVersion")?.GetValue(metadata)?.ToString();

            if (string.IsNullOrEmpty(modGuid))
            {
                return null;
            }

            var version = modVersion ?? GetAssemblyVersion(assembly);

            var warnings = ValidateModMetadata(name ?? string.Empty, author ?? string.Empty, version, modGuid);

            return new Mod
            {
                Guid = modGuid,
                FilePath = dllPath,
                IsServerMod = true,
                LocalName = name ?? string.Empty,
                LocalAuthor = author ?? string.Empty,
                LocalVersion = version,
                LocalSptVersion = sptVersion,
                LoadWarnings = warnings,
            };
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static object? LoadSptMetadataFromAssembly(Assembly assembly)
    {
        // SPT 4.0 mods subclass the abstract record AbstractModMetadata. SPT 4.1 mods implement the IModMetadata
        // interface instead (typically a sealed record), so look for either.
        var metadataType = GetLoadableTypes(assembly)
            .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface && (InheritsAbstractModMetadata(t) || ImplementsIModMetadata(t)));

        if (metadataType is null)
        {
            return null;
        }

        return Activator.CreateInstance(metadataType);
    }

    private static bool InheritsAbstractModMetadata(Type type)
    {
        for (var current = type.BaseType; current is not null; current = SafeBaseType(current))
        {
            if (current.Name == "AbstractModMetadata")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsIModMetadata(Type type)
    {
        try
        {
            return type.GetInterfaces().Any(i => i.Name == "IModMetadata");
        }
        catch (Exception)
        {
            // An interface lives in an assembly that couldn't be resolved; not a mod we can read.
            return false;
        }
    }

    private static Type? SafeBaseType(Type type)
    {
        try
        {
            return type.BaseType;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all types from an assembly that can be loaded, gracefully handling types with missing dependencies.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return only the types that loaded successfully (non-null entries)
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static string GetAssemblyVersion(Assembly assembly)
    {
        var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (infoVersionAttr is null)
        {
            var version = assembly.GetName().Version;
            if (version is null)
            {
                return string.Empty;
            }

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        var fullVersion = infoVersionAttr.InformationalVersion;
        if (string.IsNullOrEmpty(fullVersion))
        {
            var version = assembly.GetName().Version;
            if (version is null)
            {
                return string.Empty;
            }

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        var plusIndex = fullVersion.IndexOf('+');
        if (plusIndex > 0)
        {
            fullVersion = fullVersion[..plusIndex];
        }

        return fullVersion;
    }

    /// <summary>
    /// Custom AssemblyLoadContext that resolves SPT assemblies from the SPT server folder (SPT/ on 4.0,
    /// SPT_Runtime/ on 4.1).
    /// </summary>
    private sealed class SptAssemblyLoadContext(string serverRoot) : AssemblyLoadContext(true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Try to find the assembly in the SPT server folder
            var sptPath = Path.Combine(serverRoot, $"{assemblyName.Name}.dll");
            if (File.Exists(sptPath))
            {
                using var stream = new MemoryStream(File.ReadAllBytes(sptPath));
                return LoadFromStream(stream);
            }

            return null;
        }
    }

    #endregion

    #region Client Mod Extraction

    private List<string> GetValidClientDllFiles(string pluginsPath)
    {
        // Skip SPT's own plugins/spt folder. Match the folder exactly (with a trailing separator) so mods whose folder
        // merely starts with "spt" (e.g. "SPT-Leaderboard") are still scanned.
        var sptDir = Path.Combine(pluginsPath, "spt") + Path.DirectorySeparatorChar;

        return Directory
            .GetFiles(pluginsPath, "*.dll", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(sptDir, StringComparison.OrdinalIgnoreCase))
            .Where(file => new FileInfo(file).Length <= _options.MaxDllSizeBytes)
            .ToList();
    }

    private async Task<List<Mod>> ProcessClientDllsInParallelAsync(
        List<string> dllFiles,
        CancellationToken cancellationToken = default
    )
    {
        // Per-DLL failures are collected into warnings and surfaced after the parallel scan.
        var warnings = new ConcurrentBag<(string FileName, string Reason)>();
        var tasks = dllFiles.Select(dllPath =>
            Task.Run(() => ExtractClientModMetadata(dllPath, warnings), cancellationToken)
        );

        var results = await Task.WhenAll(tasks);

        // Log each extraction failure at debug level.
        foreach (var (fileName, reason) in warnings)
        {
            logger.LogDebug("Could not extract client mod metadata from {FileName}: {Reason}", fileName, reason);
        }

        return results.Where(r => r is not null).Cast<Mod>().ToList();
    }

    private static List<Mod> FilterDuplicateClientMods(List<Mod> mods)
    {
        return mods.DistinctBy(m => (m.LocalName.ToLowerInvariant(), m.LocalAuthor.ToLowerInvariant())).ToList();
    }

    private Mod? ExtractClientModMetadata(string dllPath, ConcurrentBag<(string FileName, string Reason)> warnings)
    {
        try
        {
            using var loadContext = CreateMetadataLoadContext(dllPath);
            var assembly = loadContext.LoadFromByteArray(File.ReadAllBytes(dllPath));

            return ScanAssemblyForBepInPlugin(assembly, dllPath) ?? TryCreateLibraryMod(dllPath);
        }
        catch (Exception ex)
        {
            // Collect the failure for the caller to surface.
            warnings.Add((Path.GetFileName(dllPath), ex.Message));
            return null;
        }
    }

    private static MetadataLoadContext CreateMetadataLoadContext(string dllPath)
    {
        var resolver = new AssemblyResolver(dllPath);
        return new MetadataLoadContext(resolver);
    }

    private Mod? ScanAssemblyForBepInPlugin(Assembly assembly, string dllPath)
    {
        foreach (var type in assembly.GetTypes())
        {
            try
            {
                var bepInPlugin = ExtractBepInPluginAttribute(type);
                if (bepInPlugin is null)
                {
                    continue;
                }

                return CreateModFromBepInPlugin(bepInPlugin, dllPath);
            }
            catch
            {
                // Skip types that can't be inspected
            }
        }

        return null;
    }

    private static BepInPluginAttribute? ExtractBepInPluginAttribute(Type type)
    {
        var customAttributes = type.GetCustomAttributesData();

        var bepInPluginAttribute = customAttributes.FirstOrDefault(attr =>
            attr.AttributeType.Name is "BepInPlugin" or "BepInPluginAttribute"
            || (attr.AttributeType.FullName?.Contains("BepInPlugin") ?? false)
        );

        if (bepInPluginAttribute is null || bepInPluginAttribute.ConstructorArguments.Count < 3)
        {
            return null;
        }

        var guid = bepInPluginAttribute.ConstructorArguments[0].Value?.ToString() ?? "";
        var name = bepInPluginAttribute.ConstructorArguments[1].Value?.ToString() ?? "";
        var version = bepInPluginAttribute.ConstructorArguments[2].Value?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return new BepInPluginAttribute(guid, name, version) { Dependencies = ExtractDeclaredDependencies(customAttributes) };
    }

    /// <summary>
    /// Reads <c>[BepInDependency]</c> attributes: <c>(guid)</c> and <c>(guid, minimumVersion)</c> are hard;
    /// <c>(guid, DependencyFlags)</c> is hard unless the flags are SoftDependency (2).
    /// </summary>
    /// <summary>Diagnostics: dependency attributes that couldn't be read (logged after a scan).</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentBag<string> DependencyReadFailures = [];

    private static List<DeclaredDependency> ExtractDeclaredDependencies(IList<CustomAttributeData> attributes)
    {
        List<DeclaredDependency> dependencies = [];
        foreach (var attribute in attributes)
        {
            var typeName = attribute.AttributeType.Name;
            if (typeName is not ("BepInDependency" or "BepInDependencyAttribute") || attribute.ConstructorArguments.Count == 0)
            {
                continue;
            }

            try
            {
                var dependencyGuid = attribute.ConstructorArguments[0].Value?.ToString();
                if (string.IsNullOrWhiteSpace(dependencyGuid))
                {
                    continue;
                }

                var isHard = true;
                if (attribute.ConstructorArguments.Count > 1 && attribute.ConstructorArguments[1].Value is int flags)
                {
                    isHard = (flags & 2) == 0;
                }

                dependencies.Add(new DeclaredDependency(dependencyGuid, isHard));
            }
            catch (Exception ex)
            {
                // An unreadable dependency must never cost us the plugin itself.
                System.Diagnostics.Debug.WriteLine(ex);
                DependencyReadFailures.Add(ex.GetType().Name + ": " + ex.Message);
            }
        }

        return dependencies;
    }

    private Mod CreateModFromBepInPlugin(BepInPluginAttribute plugin, string dllPath)
    {
        var (author, name) = ParseAuthorAndName(plugin.Name, plugin.Guid);

        var warnings = ValidateModMetadata(name, author, plugin.Version, plugin.Guid);

        return new Mod
        {
            Guid = plugin.Guid,
            FilePath = dllPath,
            IsServerMod = false,
            LocalName = name,
            LocalAuthor = author,
            LocalVersion = plugin.Version,
            LocalSptVersion = null,
            LoadWarnings = warnings,
            DeclaredDependencies = plugin.Dependencies.ToList(),
        };
    }

    /// <summary>
    /// A library DLL (no <c>[BepInPlugin]</c>) as a client mod, when it carries a GUID naming itself (see
    /// <see cref="LibraryDllIdentity"/>); null otherwise.
    /// </summary>
    private Mod? TryCreateLibraryMod(string dllPath)
    {
        string? assemblyName;
        try
        {
            assemblyName = AssemblyName.GetAssemblyName(dllPath).Name;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or ArgumentException)
        {
            return null;
        }

        var identity = LibraryDllIdentity.TryIdentify(dllPath, assemblyName);
        if (identity is null)
        {
            return null;
        }

        var (guid, version) = identity.Value;
        logger.LogDebug("Library DLL {DllPath} identified as {Guid} {Version}", dllPath, guid, version);

        var segments = guid.Split('.');
        return new Mod
        {
            Guid = guid,
            FilePath = dllPath,
            IsServerMod = false,
            LocalName = assemblyName ?? Path.GetFileNameWithoutExtension(dllPath),
            LocalAuthor = segments.Length >= 3 ? segments[1] : "Unknown",
            LocalVersion = version,
            IsLibrary = true,
        };
    }

    /// <summary>
    /// Parses author and name from BepInEx plugin metadata.
    /// </summary>
    private static (string Author, string Name) ParseAuthorAndName(string pluginName, string guid)
    {
        // Try to extract author from formats like "AuthorName - ModName" or "ModName by AuthorName"
        if (pluginName.Contains(" - "))
        {
            var parts = pluginName.Split(" - ", 2);
            if (parts.Length == 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }
        }

        if (pluginName.Contains(" by ", StringComparison.OrdinalIgnoreCase))
        {
            var byIndex = pluginName.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
            var name = pluginName[..byIndex].Trim();
            var author = pluginName[(byIndex + 4)..].Trim();
            return (author, name);
        }

        // Try to extract the author from GUID (e.g., "com.author.modname")
        var guidParts = guid.Split('.');
        if (guidParts.Length < 2)
        {
            return ("Unknown", pluginName);
        }

        // Assume the second-to-last part is author for GUIDs like "com.author.modname"
        var potentialAuthor = guidParts.Length >= 3 ? guidParts[^2] : guidParts[0];

        // Don't use common prefixes as author names
        if (
            string.Equals(potentialAuthor, "com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(potentialAuthor, "org", StringComparison.OrdinalIgnoreCase)
            || string.Equals(potentialAuthor, "spt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(potentialAuthor, "aki", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ("Unknown", pluginName);
        }

        return (potentialAuthor, pluginName);
    }

    #endregion

    #region Validation

    private static List<string> ValidateModMetadata(string name, string author, string version, string guid)
    {
        List<string> warnings = [];

        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add("Missing mod name");
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            warnings.Add("Missing author");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            warnings.Add("Missing version");
        }
        else if (!IsValidVersion(version))
        {
            warnings.Add($"Invalid version format: {version}");
        }

        if (string.IsNullOrWhiteSpace(guid))
        {
            warnings.Add("Missing GUID");
        }

        return warnings;
    }

    private static bool IsValidVersion(string version)
    {
        return SemVer.TryParse(version) is not null;
    }

    #endregion
}
