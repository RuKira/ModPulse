using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;
using SPTarkov.DI.Annotations;

namespace ModPulse.Services;

/// <summary>
/// File-backed <see cref="IGitHubSourceStore"/>: the user's links in <c>%AppData%/ModPulse/github-sources.json</c>
/// (shared by the CLI and the GUI) over the built-in community list. Disk errors are logged and never fail a run.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class GitHubSourceStore(IOptions<GitHubOptions> options, ILogger<GitHubSourceStore> logger)
    : IGitHubSourceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GitHubOptions _options = options.Value;
    private List<GitHubSource>? _user;

    /// <inheritdoc />
    public IReadOnlyList<GitHubSource> Load()
    {
        var user = UserSources();
        var userKeys = user.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtIn = _options.UseBuiltInSources
            ? BuiltInGitHubSources.Entries.Where(s => !userKeys.Contains(s.Key))
            : [];

        return user.Concat(builtIn).ToList();
    }

    /// <inheritdoc />
    public GitHubSource? Find(Mod mod)
    {
        var key = GitHubSource.KeyFor(mod);
        return Load().FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Link(Mod mod, string repo)
    {
        if (!GitHubRepoName.TryParse(repo, out var ownerRepo))
        {
            throw new ArgumentException($"'{repo}' isn't a GitHub repository (owner/repo).", nameof(repo));
        }

        var key = GitHubSource.KeyFor(mod);
        var user = UserSources().Where(s => !string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
        user.Add(new GitHubSource(key, ownerRepo, mod.DisplayName));
        Save(user);
    }

    /// <inheritdoc />
    public bool Unlink(Mod mod)
    {
        var key = GitHubSource.KeyFor(mod);
        var user = UserSources().ToList();
        if (user.RemoveAll(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        Save(user);
        return true;
    }

    private List<GitHubSource> UserSources()
    {
        return _user ??= Read();
    }

    private List<GitHubSource> Read()
    {
        try
        {
            if (!File.Exists(_options.SourcesFilePath))
            {
                return [];
            }

            var file = JsonSerializer.Deserialize<GitHubSourcesFile>(File.ReadAllText(_options.SourcesFilePath), JsonOptions);
            return file?.Sources?.Where(s => s.IsWellFormed)
                    .Select(s =>
                        s with
                        {
                            Repo = GitHubRepoName.TryParse(s.Repo, out var r) ? r : s.Repo,
                            Origin = GitHubSourceOrigin.User,
                        }
                    )
                    .ToList()
                ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Could not read GitHub links at {Path}; treating it as empty", _options.SourcesFilePath);
            return [];
        }
    }

    private void Save(List<GitHubSource> sources)
    {
        _user = sources;
        try
        {
            var directory = Path.GetDirectoryName(_options.SourcesFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new GitHubSourcesFile(
                GitHubSourcesFile.CurrentSchemaVersion,
                sources.OrderBy(s => s.Name ?? s.Key, StringComparer.OrdinalIgnoreCase).ToList()
            );

            // Atomic write: stage to a temp file then move it into place.
            var temp = _options.SourcesFilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(temp, _options.SourcesFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write GitHub links at {Path}", _options.SourcesFilePath);
        }
    }
}

/// <summary>
/// The community GitHub links as they were when ModPulse was built, embedded from <c>community/github-sources.json</c>.
/// </summary>
public static class BuiltInGitHubSources
{
    private const string ResourceName = "ModPulse.Community.github-sources.json";

    private static readonly Lazy<IReadOnlyList<GitHubSource>> _entries = new(Read);

    /// <summary>The embedded links, tagged <see cref="GitHubSourceOrigin.BuiltIn"/>.</summary>
    public static IReadOnlyList<GitHubSource> Entries
    {
        get { return _entries.Value; }
    }

    private static IReadOnlyList<GitHubSource> Read()
    {
        try
        {
            using var stream = typeof(BuiltInGitHubSources).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return [];
            }

            var file = JsonSerializer.Deserialize<GitHubSourcesFile>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            return file?.Sources?.Where(s => s.IsWellFormed)
                    .Select(s =>
                        s with
                        {
                            Repo = GitHubRepoName.TryParse(s.Repo, out var r) ? r : s.Repo,
                            Origin = GitHubSourceOrigin.BuiltIn,
                        }
                    )
                    .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
