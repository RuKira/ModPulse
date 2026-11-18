using Microsoft.Extensions.Logging.Abstractions;
using ModPulse.Models;
using ModPulse.Services;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Tests;

/// <summary>
/// Server/client pairing on a real Fika install: Fika's server ("Fika") must not pair with KeepMeAlive's Fika client
/// ("com.keepmealive.fika"); ORBIT/VisitAPI-style "*.server" GUIDs pair without warnings; halves the Forge lists
/// separately are split again.
/// </summary>
public sealed class PairingTests
{
    private static Mod Server(string guid, string name, string version = "1.0.0")
    {
        return new Mod
        {
            Guid = guid,
            FilePath = $"server/{name}.dll",
            IsServerMod = true,
            LocalName = name,
            LocalAuthor = "Author",
            LocalVersion = version,
        };
    }

    private static Mod Client(string guid, string name, string version = "1.0.0")
    {
        return new Mod
        {
            Guid = guid,
            FilePath = $"client/{name}.dll",
            IsServerMod = false,
            LocalName = name,
            LocalAuthor = "Author",
            LocalVersion = version,
        };
    }

    private static ModReconciliationResult Reconcile(List<Mod> servers, List<Mod> clients)
    {
        return new ModReconciliationService(NullLogger<ModReconciliationService>.Instance).ReconcileMods(servers, clients);
    }

    [Theory]
    [InlineData("com.chazut.orbit.server", "orbit")]
    [InlineData("com.chazut.orbit", "orbit")]
    [InlineData("com.keepmealive.fika", "keepmealive")]
    [InlineData("com.fika.core", "fika")]
    [InlineData("com.manimal.icebreaker.fika", "icebreaker")]
    [InlineData("Fika", "Fika")]
    [InlineData("author.server", "server")]
    [InlineData("KeepMeAliveServer", "KeepMeAliveServer")]
    public void Guid_name_skips_a_trailing_part_or_integration_segment(string guid, string expected)
    {
        Assert.Equal(expected, ModNameNormalizer.ExtractNameFromGuid(guid));
    }

    [Theory]
    [InlineData("com.chazut.orbit.server", "com.chazut.orbit", true)]
    [InlineData("com.sora.visitapi.server", "com.sora.visitapi", true)]
    [InlineData("com.manimal.icebreaker", "com.manimal.icebreaker.fika", true)]
    [InlineData("com.author.mod.server", "com.author.mod.client", true)]
    [InlineData("Fika", "com.fika.core", false)]
    [InlineData("Fika", "com.keepmealive.fika", false)]
    [InlineData("author.server", "author", false)]
    [InlineData("com.author.mod", "com.author.mod", false)]
    public void Component_guid_convention(string a, string b, bool expected)
    {
        Assert.Equal(expected, ModNameNormalizer.AreComponentGuids(a, b));
    }

    [Fact]
    public void Fika_server_is_not_paired_with_keepmealives_fika_client()
    {
        var fikaServer = Server("Fika", "server", "2.4.2");
        var kmaServer = Server("KeepMeAliveServer", "KeepMeAlive Server", "1.1.0");
        var kmaClient = Client("com.keepmealive.fika", "KeepMeAlive", "1.1.0");

        var result = Reconcile([fikaServer, kmaServer], [kmaClient]);

        var pair = Assert.Single(result.ReconciledPairs);
        Assert.Same(kmaServer, pair.ServerMod);
        Assert.Same(kmaClient, pair.ClientMod);
        Assert.Contains(fikaServer, result.UnmatchedServerMods);
    }

    [Fact]
    public void Server_guid_convention_pairs_without_a_mismatch_note()
    {
        var result = Reconcile(
            [Server("com.chazut.orbit.server", "ORBIT Server", "3.1.1")],
            [Client("com.chazut.orbit", "ORBIT", "3.1.1")]
        );

        Assert.Empty(Assert.Single(result.ReconciledPairs).Notes);
    }

    [Fact]
    public async Task Pair_of_two_forge_listings_is_split()
    {
        // Fika's server and client are separate Forge mods (Project Fika - Server / Project Fika).
        var server = Server("Fika", "server", "2.4.2");
        var client = Client("com.fika.core", "Fika.Core", "2.4.6");
        var reconciliation = Reconcile([server], [client]);
        Assert.Single(reconciliation.ReconciledPairs);

        var verified = await PairVerifier.VerifyAsync(
            reconciliation,
            guid => Task.FromResult<int?>(guid == "Fika" ? 2357 : 1001)
        );

        Assert.Empty(verified.ReconciledPairs);
        Assert.Equal(2, verified.Mods.Count);
        Assert.Contains(server, verified.UnmatchedServerMods);
        Assert.Contains(client, verified.UnmatchedClientMods);
        Assert.Null(server.PairedComponentPath);
    }

    [Fact]
    public async Task Pair_with_one_known_guid_stays_and_carries_the_other_guid()
    {
        // KeepMeAlive: only the client GUID is on the Forge (K.M.A. - Fika, 3269).
        var server = Server("KeepMeAliveServer", "KeepMeAlive Server", "1.1.0");
        var client = Client("com.keepmealive.fika", "KeepMeAlive", "1.1.0");
        var reconciliation = Reconcile([server], [client]);

        var verified = await PairVerifier.VerifyAsync(
            reconciliation,
            guid => Task.FromResult<int?>(guid == "com.keepmealive.fika" ? 3269 : null)
        );

        var pair = Assert.Single(verified.ReconciledPairs);
        Assert.Contains("com.keepmealive.fika", pair.SelectedMod.AlternateGuids);
    }

    [Fact]
    public async Task Pairs_resolving_to_the_same_forge_mod_stay_together()
    {
        var reconciliation = Reconcile(
            [Server("com.chazut.orbit.server", "ORBIT Server")],
            [Client("com.chazut.orbit", "ORBIT")]
        );

        var verified = await PairVerifier.VerifyAsync(reconciliation, _ => Task.FromResult<int?>(2706));

        Assert.Single(verified.ReconciledPairs);
    }
}
