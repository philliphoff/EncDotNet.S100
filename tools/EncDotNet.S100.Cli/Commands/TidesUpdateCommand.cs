using System.ComponentModel;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Mirror;
using EncDotNet.S100.Tides.Stations.Noaa;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tides update</c>: downloads NOAA CO-OPS's tide stations into the
/// offline catalogue the other <c>s100 tides</c> commands and
/// <c>s100 mcp serve</c> read, or with <c>--check</c> asks whether there's
/// newer data than the catalogue: from NOAA itself, or from a mirror that
/// publishes NOAA's catalogue as one file (#1674).
/// </summary>
internal sealed class TidesUpdateCommand : AsyncCommand<TidesUpdateCommand.Settings>
{
    /// <summary>The <c>--from</c> value for NOAA's metadata API.</summary>
    public const string FromNoaa = "noaa";

    /// <summary>The <c>--from</c> value for the EncDotNet.S100 mirror.</summary>
    public const string FromMirror = "mirror";

    /// <summary>Makes the HTTP client the command downloads with; replaced by tests.</summary>
    internal static Func<HttpClient> HttpFactory { get; set; } = () => new HttpClient();

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Only check whether there's newer data than the catalogue (a few requests, or one from a mirror); download nothing. Exits with 0 when it's current and 8 when there's newer data.")]
        public bool Check { get; init; }

        [CommandOption("--catalogue <FILE>")]
        [Description("Where to keep the catalogue: a .json or .json.gz file. Defaults to the offline cache in your local application data.")]
        public string? Catalogue { get; init; }

        [CommandOption("--from <SOURCE>")]
        [Description("Where to download from: 'noaa' (NOAA's metadata API, about 10 minutes; the default), 'mirror' (the EncDotNet.S100 mirror of NOAA's catalogue, one file), or the URL of a mirror's tide-stations.manifest.json.")]
        public string From { get; init; } = FromNoaa;

        public override ValidationResult Validate() =>
            TryGetMirrorAddress(From, out _)
                ? ValidationResult.Success()
                : ValidationResult.Error($"--from must be '{FromNoaa}', '{FromMirror}' or an http(s) URL of a mirror's manifest, not '{From}'.");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var path = TideCli.CataloguePath(settings.Catalogue);
        using var http = HttpFactory();
        http.Timeout = TimeSpan.FromMinutes(2);
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("EncDotNet.S100.Cli");
        TryGetMirrorAddress(settings.From, out var mirror);

        try
        {
            if (mirror is not null)
            {
                var client = new TideCatalogueMirrorClient(http, new TideCatalogueMirrorClientOptions { ManifestAddress = mirror });
                return settings.Check
                    ? await CheckMirrorAsync(client, path, cancellationToken).ConfigureAwait(false)
                    : await DownloadMirrorAsync(client, mirror, path, cancellationToken).ConfigureAwait(false);
            }

            var noaa = new NoaaTideStationClient(http);
            return settings.Check
                ? await CheckNoaaAsync(noaa, path, cancellationToken).ConfigureAwait(false)
                : await DownloadNoaaAsync(noaa, path, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine(mirror is null
                ? $"Error: NOAA's metadata API didn't answer: {ex.Message}"
                : $"Error: The mirror at {mirror} didn't answer: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (mirror is not null && ex is InvalidDataException or NotSupportedException or FormatException)
        {
            Console.Error.WriteLine($"Error: The mirror's catalogue can't be used: {ex.Message}");
            return 1;
        }
    }

    /// <summary>The mirror manifest a <c>--from</c> value names; <c>null</c> for NOAA.</summary>
    /// <returns><c>false</c> when the value is neither NOAA, the mirror nor an http(s) URL.</returns>
    internal static bool TryGetMirrorAddress(string? from, out Uri? address)
    {
        address = null;
        var value = from?.Trim() ?? string.Empty;
        if (value.Length == 0 || string.Equals(value, FromNoaa, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(value, FromMirror, StringComparison.OrdinalIgnoreCase))
        {
            address = TideCatalogueMirrorClientOptions.DefaultManifestAddress;
            return true;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            address = uri;
            return true;
        }

        return false;
    }

    private static async Task<int> DownloadNoaaAsync(NoaaTideStationClient client, string path, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"Downloading NOAA CO-OPS tide stations into {path}. This takes about 10 minutes.");
        var lastPercent = -1;
        var progress = new SynchronousProgress(p =>
        {
            var percent = p.TotalStations == 0 ? 100 : p.CompletedStations * 100 / p.TotalStations;
            if (percent / 10 != lastPercent / 10)
            {
                lastPercent = percent;
                Console.Error.WriteLine($"  {p.CompletedStations}/{p.TotalStations} reference stations");
            }
        });
        var catalogue = await client.DownloadCatalogueAsync(progress, cancellationToken).ConfigureAwait(false);
        await TideStationCatalogueFile.SaveAsync(path, catalogue, cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine(TideCli.DescribeCatalogue(TideToolText.Catalogue(catalogue, DateTimeOffset.UtcNow)));
        Console.Error.WriteLine($"Saved {path}.");
        return 0;
    }

    private static async Task<int> DownloadMirrorAsync(TideCatalogueMirrorClient client, Uri mirror, string path, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"Downloading the tide station catalogue from {mirror} into {path}.");
        var download = await client.DownloadCatalogueAsync(path, cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine(TideCli.DescribeCatalogue(TideToolText.Catalogue(download.Catalogue, DateTimeOffset.UtcNow)));
        Console.Error.WriteLine($"The mirror last checked it against its provider {TideCli.FormatDate(download.Manifest.Checked)}.");
        Console.Error.WriteLine($"Saved {path}.");
        return 0;
    }

    private static async Task<int> CheckNoaaAsync(NoaaTideStationClient client, string path, CancellationToken cancellationToken)
    {
        if (await LoadForCheckAsync(path, cancellationToken).ConfigureAwait(false) is not { } catalogue)
            return 2;

        var check = await client.CheckForUpdatesAsync(catalogue, cancellationToken).ConfigureAwait(false);
        if (!check.HasNewerData)
        {
            Console.Out.WriteLine("NOAA has no newer data.");
            return 0;
        }

        Console.Out.WriteLine("NOAA has newer data: run 's100 tides update' to download it.");
        if (check.NewDatumEpoch is { } epoch)
            Console.Out.WriteLine($"  New tidal datum epoch: {epoch}");
        Report("Added", check.AddedStationIds);
        Report("Removed", check.RemovedStationIds);
        Report("Moved or new offsets", check.ChangedStationIds);
        Report("Datums accepted again", check.RevisedDatumStationIds);
        return 8;
    }

    private static async Task<int> CheckMirrorAsync(TideCatalogueMirrorClient client, string path, CancellationToken cancellationToken)
    {
        if (await LoadForCheckAsync(path, cancellationToken).ConfigureAwait(false) is not { } catalogue)
            return 2;

        var check = await client.CheckForUpdatesAsync(catalogue, cancellationToken).ConfigureAwait(false);
        var manifest = check.Manifest;
        var mirrored = $"{manifest.Provider ?? "unknown provider"}, retrieved {TideCli.FormatDate(manifest.Retrieved)}, checked {TideCli.FormatDate(manifest.Checked)}";
        if (!manifest.IsSupported)
        {
            Console.Out.WriteLine($"The mirror's catalogue ({mirrored}) is in a newer format than this version of s100 reads: update s100.");
            return 0;
        }

        if (!check.HasNewerData)
        {
            Console.Out.WriteLine($"The mirror has no newer data ({mirrored}).");
            return 0;
        }

        Console.Out.WriteLine($"The mirror has newer data ({mirrored}): run 's100 tides update --from {FromMirror}' to download it.");
        return 8;
    }

    private static async Task<TideStationCatalogue?> LoadForCheckAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: No tide station catalogue. {TideCli.MissingHint(path)}");
            return null;
        }

        var catalogue = await TideStationCatalogueFile.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        Console.Out.WriteLine(TideCli.DescribeCatalogue(TideToolText.Catalogue(catalogue, DateTimeOffset.UtcNow)));
        return catalogue;
    }

    private static void Report(string label, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
            return;
        var shown = string.Join(", ", ids.Take(10));
        Console.Out.WriteLine(ids.Count > 10 ? $"  {label}: {shown} and {ids.Count - 10} more" : $"  {label}: {shown}");
    }

    /// <summary>Reports progress on the thread that reports it, not a captured context.</summary>
    private sealed class SynchronousProgress(Action<NoaaDownloadProgress> report) : IProgress<NoaaDownloadProgress>
    {
        private readonly Lock _gate = new();

        public void Report(NoaaDownloadProgress value)
        {
            lock (_gate)
                report(value);
        }
    }
}
