using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Live sources for a one-shot command such as <c>identify</c> (data source
/// design note D9): each started for the command's area, given up to a timeout
/// for its first data, and left out with a warning when it has none, so a late
/// feed never costs the answer.
/// </summary>
internal sealed class OneShotSources : IDisposable
{
    private readonly List<IDatasetProcessor> _processors = [];

    private OneShotSources()
    {
    }

    /// <summary>The sources that delivered data in time, as catalog entries.</summary>
    public List<LoadedDataset> Datasets { get; } = [];

    /// <summary>The processors of <see cref="Datasets"/>, in the same order, for drawing.</summary>
    public List<IDatasetProcessor> Processors { get; } = [];

    /// <summary>Sources left out, and why.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Starts the sources named by <paramref name="arguments"/> for
    /// <paramref name="area"/> and waits up to <paramref name="timeout"/> for
    /// each one's first data.
    /// </summary>
    /// <param name="arguments">The <c>--source</c> arguments.</param>
    /// <param name="registry">The sources on offer.</param>
    /// <param name="area">The command's area.</param>
    /// <param name="timeout">How long to wait for a source's first data.</param>
    /// <param name="environment">Reads an environment variable; <see langword="null"/> for the process's.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The sources, or the error for a bad argument or a source that can't start (exit code 2).</returns>
    public static async Task<(OneShotSources? Sources, string? Error)> StartAsync(
        IReadOnlyList<string> arguments,
        SourceRegistry registry,
        GeographicBounds area,
        TimeSpan timeout,
        Func<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(registry);
        environment ??= Environment.GetEnvironmentVariable;
        var sources = new OneShotSources();
        var started = new List<(CreatedLiveSource Source, string Text)>();
        try
        {
            foreach (var text in arguments)
            {
                if (SourceArgument.TryParse(text, out var parseError) is not { } argument)
                    return (Fail(sources), parseError);
                if (LiveSourceFactory.TryCreate(registry, argument.Id, argument.Options, environment, null, out var problem) is not { } created)
                    return (Fail(sources), $"--source {text}: {problem!.Reason}.");
                sources._processors.Add(created.Processor);
                if (created.Processor is IHostedDatasetProcessor hosted)
                {
                    if (!created.Registration.ActivationFor(created.Options).IsActiveFor(area))
                    {
                        sources.Warnings.Add($"--source {text}: left out; the area is wider than the {created.Registration.Activation.MaxViewportSpanDegrees:0.##}° it runs in.");
                        continue;
                    }
                    hosted.IsActive = true;
                    hosted.UpdateArea(area);
                    hosted.Start();
                }
                started.Add((created, text));
            }

            var deadline = DateTimeOffset.UtcNow + timeout;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (created, text) in started)
            {
                if (!await WaitForDataAsync(created.Processor, deadline, cancellationToken).ConfigureAwait(false))
                {
                    var reason = (created.Processor as IHostedDatasetProcessor)?.LastError?.Message;
                    sources.Warnings.Add($"--source {text}: left out; no data within {timeout.TotalSeconds:0} s{(reason is null ? "" : $" ({reason})")}.");
                    continue;
                }
                var id = created.Registration.Id;
                for (var n = 2; !ids.Add(id); n++)
                    id = $"{created.Registration.Id}-{n}";
                if (LoadedDatasetProjector.Project(new DatasetId(id), created.Processor) is { } dataset)
                {
                    sources.Datasets.Add(dataset);
                    sources.Processors.Add(created.Processor);
                }
            }
            return (sources, null);
        }
        catch
        {
            sources.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var processor in _processors)
            (processor as IDisposable)?.Dispose();
        _processors.Clear();
    }

    private static OneShotSources? Fail(OneShotSources sources)
    {
        sources.Dispose();
        return null;
    }

    // A source has data once it says so, or once it has features (a source without health).
    private static async Task<bool> WaitForDataAsync(IDatasetProcessor processor, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        while (true)
        {
            if ((processor as IHostedDatasetProcessor)?.LastUpdated is not null || processor.EnumerateFeatures().Any())
                return true;
            if (DateTimeOffset.UtcNow >= deadline)
                return false;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }
}
