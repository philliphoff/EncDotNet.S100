using System.ComponentModel;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Noaa;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tides update</c>: downloads NOAA CO-OPS's tide stations into the
/// offline catalogue the other <c>s100 tides</c> commands and
/// <c>s100 mcp serve</c> read, or with <c>--check</c> asks NOAA whether it
/// has newer data than the catalogue.
/// </summary>
internal sealed class TidesUpdateCommand : AsyncCommand<TidesUpdateCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Only ask NOAA whether it has newer data than the catalogue (a few requests); download nothing. Exits with 0 when it's current and 8 when NOAA has newer data.")]
        public bool Check { get; init; }

        [CommandOption("--catalogue <FILE>")]
        [Description("Where to keep the catalogue: a .json or .json.gz file. Defaults to the offline cache in your local application data.")]
        public string? Catalogue { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var path = TideCli.CataloguePath(settings.Catalogue);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("EncDotNet.S100.Cli");
        var client = new NoaaTideStationClient(http);

        try
        {
            if (settings.Check)
                return await CheckAsync(client, path, cancellationToken).ConfigureAwait(false);

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
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Error: NOAA's metadata API didn't answer: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CheckAsync(NoaaTideStationClient client, string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: No tide station catalogue. {TideCli.MissingHint(path)}");
            return 2;
        }

        var catalogue = await TideStationCatalogueFile.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        Console.Out.WriteLine(TideCli.DescribeCatalogue(TideToolText.Catalogue(catalogue, DateTimeOffset.UtcNow)));
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
