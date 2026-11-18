using ModPulse.Models;

namespace ModPulse.Gui.Tests;

/// <summary>Builders for Core mods in the states the GUI has to show.</summary>
internal static class TestMods
{
    public static Mod Local(string name = "Test Mod", string version = "1.0.0", bool server = true, string? guid = null)
    {
        return new Mod
        {
            Guid = guid ?? "com.test." + name.Replace(" ", string.Empty).ToLowerInvariant(),
            FilePath = $@"C:\SPT\BepInEx\plugins\{name}.dll",
            IsServerMod = server,
            LocalName = name,
            LocalAuthor = "Author",
            LocalVersion = version,
        };
    }

    public static Mod Matched(string name = "Test Mod", string version = "1.0.0", int id = 7)
    {
        var mod = Local(name, version);
        mod.UpdateFromApiMatch(
            new ModSearchResult(
                id,
                null,
                name,
                name.Replace(" ", "-").ToLowerInvariant(),
                null,
                null,
                0,
                null,
                $"https://sp-mod.com/mod/{id}/test",
                null,
                [new ModVersion(1, null, version, null, null, "~4.1.0", null, 0, null, null, null)]
            )
        );
        return mod;
    }

    public static Mod UpToDate(string name = "Fresh Mod")
    {
        var mod = Matched(name);
        mod.UpdateFromUpToDate(new UpToDateMod(1, 7, null, null, "1.0.0", null));
        return mod;
    }

    public static Mod WithUpdate(string name = "Old Mod", string latest = "1.1.0", int id = 7, string installed = "1.0.0")
    {
        var mod = Matched(name, installed, id);
        mod.UpdateFromSafeToUpdate(
            new SafeToUpdateMod(
                new ModUpdateVersion(1, id, null, null, null, installed, null, null),
                new ModUpdateVersion(2, id, null, null, null, latest, "https://dl.example/" + latest, ["4.1.6"]),
                null
            )
        );
        return mod;
    }

    public static Mod LocallyIncompatible(string name = "Broken Mod", string? compatible = "2.0.0")
    {
        var mod = Matched(name);
        mod.SetLocalSptIncompatible("Version 1.0.0 requires SPT ~4.0.0", compatible);
        return mod;
    }
}
