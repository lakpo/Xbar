using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Xbar.Core;

namespace Xbar.Update;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }

    /// <summary>Empreinte calculee par GitHub, de la forme "sha256:&lt;hex&gt;". Absente sur les anciennes versions.</summary>
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

/// <summary>
/// Recherche des nouvelles versions via GitHub Releases. Ce qu'on en fait (installation
/// automatique ou simple signalement) est decide par UpdateService.
/// </summary>
public static class UpdateChecker
{
    /// <summary>Nom du fichier publie avec chaque version, celui que l'on telecharge.</summary>
    public const string ExecutableAssetName = "Xbar.exe";

    public sealed record UpdateInfo(string Version, string Url, GitHubAsset? Executable);

    /// <summary>
    /// Derniere version publiee si elle est plus recente que celle-ci, sinon null. Leve une
    /// exception si GitHub ne repond pas : l'appelant distingue ainsi "a jour" de "injoignable".
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(10));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var url = $"https://api.github.com/repos/{AppInfo.GitHubRepository}/releases/latest";
        var json = await client.GetStringAsync(url, token).ConfigureAwait(false);

        var release = JsonStore.Deserialize(json, JsonContext.Default.GitHubRelease);
        if (release is null || release.Draft || release.PreRelease) return null;

        var latest = ParseVersion(release.TagName);
        var current = ParseVersion(AppInfo.Version);
        if (latest is null || current is null || latest <= current) return null;

        var executable = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, ExecutableAssetName, StringComparison.OrdinalIgnoreCase));

        Log.Info($"Nouvelle version disponible : {release.TagName}" +
                 (executable is null ? " (sans executable joint)" : ""));
        return new UpdateInfo(release.TagName.TrimStart('v', 'V'), release.HtmlUrl, executable);
    }

    internal static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Xbar", AppInfo.Version));
        return client;
    }

    private static Version? ParseVersion(string value)
    {
        value = value.TrimStart('v', 'V').Trim();
        return Version.TryParse(value, out var version) ? version : null;
    }
}
