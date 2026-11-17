using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Services;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;
using Version = SemanticVersioning.Version;

namespace ModPulse.Tests;

/// <summary>
/// Install detection for every supported SPT line (3.9/3.10/3.11 Node server in the game root; 4.0 in SPT/;
/// 4.1 in SPT_Runtime/) and server-mod scanning for each server kind. Kept in one class because the file-version
/// reader is swapped for the duration of each test.
/// </summary>
public sealed class SptLayoutTests : IDisposable
{
    private readonly string _root = TempWorkspace.CreateDirectory("modpulse-layout");
    private readonly Dictionary<string, string> _fileVersions = new(StringComparer.OrdinalIgnoreCase);

    public SptLayoutTests()
    {
        SptLayout.FileVersionReader = path => _fileVersions.GetValueOrDefault(Path.GetFullPath(path));
    }

    public void Dispose()
    {
        SptLayout.FileVersionReader = SptLayout.DefaultFileVersionReader;
        TempWorkspace.SafeDelete(_root);
    }

    private string Write(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>3.x: SPT.Server.exe and SPT_Data/Server/configs/core.json in the game root.</summary>
    private void NodeInstall(string sptVersion)
    {
        Write("EscapeFromTarkov.exe");
        Write("SPT.Server.exe");
        Write(Path.Combine("SPT_Data", "Server", "configs", "core.json"), $$"""{ "sptVersion": "{{sptVersion}}" }""");
        Directory.CreateDirectory(Path.Combine(_root, "user", "mods"));
        Directory.CreateDirectory(Path.Combine(_root, "BepInEx", "plugins"));
    }

    /// <summary>4.x: the C# server in its sub folder (SPT/ for 4.0, SPT_Runtime/ for 4.1 unless overridden).</summary>
    private string DotNetServer(string version, string? folder = null)
    {
        folder ??= SptLayout.ExpectedServerFolder(Version.Parse(version))!;
        Write("EscapeFromTarkov.exe");
        Directory.CreateDirectory(Path.Combine(_root, "BepInEx", "plugins"));
        var core = Write(Path.Combine(folder, "SPTarkov.Server.Core.dll"));
        _fileVersions[Path.GetFullPath(core)] = version;
        Directory.CreateDirectory(Path.Combine(_root, folder, "user", "mods"));
        return Path.Combine(_root, folder);
    }

    [Theory]
    [InlineData("3.9.8")]
    [InlineData("3.10.5")]
    [InlineData("3.11.4")]
    public void Spt3_server_is_the_game_root_and_version_comes_from_core_json(string version)
    {
        NodeInstall(version);

        var install = SptLayout.Resolve(_root);

        Assert.NotNull(install);
        Assert.Equal(SptServerKind.Node, install!.Kind);
        Assert.Equal(_root, install.ServerRoot);
        Assert.Equal(_root, install.GameRoot);
        Assert.Equal(version, SptLayout.ReadVersion(install.ServerRoot)?.Version);
    }

    [Theory]
    [InlineData("4.0.13", "SPT")]
    [InlineData("4.1.6", "SPT_Runtime")]
    public void Spt4_server_is_its_release_folder_and_version_comes_from_core_dll(string version, string folder)
    {
        DotNetServer(version);

        var install = SptLayout.Resolve(_root);

        Assert.NotNull(install);
        Assert.Equal(SptServerKind.DotNet, install!.Kind);
        Assert.Equal(Path.Combine(_root, folder), install.ServerRoot);
        Assert.Equal(_root, install.GameRoot);
        Assert.Equal(version, SptLayout.ReadVersion(install.ServerRoot)?.Version);
        Assert.Null(SptLayout.DescribeFolderMismatch(install, Version.Parse(version)));
    }

    [Fact]
    public void Pointing_at_the_server_folder_resolves_the_game_root_as_its_parent()
    {
        var server = DotNetServer("4.1.6");

        var install = SptLayout.Resolve(server);

        Assert.Equal(_root, install!.GameRoot);
        Assert.Equal(server, install.ServerRoot);
    }

    [Fact]
    public void Leftover_40_folder_after_upgrading_resolves_to_the_41_server()
    {
        DotNetServer("4.1.6");
        DotNetServer("4.0.13");

        Assert.Equal(Path.Combine(_root, "SPT_Runtime"), SptLayout.Resolve(_root)!.ServerRoot);
    }

    [Fact]
    public void Newer_server_wins_and_mismatched_folder_is_reported()
    {
        DotNetServer("4.0.13", "SPT_Runtime");
        DotNetServer("4.1.6", "SPT");

        var install = SptLayout.Resolve(_root)!;

        Assert.Equal(Path.Combine(_root, "SPT"), install.ServerRoot);
        Assert.NotNull(SptLayout.DescribeFolderMismatch(install, Version.Parse("4.1.6")));
    }

    [Fact]
    public void Spt4_root_with_leftover_spt3_files_still_uses_the_csharp_server()
    {
        DotNetServer("4.1.6");
        Write(Path.Combine("SPT_Data", "Server", "configs", "core.json"), """{ "sptVersion": "3.11.4" }""");

        Assert.Equal(SptServerKind.DotNet, SptLayout.Resolve(_root)!.Kind);
    }

    [Fact]
    public void Folder_without_spt_resolves_to_null()
    {
        Assert.Null(SptLayout.Resolve(_root));
    }

    [Fact]
    public void Spt3_falls_back_to_server_exe_version_without_core_json()
    {
        NodeInstall("3.10.5");
        File.Delete(Path.Combine(_root, "SPT_Data", "Server", "configs", "core.json"));
        _fileVersions[Path.GetFullPath(Path.Combine(_root, "SPT.Server.exe"))] = "3.10.5.0";

        Assert.Equal("3.10.5", SptLayout.ReadVersion(_root)?.Version);
    }

    [Theory]
    [InlineData("4.1.6-RELEASE+731d7a2.20260918", "4.1.6")]
    [InlineData("3.10.5.0", "3.10.5")]
    [InlineData("v3.9.8", "3.9.8")]
    [InlineData("4.0", "4.0.0")]
    [InlineData("", null)]
    [InlineData("release", null)]
    public void Normalize_reduces_to_major_minor_patch(string raw, string? expected)
    {
        Assert.Equal(expected, SptLayout.Normalize(raw));
    }

    [Theory]
    [InlineData("3.8.3", false, true)]
    [InlineData("3.9.8", true, true)]
    [InlineData("3.10.5", true, true)]
    [InlineData("3.11.4", true, false)]
    [InlineData("4.0.13", true, false)]
    [InlineData("4.1.6", true, false)]
    [InlineData("4.2.0", false, false)]
    public void Supported_range_is_3_9_to_4_1_and_legacy_is_below_3_11(string version, bool supported, bool legacy)
    {
        Assert.Equal(supported, SptLayout.IsSupported(Version.Parse(version)));
        Assert.Equal(legacy, SptLayout.IsLegacy(Version.Parse(version)));
    }

    [Fact]
    public void Spt3_server_mods_are_read_from_package_json()
    {
        NodeInstall("3.10.5");
        Write(
            Path.Combine("user", "mods", "SPT-Realism", "package.json"),
            """{ "name": "SPT-Realism", "author": { "name": "Fontaine" }, "version": "1.5.2", "sptVersion": "~3.10" }"""
        );
        Write(Path.Combine("user", "mods", "SPT-Realism", "node_modules", "lib.dll"));

        var mods = CreateScanner().ScanServerMods(_root);

        var mod = Assert.Single(mods);
        Assert.Equal("SPT-Realism", mod.Guid);
        Assert.Equal("Fontaine", mod.LocalAuthor);
        Assert.Equal("1.5.2", mod.LocalVersion);
        Assert.Equal("~3.10", mod.LocalSptVersion);
        Assert.True(mod.IsServerMod);
        Assert.Empty(mod.LoadWarnings);
    }

    [Fact]
    public void Spt4_flags_a_leftover_spt3_package_json_mod()
    {
        DotNetServer("4.1.6");
        Write(
            Path.Combine("SPT_Runtime", "user", "mods", "OldMod", "package.json"),
            """{ "name": "OldMod", "author": "Someone", "version": "1.0.0" }"""
        );

        var mod = Assert.Single(CreateScanner().ScanServerMods(_root));

        Assert.Contains(mod.LoadWarnings, w => w.Contains("SPT 4.x can't load it"));
    }

    [Fact]
    public void Scanner_reports_the_version_for_both_server_kinds()
    {
        NodeInstall("3.11.4");
        Assert.Equal("3.11.4", CreateScanner().GetSptVersion(_root));

        File.Delete(Path.Combine(_root, "SPT.Server.exe"));
        Directory.Delete(Path.Combine(_root, "SPT_Data"), recursive: true);
        DotNetServer("4.0.13");
        Assert.Equal("4.0.13", CreateScanner().GetSptVersion(_root));
    }

    private static ModScannerService CreateScanner()
    {
        return new ModScannerService(
            Options.Create(new ModScannerOptions()),
            NoOpReporter.Create(),
            NullLogger<ModScannerService>.Instance
        );
    }
}

/// <summary>
/// An <see cref="IModCheckReporter"/> that does nothing, for tests that only care about results.
/// </summary>
public class NoOpReporter : DispatchProxy
{
    public static IModCheckReporter Create()
    {
        return DispatchProxy.Create<IModCheckReporter, NoOpReporter>();
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var returnType = targetMethod?.ReturnType;
        if (returnType is null || returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
