using System.ComponentModel;
using EncDotNet.S100.Collections.Net;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>
/// Builds and runs the processors of dataset entries that are sources rather
/// than files (design note D3, D4): it creates each processor from its
/// <see cref="SourceRegistration"/>, runs it through a
/// <see cref="HostedSourceActivator"/> while it's loaded, and keeps the list
/// of added sources in <see cref="ViewerSettings.SourceDatasets"/>.
/// </summary>
internal sealed class SourceDatasetService : IDisposable
{
    private readonly SourceRegistry _registry;
    private readonly ViewerSettings _settings;
    private readonly IMapViewportNotifier? _notifier;
    private readonly Secrets.ISecretStore? _secrets;
    private readonly ISourceHostExtension[] _extensions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly Dictionary<MapDatasetId, Running> _running = [];

    /// <summary>Creates the service.</summary>
    /// <param name="registry">The sources on offer.</param>
    /// <param name="settings">Where the added sources are saved.</param>
    /// <param name="notifier">The map's view, for activation and the area in view.</param>
    /// <param name="loggerFactory">The loggers.</param>
    public SourceDatasetService(
        SourceRegistry registry,
        ViewerSettings settings,
        IMapViewportNotifier? notifier = null,
        ILoggerFactory? loggerFactory = null,
        Secrets.ISecretStore? secrets = null,
        IEnumerable<ISourceHostExtension>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        _registry = registry;
        _settings = settings;
        _notifier = notifier;
        _secrets = secrets;
        _extensions = extensions?.ToArray() ?? [];
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<SourceDatasetService>();
    }

    /// <summary>The sources a user can add, in registration order.</summary>
    public IReadOnlyList<SourceRegistration> Registrations => _registry.Registrations;

    /// <summary>Finds a source by id.</summary>
    /// <param name="sourceId">The source's id.</param>
    public SourceRegistration? Find(string sourceId) =>
        _registry.TryResolve(sourceId, out var registration) ? registration : null;

    /// <summary>
    /// The saved sources to add again at start-up, for the sources still
    /// registered, in the order they were added.
    /// </summary>
    public IReadOnlyList<SourceRegistration> SavedRegistrations() =>
        [.. _settings.SourceDatasets
            .Select(Find)
            .OfType<SourceRegistration>()
            .DistinctBy(r => r.Id, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Saves that the source is added, so it's added again at the next start.</summary>
    /// <param name="sourceId">The source's id.</param>
    /// <returns><see langword="true"/> when the list changed.</returns>
    public bool Remember(string sourceId)
    {
        if (_settings.SourceDatasets.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
            return false;
        _settings.SourceDatasets.Add(sourceId);
        _settings.Save();
        return true;
    }

    /// <summary>Saves that the source was removed.</summary>
    /// <param name="sourceId">The source's id.</param>
    /// <returns><see langword="true"/> when the list changed.</returns>
    public bool Forget(string sourceId)
    {
        if (_settings.SourceDatasets.RemoveAll(id => string.Equals(id, sourceId, StringComparison.OrdinalIgnoreCase)) == 0)
            return false;
        _settings.Save();
        return true;
    }

    /// <summary>Builds the processor of a source entry. Runs off the UI thread.</summary>
    /// <param name="sourceId">The entry's <see cref="DatasetEntry.SourceId"/>.</param>
    /// <param name="options">The entry's <see cref="DatasetEntry.SourceOptions"/>.</param>
    /// <exception cref="NotSupportedException">The source isn't registered.</exception>
    public IDatasetProcessor CreateProcessor(string sourceId, object? options) =>
        _registry.Create(sourceId, new SourceCreationContext
        {
            Options = options,
            LoggerFactory = _loggerFactory,
            CreateHttpClient = CreateHttpClient,
            ReadSecret = _secrets is { IsAvailable: true } store ? store.Read : null,
        });

    /// <summary>
    /// Starts running a loaded source entry's processor, following the
    /// entry's visibility and the view, and attaches the
    /// <see cref="ISourceHostExtension"/>s for its source.
    /// </summary>
    /// <param name="entry">The loaded source entry.</param>
    /// <param name="processor">Its registered processor.</param>
    public void Attach(DatasetEntry entry, IDatasetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(processor);
        if (entry.SourceId is not { } sourceId)
            return;
        Detach(entry.Id);

        var attached = new List<IDisposable>();
        foreach (var extension in _extensions)
        {
            if (!string.Equals(extension.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (extension.Attach(entry, processor) is { } attachment)
                    attached.Add(attachment);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Attaching {Extension} to the {Source} source failed.", extension.GetType().Name, sourceId);
            }
        }

        HostedSourceActivator? activator = null;
        PropertyChangedEventHandler? onChanged = null;
        if (processor is IHostedDatasetProcessor hosted)
        {
            var activation = Find(sourceId)?.Activation ?? SourceActivation.Always;
            var running = activator = new HostedSourceActivator(
                hosted, activation, _notifier, entry.IsVisible, logger: _logger);
            onChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(DatasetEntry.IsVisible))
                    running.SetVisible(entry.IsVisible);
            };
            entry.PropertyChanged += onChanged;
        }
        lock (_lock)
            _running[entry.Id] = new Running(entry, activator, onChanged, attached);
    }

    /// <summary>Whether the source entry's processor is running now.</summary>
    /// <param name="id">The entry's id.</param>
    public bool IsRunning(MapDatasetId id)
    {
        lock (_lock)
            return _running.TryGetValue(id, out var running) && running.Activator?.IsRunning == true;
    }

    /// <summary>Stops following a source entry; its processor is disposed by its owner.</summary>
    /// <param name="id">The entry's id.</param>
    public void Detach(MapDatasetId id)
    {
        Running? running;
        lock (_lock)
        {
            if (!_running.Remove(id, out running))
                return;
        }
        if (running.OnChanged is not null)
            running.Entry.PropertyChanged -= running.OnChanged;
        running.Activator?.Dispose();
        foreach (var attachment in running.Attached)
            attachment.Dispose();
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        // Feeds such as NDBC ask automated clients to identify themselves.
        http.DefaultRequestHeaders.UserAgent.TryParseAdd(App.WeatherUserAgent);
        return http;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        MapDatasetId[] ids;
        lock (_lock)
            ids = [.. _running.Keys];
        foreach (var id in ids)
            Detach(id);
    }

    private sealed record Running(
        DatasetEntry Entry,
        HostedSourceActivator? Activator,
        PropertyChangedEventHandler? OnChanged,
        IReadOnlyList<IDisposable> Attached);
}
