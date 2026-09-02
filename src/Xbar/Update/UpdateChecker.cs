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
}

/// <summary>
/// Verification discrete des nouvelles versions via GitHub Releases.
/// Aucune mise a jour n'est jamais appliquee automatiquement : Xbar se contente de signaler
/// qu'une version existe. La verification peut etre desactivee dans les parametres.
/// </summary>
public static class UpdateChecker
{
    public sealed record UpdateInfo(string Version, string Url);

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        if (!SettingsStore.Current.CheckForUpdates) return null;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Xbar", AppInfo.Version));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var url = $"https://api.github.com/repos/{AppInfo.GitHubRepository}/releases/latest";
            var json = await client.GetStringAsync(url, token).ConfigureAwait(false);

            var release = JsonStore.Deserialize(json, JsonContext.Default.GitHubRelease);
            if (release is null || release.Draft || release.PreRelease) return null;

            var latest = ParseVersion(release.TagName);
            var current = ParseVersion(AppInfo.Version);
            if (latest is null || current is null || latest <= current) return null;

            Log.Info($"Nouvelle version disponible : {release.TagName}");
            return new UpdateInfo(release.TagName, release.HtmlUrl);
        }
        catch (Exception ex)
        {
            Log.Debug($"Verification des mises a jour impossible : {ex.Message}");
            return null;
        }
    }

    private static Version? ParseVersion(string value)
    {
        value = value.TrimStart('v', 'V').Trim();
        return Version.TryParse(value, out var version) ? version : null;
    }
}
