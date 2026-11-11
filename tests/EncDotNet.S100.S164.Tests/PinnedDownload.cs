using System.Security.Cryptography;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// Fetches a test-data archive pinned by SHA-256 into a local cache: the S-164
/// exchange sets and the S-64 test data sets.
/// </summary>
internal static class PinnedDownload
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Whether the environment variable <paramref name="variable"/> turns
    /// downloads on (<c>1</c> or <c>true</c>).
    /// </summary>
    public static bool Enabled(string variable) =>
        Environment.GetEnvironmentVariable(variable) is "1" or "true" or "TRUE" or "True";

    /// <summary>
    /// Returns <paramref name="path"/> once it holds the file whose SHA-256 is
    /// <paramref name="sha256"/>, downloading it from <paramref name="url"/>
    /// when it's missing or stale and <paramref name="downloadVariable"/> turns
    /// downloads on; <see langword="null"/> when it's missing and downloads are
    /// off.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The cached file is stale and downloads are off, or every download attempt failed.
    /// </exception>
    public static async Task<string?> FetchAsync(string path, Uri url, string sha256, string manifestName, string downloadVariable)
    {
        var downloadEnabled = Enabled(downloadVariable);
        if (File.Exists(path))
        {
            if (await HashMatchesAsync(path, sha256))
            {
                return path;
            }

            if (!downloadEnabled)
            {
                throw new InvalidOperationException(
                    $"Cached file '{path}' doesn't match the SHA-256 in {manifestName}. Delete it, or set {downloadVariable}=1 to download it again.");
            }
        }
        else if (!downloadEnabled)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        Exception? last = null;

        // Downloads occasionally fail mid-transfer; a few attempts keep CI
        // from failing on one dropped connection.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var file = File.Create(partial);
                    await response.Content.CopyToAsync(file);
                }

                if (!await HashMatchesAsync(partial, sha256))
                {
                    throw new InvalidDataException(
                        $"Downloaded '{url}' doesn't match the SHA-256 in {manifestName}. The release may have been replaced.");
                }

                File.Move(partial, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        throw new InvalidOperationException($"Couldn't download '{url}' after 3 attempts.", last);
    }

    private static async Task<bool> HashMatchesAsync(string path, string expectedSha256)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return string.Equals(Convert.ToHexStringLower(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
