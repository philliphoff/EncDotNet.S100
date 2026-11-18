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
/// <see cref="HostedSourceActivator"/> while it's loaded. Which feeds are
/// added is the Library's (<see cref="LiveFeedLibrary"/>, design note D8).
/// </summary>
internal sealed class SourceDatasetService : IDisposable
{
    private readonly SourceRegistry _registry;
    private readonly IMapViewportNotifier? _notifier;
    private readonly Secrets.ISecretStore? _secrets;
    private readonly ISourceHostExtension[] _extensions;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;

    /// <summary>How often a source's status is read again, so its age stays current.</summary>
    internal static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(30);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly Dictionary<MapDatasetId, Running> _running = [];

    /// <summary>Creates the service.</summary>
    /// <param name="registry">The sources on offer.</param>
    /// <param name="notifier">The map's view, for activation and the area in view.</param>
    /// <param name="loggerFactory">The loggers.</param>
    public SourceDatasetService(
        SourceRegistry registry,
        IMapViewportNotifier? notifier = null,
        ILoggerFactory? loggerFactory = null,
        Secrets.ISecretStore? secrets = null,
        IEnumerable<ISourceHostExtension>? extensions = null,
        TimeProvider? timeProvider = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _notifier = notifier;
        _secrets = secrets;
        _extensions = extensions?.ToArray() ?? [];
        _time = timeProvider ?? TimeProvider.System;
        _post = post ?? (static action => action());
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<SourceDatasetService>();
    }

    /// <summary>The sources a user can add, in registration order.</summary>
    public IReadOnlyList<SourceRegistration> Registrations => _registry.Registrations;

    /// <summary>Finds a source by id.</summary>
    /// <param name="sourceId">The source's id.</param>
    public SourceRegistration? Find(string sourceId) =>
        _registry.TryResolve(sourceId, out var registration) ? registration : null;

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
            var registration = Find(sourceId);
            var activation = registration?.ActivationFor(entry.SourceOptions) ?? SourceActivation.Always;
            var running = activator = new HostedSourceActivator(
                hosted, activation, _notifier, entry.IsVisible, logger: _logger);
            onChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(DatasetEntry.IsVisible))
                    running.SetVisible(entry.IsVisible);
            };
            entry.PropertyChanged += onChanged;

            // D8: the row says what the source is doing. Read again when it
            // pauses or resumes, when it delivers data, and every 30 s so its
            // age and staleness stay current.
            void Refresh() => UpdateStatus(entry, running, registration, activation, hosted);
            running.StateChanged += (_, _) => Refresh();
            EventHandler<DatasetChangedEventArgs>? onData = null;
            if (processor is ILiveDatasetProcessor live)
            {
                onData = (_, _) => Refresh();
                live.Changed += onData;
                attached.Add(new Callback(() => live.Changed -= onData));
            }
            attached.Add(_time.CreateTimer(_ => Refresh(), null, StatusRefreshInterval, StatusRefreshInterval));
            Refresh();
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

    private void UpdateStatus(
        DatasetEntry entry,
        HostedSourceActivator activator,
        SourceRegistration? registration,
        SourceActivation activation,
        IHostedDatasetProcessor processor)
    {
        SourceStatus status;
        try
        {
            status = SourceStatus.Evaluate(
                activator.State, registration, processor.LastUpdated, processor.LastError, _time.GetUtcNow(), activation);
        }
        catch (ObjectDisposedException)
        {
            return; // Removed meanwhile.
        }
        _post(() => entry.SourceStatus = status);
    }

    private sealed class Callback(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
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
