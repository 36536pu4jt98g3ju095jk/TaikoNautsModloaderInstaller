using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace TaikoNautsModloaderInstaller.Core;

internal sealed record ReleaseAsset(string Name, long Size, string Url, string? Sha256);

internal sealed record ReleaseInfo(string Tag, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>"v1.3.1" becomes 1.3.1; null when the tag is not a version.</summary>
    public Version? Version
    {
        get
        {
            string text = Tag.TrimStart('v', 'V');
            return Version.TryParse(text, out Version? parsed) ? parsed : null;
        }
    }
}

/// <summary>Reads releases of public repositories and downloads their assets with a SHA-256 check.</summary>
internal static class GitHubReleases
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaikoNautsModloaderInstaller", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<ReleaseInfo> LatestAsync(string repository, CancellationToken cancellation)
    {
        string url = $"https://api.github.com/repos/{repository}/releases/latest";
        using HttpResponseMessage response = await Http.GetAsync(url, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallException(
                $"GitHub returned {(int)response.StatusCode} for {repository}.",
                $"GitHub が {repository} に対して {(int)response.StatusCode} を返しました。");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var assets = new List<ReleaseAsset>();
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string? digest = asset.TryGetProperty("digest", out JsonElement value) ? value.GetString() : null;
            string? sha256 = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..].ToUpperInvariant()
                : null;
            assets.Add(new ReleaseAsset(
                asset.GetProperty("name").GetString() ?? string.Empty,
                asset.GetProperty("size").GetInt64(),
                asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                sha256));
        }

        return new ReleaseInfo(tag, assets);
    }

    public static ReleaseAsset Pick(ReleaseInfo release, string repository, Func<string, bool> match)
    {
        ReleaseAsset? asset = release.Assets.FirstOrDefault(candidate => match(candidate.Name));
        if (asset == null)
        {
            throw new InstallException(
                $"Release {release.Tag} of {repository} has no matching file.",
                $"{repository} のリリース {release.Tag} に、対象のファイルがありません。");
        }

        return asset;
    }

    /// <summary>Downloads the asset into the folder and checks it against the digest GitHub publishes.</summary>
    public static async Task<string> DownloadAsync(
        ReleaseAsset asset,
        string directory,
        IProgress<double>? progress,
        CancellationToken cancellation)
    {
        if (asset.Sha256 == null)
        {
            throw new InstallException(
                $"{asset.Name} has no published SHA-256 digest, so it cannot be verified.",
                $"{asset.Name} には SHA-256 が公開されておらず、検証できません。");
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, asset.Name);
        using HttpResponseMessage response = await Http
            .GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? asset.Size;

        using var hash = SHA256.Create();
        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
        await using (FileStream target = File.Create(path))
        {
            byte[] buffer = new byte[128 * 1024];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                hash.TransformBlock(buffer, 0, read, null, 0);
                done += read;
                if (total > 0)
                {
                    progress?.Report((double)done / total);
                }
            }

            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }

        string actual = Convert.ToHexString(hash.Hash!);
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InstallException(
                $"The downloaded {asset.Name} does not match its published SHA-256, so it was discarded.",
                $"ダウンロードした {asset.Name} の SHA-256 が公開値と一致しないため、破棄しました。");
        }

        return path;
    }
}
