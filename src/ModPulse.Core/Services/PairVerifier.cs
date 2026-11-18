using ModPulse.Models;
using ModPulse.Services.Interfaces;

namespace ModPulse.Services;

/// <summary>
/// Checks server/client pairs whose GUIDs differ against the Forge. Pairing is decided from local names and GUIDs only,
/// which can join two different mods (Fika's server "Fika" and KeepMeAlive's Fika client "com.keepmealive.fika") or two
/// halves of one project that the Forge lists separately ("Fika" and "com.fika.core").
/// <list type="bullet">
/// <item>Both GUIDs resolve to different Forge mods: the pair is split back into two mods.</item>
/// <item>Otherwise the pair stays, and the selected half also carries the other half's GUID, so matching can use
/// whichever one the Forge knows.</item>
/// </list>
/// </summary>
public static class PairVerifier
{
    /// <summary>
    /// Verifies <paramref name="reconciliation"/>. <paramref name="lookupForgeId"/> returns the Forge mod id a GUID
    /// belongs to, or null when the Forge doesn't know it.
    /// </summary>
    /// <returns>The reconciliation with any split pairs moved back to the unmatched lists.</returns>
    public static async Task<ModReconciliationResult> VerifyAsync(
        ModReconciliationResult reconciliation,
        Func<string, Task<int?>> lookupForgeId
    )
    {
        var kept = new List<ModPair>();
        var splitServers = new List<Mod>();
        var splitClients = new List<Mod>();

        foreach (var pair in reconciliation.ReconciledPairs)
        {
            var serverGuid = pair.ServerMod.Guid;
            var clientGuid = pair.ClientMod.Guid;

            if (
                string.IsNullOrWhiteSpace(serverGuid)
                || string.IsNullOrWhiteSpace(clientGuid)
                || string.Equals(serverGuid, clientGuid, StringComparison.OrdinalIgnoreCase)
            )
            {
                kept.Add(pair);
                continue;
            }

            var serverId = await lookupForgeId(serverGuid);
            var clientId = await lookupForgeId(clientGuid);

            if (serverId is not null && clientId is not null && serverId != clientId)
            {
                // Two Forge listings: check each half on its own.
                pair.ServerMod.PairedComponentPath = null;
                pair.ClientMod.PairedComponentPath = null;
                splitServers.Add(pair.ServerMod);
                splitClients.Add(pair.ClientMod);
                continue;
            }

            // One mod: let matching try the other half's GUID too.
            var other = ReferenceEquals(pair.SelectedMod, pair.ServerMod) ? pair.ClientMod : pair.ServerMod;
            if (
                !string.Equals(other.Guid, pair.SelectedMod.Guid, StringComparison.OrdinalIgnoreCase)
                && !pair.SelectedMod.AlternateGuids.Contains(other.Guid, StringComparer.OrdinalIgnoreCase)
            )
            {
                pair.SelectedMod.AlternateGuids.Add(other.Guid);
            }

            kept.Add(pair);
        }

        if (splitServers.Count == 0)
        {
            return reconciliation;
        }

        var unmatchedServer = reconciliation.UnmatchedServerMods.Concat(splitServers).ToList();
        var unmatchedClient = reconciliation.UnmatchedClientMods.Concat(splitClients).ToList();

        return new ModReconciliationResult
        {
            Mods = kept.Select(p => p.SelectedMod).Concat(unmatchedServer).Concat(unmatchedClient).ToList(),
            ReconciledPairs = kept,
            UnmatchedServerMods = unmatchedServer,
            UnmatchedClientMods = unmatchedClient,
        };
    }
}
