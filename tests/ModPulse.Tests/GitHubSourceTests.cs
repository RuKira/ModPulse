using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Tests;

/// <summary>
/// GitHub sources for off-Forge mods: reading repositories and tag versions, the release client (release, tag fallback,
/// rate limit), the verdicts, the links file, and ignoring GitHub releases.
/// </summary>
public sealed class GitHubSourceTests : IDisposable
{
    private static readonly SemanticVersioning.Version Spt41 = new(4, 1, 6);
    private static readonly SemanticVersioning.Version Spt310 = new(3, 10, 6);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modpulse-github-tests", Guid.NewGuid().ToString("N"));

    public GitHubSourceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    // ----- Repository names -----

    [Theory]
    [InlineData("owner/repo", "owner/repo")]
    [InlineData("  Some-Owner/My.Mod_2  ", "Some-Owner/My.Mod_2")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("github.com/owner/repo/releases/tag/v1.0.0", "owner/repo")]
    [InlineData("https://www.github.com/owner/repo?tab=readme", "owner/repo")]
    public void Repository_is_read_from_names_and_links(string input, string expected)
    {
        Assert.True(GitHubRepoName.TryParse(input, out var repo));
        Assert.Equal(expected, repo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("repo")]
    [InlineData("owner/repo/extra")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("not a repo")]
    [InlineData("-owner/repo")]
    public void Anything_else_is_not_a_repository(string input)
    {
        Assert.False(GitHubRepoName.TryParse(input, out _));
    }

    // ----- Versions -----

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0.0")]
    [InlineData("MyMod-1.2.3", "1.2.3")]
    [InlineData("MyMod_v1.2.3", "1.2.3")]
    [InlineData("release v4", "4.0.0")]
    [InlineData("v1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("1.2.3.4", "1.2.3")]
    public void Tag_versions_are_read(string tag, string expected)
    {
        Assert.Equal(expected, ReleaseVersion.FromTag(tag)?.ToString());
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v1.2.3x")]
    [InlineData("nightly-build")]
    [InlineData("")]
    public void Tags_without_a_version_are_not_guessed(string tag)
    {
        Assert.Null(ReleaseVersion.FromTag(tag));
    }

    [Fact]
    public void Installed_assembly_versions_compare_with_tags()
    {
        Assert.Equal(ReleaseVersion.FromTag("v1.2.3"), ReleaseVersion.FromInstalled("1.2.3.0"));
        Assert.Equal(ReleaseVersion.FromTag("v1.2.3"), ReleaseVersion.FromInstalled("1.2.3+abc123"));
    }

    // ----- Release client -----

    private const string ReleaseJson = """
        {"tag_name":"v1.4.0","name":"Big update","body":"Works on 3.10 and 3.11","html_url":"https://github.com/o/r/releases/tag/v1.4.0",
         "published_at":"2026-10-01T12:00:00Z","assets":[{"name":"Mod.zip"},{"name":"Mod.7z"}]}
        """;

    [Fact]
    public void Release_json_is_parsed()
    {
        var release = GitHubReleaseClient.ParseRelease(ReleaseJson)!;

        Assert.Equal("v1.4.0", release.Tag);
        Assert.Equal("Big update", release.Name);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.4.0", release.Url);
        Assert.Equal(["Mod.zip", "Mod.7z"], release.Assets);
        Assert.False(release.FromTags);
    }

    [Fact]
    public void Tags_pick_the_highest_stable_version()
    {
        const string tags = """[{"name":"v2.0.0-rc.1"},{"name":"latest"},{"name":"v1.10.0"},{"name":"v1.9.2"}]""";

        var release = GitHubReleaseClient.PickTag(tags, "o/r")!;

        Assert.Equal("v1.10.0", release.Tag);
        Assert.True(release.FromTags);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.10.0", release.Url);
    }

    [Fact]
    public void Tags_fall_back_to_a_pre_release_when_nothing_else_has_a_version()
    {
        Assert.Equal("v2.0.0-rc.1", GitHubReleaseClient.PickTag("""[{"name":"v2.0.0-rc.1"},{"name":"main"}]""", "o/r")!.Tag);
        Assert.Null(GitHubReleaseClient.PickTag("""[{"name":"main"}]""", "o/r"));
    }

    private static GitHubReleaseClient Client(FakeHandler handler, string? token = null)
    {
        return new GitHubReleaseClient(
            new HttpClient(handler),
            new NoCache(),
            Options.Create(new GitHubOptions { Token = token, ApiBaseUrl = "https://api.test" }),
            NullLogger<GitHubReleaseClient>.Instance
        );
    }

    [Fact]
    public async Task Latest_release_is_used_and_the_token_is_sent()
    {
        var handler = new FakeHandler { ["/repos/o/r/releases/latest"] = (HttpStatusCode.OK, ReleaseJson) };

        var lookup = await Client(handler, token: "secret").GetLatestAsync("o/r");

        Assert.Equal(GitHubLookupStatus.Found, lookup.Status);
        Assert.Equal("v1.4.0", lookup.Release!.Tag);
        Assert.Equal("Bearer secret", handler.Authorization);
    }

    [Fact]
    public async Task No_release_falls_back_to_tags()
    {
        var handler = new FakeHandler
        {
            ["/repos/o/r/releases/latest"] = (HttpStatusCode.NotFound, "{}"),
            ["/repos/o/r/tags?per_page=100"] = (HttpStatusCode.OK, """[{"name":"1.0.1"},{"name":"1.0.0"}]"""),
        };

        var lookup = await Client(handler).GetLatestAsync("o/r");

        Assert.Equal(GitHubLookupStatus.Found, lookup.Status);
        Assert.Equal("1.0.1", lookup.Release!.Tag);
    }

    [Fact]
    public async Task Missing_repository_and_empty_repository_are_told_apart()
    {
        var missing = new FakeHandler
        {
            ["/repos/o/r/releases/latest"] = (HttpStatusCode.NotFound, "{}"),
            ["/repos/o/r/tags?per_page=100"] = (HttpStatusCode.NotFound, "{}"),
        };
        var empty = new FakeHandler
        {
            ["/repos/o/r/releases/latest"] = (HttpStatusCode.NotFound, "{}"),
            ["/repos/o/r/tags?per_page=100"] = (HttpStatusCode.OK, "[]"),
        };

        Assert.Equal(GitHubLookupStatus.NotFound, (await Client(missing).GetLatestAsync("o/r")).Status);
        Assert.Equal(GitHubLookupStatus.NoReleases, (await Client(empty).GetLatestAsync("o/r")).Status);
    }

    [Fact]
    public async Task Used_up_rate_limit_is_reported_with_its_reset_time()
    {
        var handler = new FakeHandler
        {
            ["/repos/o/r/releases/latest"] = (HttpStatusCode.Forbidden, "{}"),
            RateLimitReset = 1_900_000_000,
        };

        var lookup = await Client(handler).GetLatestAsync("o/r");

        Assert.Equal(GitHubLookupStatus.RateLimited, lookup.Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_900_000_000), lookup.RateLimitResetUtc);
    }

    // ----- Verdicts -----

    private static readonly GitHubSource Link = new("com.test.mod", "o/r");

    private static GitHubLookup Found(string tag, string? body = null)
    {
        return new GitHubLookup(
            GitHubLookupStatus.Found,
            new GitHubRelease(tag, null, body, "https://github.com/o/r/releases/tag/" + tag, null, [])
        );
    }

    [Theory]
    [InlineData("1.0.0", "v1.1.0", GitHubMatchStatus.UpdateAvailable)]
    [InlineData("1.1.0.0", "v1.1.0", GitHubMatchStatus.UpToDate)]
    [InlineData("1.2.0", "v1.1.0", GitHubMatchStatus.NewerInstalled)]
    [InlineData("1.0.0", "nightly", GitHubMatchStatus.Unknown)]
    [InlineData("unknown", "v1.1.0", GitHubMatchStatus.Unknown)]
    public void Latest_release_is_compared_with_the_installed_version(
        string installed,
        string tag,
        GitHubMatchStatus expected
    )
    {
        var match = GitHubUpdateResolver.Evaluate(installed, Link, Found(tag), Spt41);

        Assert.Equal(expected, match.Status);
        Assert.Equal("o/r", match.Repo);
    }

    [Fact]
    public void An_update_never_claims_spt_compatibility()
    {
        var match = GitHubUpdateResolver.Evaluate("1.0.0", Link, Found("v1.1.0"), Spt41);

        Assert.Contains("check it supports SPT 4.1.6", match.Reason);
        Assert.Equal("1.1.0", match.Version);
    }

    [Fact]
    public void Legacy_spt_holds_back_releases_that_dont_mention_its_version()
    {
        Assert.Equal(
            GitHubMatchStatus.HeldBack,
            GitHubUpdateResolver.Evaluate("1.0.0", Link, Found("v1.1.0", "For SPT 3.11"), Spt310).Status
        );
        Assert.Equal(
            GitHubMatchStatus.UpdateAvailable,
            GitHubUpdateResolver.Evaluate("1.0.0", Link, Found("v1.1.0", "Supports SPT 3.10 and 3.11"), Spt310).Status
        );
    }

    [Theory]
    [InlineData("for 3.10.x", true)]
    [InlineData("SPT 3.10", true)]
    [InlineData("v3.100 notes", false)]
    [InlineData("13.10 something", false)]
    [InlineData("3.1", false)]
    public void Spt_line_mentions_are_whole_versions(string body, bool expected)
    {
        var release = new GitHubRelease("v1", null, body, "u", null, []);

        Assert.Equal(expected, GitHubUpdateResolver.MentionsSptLine(release, "3.10"));
    }

    [Theory]
    [InlineData(GitHubLookupStatus.NotFound, "wasn't found")]
    [InlineData(GitHubLookupStatus.NoReleases, "no releases")]
    [InlineData(GitHubLookupStatus.RateLimited, "rate limit")]
    [InlineData(GitHubLookupStatus.Failed, "Couldn't reach GitHub")]
    public void Lookup_problems_leave_the_mod_unknown_with_a_reason(GitHubLookupStatus status, string reason)
    {
        var match = GitHubUpdateResolver.Evaluate("1.0.0", Link, new GitHubLookup(status), Spt41);

        Assert.Equal(GitHubMatchStatus.Unknown, match.Status);
        Assert.Contains(reason, match.Reason);
    }

    private static Mod OffForge(string name, string guid, string version = "1.0.0")
    {
        return new Mod
        {
            Guid = guid,
            FilePath = $@"F:\SPT\BepInEx\plugins\{name}\{name}.dll",
            IsServerMod = false,
            LocalName = name,
            LocalAuthor = "Author",
            LocalVersion = version,
        };
    }

    [Fact]
    public async Task Resolver_checks_each_repository_once_and_stops_at_the_rate_limit()
    {
        var a = OffForge("A", "com.a.a");
        var a2 = OffForge("A Client", "com.a.client");
        var b = OffForge("B", "com.b.b");
        var unlinked = OffForge("C", "com.c.c");
        var store = new FakeStore(new GitHubSource("com.a.a", "o/a"), new GitHubSource("com.a.client", "o/a"), new GitHubSource("com.b.b", "o/b"));
        var client = new FakeClient
        {
            ["o/a"] = new GitHubLookup(GitHubLookupStatus.RateLimited, RateLimitResetUtc: DateTimeOffset.UnixEpoch.AddDays(1)),
            ["o/b"] = Found("v2.0.0"),
        };

        var summary = await GitHubUpdateResolver.ResolveAsync([a, a2, b, unlinked], store, client, Spt41);

        Assert.Equal(3, summary.Checked);
        Assert.NotNull(summary.RateLimitedUntil);
        Assert.Equal(["o/a"], client.Requested); // o/b is never asked once the limit is hit
        Assert.All([a, a2, b], m => Assert.Equal(GitHubMatchStatus.Unknown, m.GitHub!.Status));
        Assert.Null(unlinked.GitHub);
    }

    // ----- Links file -----

    private GitHubSourceStore NewStore(bool builtIn = false)
    {
        return new GitHubSourceStore(
            Options.Create(
                new GitHubOptions { SourcesFilePath = Path.Combine(_dir, "github-sources.json"), UseBuiltInSources = builtIn }
            ),
            NullLogger<GitHubSourceStore>.Instance
        );
    }

    [Fact]
    public void Built_in_links_apply_until_the_user_links_the_mod_themselves()
    {
        var mod = OffForge("SkillMultiplier", "dazzuh.skillmultiplier");

        var builtIn = NewStore(builtIn: true).Find(mod)!;
        Assert.Equal("Dazzuh/SkillMultiplier", builtIn.Repo);
        Assert.Equal(GitHubSourceOrigin.BuiltIn, builtIn.Origin);
        Assert.False(NewStore(builtIn: true).Unlink(mod)); // a built-in link can't be removed

        NewStore(builtIn: true).Link(mod, "Someone/Fork");
        var own = NewStore(builtIn: true).Find(mod)!;
        Assert.Equal("Someone/Fork", own.Repo);
        Assert.Equal(GitHubSourceOrigin.User, own.Origin);
        Assert.Single(NewStore(builtIn: true).Load(), s => s.Key == mod.Guid);
    }

    [Fact]
    public void Links_are_saved_found_changed_and_removed()
    {
        var mod = OffForge("Skill Multiplier", "com.dazzuh.skillmultiplier");

        NewStore().Link(mod, "https://github.com/Dazzuh/SkillMultiplier");
        Assert.Equal("Dazzuh/SkillMultiplier", NewStore().Find(mod)!.Repo);
        Assert.Equal(GitHubSourceOrigin.User, NewStore().Find(mod)!.Origin);

        NewStore().Link(mod, "Other/Fork");
        Assert.Equal("Other/Fork", Assert.Single(NewStore().Load()).Repo);

        Assert.True(NewStore().Unlink(mod));
        Assert.Null(NewStore().Find(mod));
        Assert.False(NewStore().Unlink(mod));
    }

    [Fact]
    public void A_mod_without_a_guid_is_linked_by_its_name()
    {
        var mod = OffForge("Old Server Mod", string.Empty);

        NewStore().Link(mod, "o/r");

        Assert.StartsWith("name:", GitHubSource.KeyFor(mod));
        Assert.Equal("o/r", NewStore().Find(OffForge("Old Server Mod", string.Empty))!.Repo);
    }

    [Fact]
    public void Bad_link_entries_and_unreadable_files_are_ignored()
    {
        File.WriteAllText(
            Path.Combine(_dir, "github-sources.json"),
            """{"schemaVersion":1,"sources":[{"key":"com.a","repo":"not a repo"},{"key":"","repo":"o/r"},{"key":"com.b","repo":"o/b"}]}"""
        );
        Assert.Equal("o/b", Assert.Single(NewStore().Load()).Repo);

        File.WriteAllText(Path.Combine(_dir, "github-sources.json"), "{ broken");
        Assert.Empty(NewStore().Load());
    }

    // ----- Ignoring GitHub releases -----

    private static Mod WithGitHubUpdate()
    {
        var mod = OffForge("Terminal Commander", "com.mizmii.terminalcommander", "1.2.0");
        mod.SetGitHub(GitHubUpdateResolver.Evaluate("1.2.0", new GitHubSource(mod.Guid, "mizmii/TC"), Found("v1.3.0"), Spt41));
        return mod;
    }

    [Fact]
    public void A_github_release_is_ignored_by_repository_and_versions()
    {
        var entry = IgnoredUpdate.For(WithGitHubUpdate())!;

        Assert.True(entry.IsGitHub);
        Assert.True(entry.IsWellFormed);
        Assert.Equal("github:mizmii/TC|1.2.0|1.3.0", entry.Key);
        Assert.Equal("github:mizmii/TC", entry.ModIdentity);
    }

    [Fact]
    public void Ignore_store_matches_github_releases()
    {
        var store = new IgnoredUpdateStore(
            Options.Create(new IgnoredUpdateOptions { FilePath = Path.Combine(_dir, "ignored.json"), RemoteUrl = null }),
            NullLogger<IgnoredUpdateStore>.Instance
        );
        var mod = WithGitHubUpdate();
        Assert.False(store.IsIgnored(mod));

        store.Save([IgnoredUpdate.For(mod)!]);

        Assert.True(store.IsIgnored(mod));
        Assert.Equal(IgnoreSource.User, store.GetIgnoreSource(mod));
    }

    [Fact]
    public void Rewriting_ignores_keeps_github_entries_for_repositories_not_checked_this_run()
    {
        var github = new IgnoredUpdate(0, "1.0.0", "1.1.0", GitHubRepo: "o/r");
        var forge = new IgnoredUpdate(5, "1.0.0", "2.0.0");

        var result = IgnoredUpdateWorkflow.BuildNewSet([github, forge], new HashSet<string> { "5" }, []);

        Assert.Same(github, Assert.Single(result));
    }

    [Fact]
    public void Update_pages_include_github_releases()
    {
        var urls = IgnoredUpdateWorkflow.BuildUpdatePageUrls([WithGitHubUpdate()]);

        Assert.Equal(["https://github.com/o/r/releases/tag/v1.3.0"], urls);
    }

    // ----- CLI order -----

    [Fact]
    public void Cli_lists_are_sorted_by_name_ignoring_case()
    {
        Mod[] mods = [OffForge("zebra", "z"), OffForge("Apple", "a"), OffForge("mango", "m")];

        Assert.Equal(["Apple", "mango", "zebra"], SpectreModCheckReporter.ByName(mods).Select(m => m.DisplayName));
    }

    // ----- Fakes -----

    private sealed class NoCache : IForgeResponseCache
    {
        public bool TryGet(string url, out int statusCode, out string body)
        {
            statusCode = 0;
            body = string.Empty;
            return false;
        }

        public void Set(string url, int statusCode, string body) { }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = [];

        public (HttpStatusCode Status, string Body) this[string pathAndQuery]
        {
            set { _responses[pathAndQuery] = value; }
        }

        public long? RateLimitReset { get; init; }

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            var key = request.RequestUri!.PathAndQuery;
            var (status, body) = _responses.TryGetValue(key, out var r) ? r : (HttpStatusCode.InternalServerError, "");
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (RateLimitReset is { } reset)
            {
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.Add("X-RateLimit-Reset", reset.ToString());
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FakeStore(params GitHubSource[] sources) : IGitHubSourceStore
    {
        public IReadOnlyList<GitHubSource> Load()
        {
            return sources;
        }

        public GitHubSource? Find(Mod mod)
        {
            return sources.FirstOrDefault(s => s.Key == GitHubSource.KeyFor(mod));
        }

        public void Link(Mod mod, string repo) { }

        public bool Unlink(Mod mod)
        {
            return false;
        }
    }

    private sealed class FakeClient : IGitHubReleaseClient
    {
        private readonly Dictionary<string, GitHubLookup> _lookups = [];

        public GitHubLookup this[string repo]
        {
            set { _lookups[repo] = value; }
        }

        public List<string> Requested { get; } = [];

        public Task<GitHubLookup> GetLatestAsync(string repo, CancellationToken cancellationToken = default)
        {
            Requested.Add(repo);
            return Task.FromResult(_lookups.TryGetValue(repo, out var l) ? l : new GitHubLookup(GitHubLookupStatus.Failed));
        }
    }
}
