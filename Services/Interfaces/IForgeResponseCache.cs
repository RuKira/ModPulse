namespace ModPulse.Services.Interfaces;

/// <summary>
/// Persistent cache of Forge API responses, keyed by request URL.
/// </summary>
public interface IForgeResponseCache
{
    /// <summary>
    /// Returns a cached, unexpired response for <paramref name="url"/>, if there is one.
    /// </summary>
    bool TryGet(string url, out int statusCode, out string body);

    /// <summary>
    /// Stores a response for <paramref name="url"/>; its lifetime depends on the endpoint.
    /// </summary>
    void Set(string url, int statusCode, string body);
}
