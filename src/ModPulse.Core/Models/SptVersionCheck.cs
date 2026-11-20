namespace ModPulse.Models;

/// <summary>
/// The installed SPT version and the version used for Forge queries. They differ when the Forge doesn't list the exact
/// installed patch (SPT 3.11.5 isn't on the Forge; its newest 3.11 is 3.11.4): Forge lookups then use the newest patch it
/// knows in the same minor, which is compatible under the same-minor rule.
/// </summary>
/// <param name="Installed">The version read from the install (shown to the user, used for SPT updates and legacy rules).</param>
/// <param name="Forge">The version sent to the Forge API.</param>
public sealed record SptVersionCheck(SemanticVersioning.Version Installed, SemanticVersioning.Version Forge)
{
    /// <summary>True when the Forge doesn't list the installed patch and a same-minor stand-in is used.</summary>
    public bool UsesStandIn
    {
        get { return Installed != Forge; }
    }
}
