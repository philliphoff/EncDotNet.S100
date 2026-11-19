using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Mutable;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The live sources of <c>s100 mcp serve</c> (data source design note D9,
/// slice 11c): the registry's sources, added to the headless catalog and run
/// for the session's viewport as SoundCharts runs them for its map.
/// </summary>
internal sealed class HeadlessLiveSources : ILiveSourceHost, IDisposable
{
    private readonly SourceRegistry _registry;
    private readonly HeadlessMutableCatalog _catalog;
    private readonly HeadlessS100Session _session;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Func<string, string?> _environment;
    private readonly object _gate = new();
    private readonly List<Added> _added = [];

    /// <summary>Creates the host and starts following the session's viewport.</summary>
    /// <param name="registry">The sources on offer.</param>
    /// <param name="catalog">The catalog sources are added to.</param>
    /// <param name="session">The session whose viewport drives them.</param>
    /// <param name="loggerFactory">The loggers, or <see langword="null"/>.</param>
    /// <param name="environment">Reads an environment variable; <see langword="null"/> for the process's.</param>
    public HeadlessLiveSources(
        SourceRegistry registry,
        HeadlessMutableCatalog catalog,
        HeadlessS100Session session,
        ILoggerFactory? loggerFactory = null,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(session);
        _registry = registry;
        _catalog = catalog;
        _session = session;
        _loggerFactory = loggerFactory;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _session.ViewportChanged += OnViewportChanged;
        _catalog.Changed += OnCatalogChanged;
    }

    /// <inheritdoc />
    public IReadOnlyList<DataSourceInfo> Sources
    {
        get
        {
            List<Added> added;
            lock (_gate)
                added = [.. _added];
            return [.. _registry.Registrations.Select(r => Describe(r, added))];
        }
    }

    /// <inheritdoc />
    public ToolResult<AddDataSourceResult> Add(string id, IReadOnlyDictionary<string, string>? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_registry.TryResolve(id, out var registration))
            return Invalid("id", $"no source '{id}'; list_data_sources lists them");

        var fields = SourceOptionsForm.Fields(registration);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in options ?? new Dictionary<string, string>())
        {
            var field = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                return Invalid("options", $"'{registration.DisplayName}' has no option '{name}'; it takes {Names(fields)}");
            // D7, D9: a secret never travels as an argument.
            if (field.Kind == SourceOptionKind.Secret)
                return Invalid("options", $"{field.Label} isn't accepted as an argument: set the {field.EnvironmentVariable} environment variable before starting the server");
            values[field.Name] = value;
        }

        if (SourceOptionsForm.MissingRequired(registration, values.Count == 0 ? null : values, _environment) is { } missing)
            return Invalid("id", Needs(registration, missing));

        IDatasetProcessor processor;
        try
        {
            var built = SourceOptionsForm.Build(registration, values.Count == 0 ? null : values);
            processor = registration.Create(HeadlessSources.Context(built, _loggerFactory));
        }
        catch (FormatException ex)
        {
            return Invalid("options", ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return Invalid("id", $"'{registration.DisplayName}' couldn't start: {ex.Message}");
        }

        DatasetId datasetId;
        try
        {
            datasetId = _catalog.AddProcessor(registration.Id, processor);
        }
        catch (ArgumentException ex)
        {
            (processor as IDisposable)?.Dispose();
            return Invalid("id", ex.Message);
        }

        var entry = new Added(registration, SourceOptionsForm.Build(registration, values.Count == 0 ? null : values), datasetId, processor);
        lock (_gate)
            _added.Add(entry);
        Drive(entry, _session.ViewArea);
        return ToolResult<AddDataSourceResult>.Ok(new AddDataSourceResult(datasetId.Value, StatusOf(entry)));
    }

    /// <summary>
    /// Adds the live feeds subscribed to in a Library (D9: with
    /// <c>--live-feeds</c>), with their saved options. A saved secret is left
    /// out: it comes from its environment variable here.
    /// </summary>
    /// <param name="feeds">The feeds: registration ids and saved options.</param>
    /// <param name="report">Says why a feed wasn't added.</param>
    /// <returns>How many were added.</returns>
    public int AddFeeds(IEnumerable<(string Id, IReadOnlyDictionary<string, string>? Options)> feeds, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(report);
        var count = 0;
        foreach (var (id, saved) in feeds)
        {
            var options = saved is null || !_registry.TryResolve(id, out var registration)
                ? saved
                : saved
                    .Where(o => SourceOptionsForm.Fields(registration).FirstOrDefault(f => f.Name == o.Key)?.Kind != SourceOptionKind.Secret)
                    .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);
            var result = Add(id, options);
            if (result.TryGetValue(out _))
                count++;
            else if (result.TryGetError(out var error))
                report($"Live feed '{id}' wasn't added: {error!.Message}");
        }
        return count;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session.ViewportChanged -= OnViewportChanged;
        _catalog.Changed -= OnCatalogChanged;
    }

    private void OnViewportChanged(object? sender, EventArgs e)
    {
        List<Added> added;
        lock (_gate)
            added = [.. _added];
        var area = _session.ViewArea;
        foreach (var entry in added)
            Drive(entry, area);
    }

    // A source closed with close_dataset is no longer driven.
    private void OnCatalogChanged(object? sender, DatasetCatalogChangedEventArgs e)
    {
        var present = _catalog.Datasets.Select(d => d.Id).ToHashSet();
        lock (_gate)
            _added.RemoveAll(a => !present.Contains(a.DatasetId));
    }

    // As SoundCharts' activator does: run within the source's range, for the area in view.
    private static void Drive(Added entry, EncDotNet.S100.Datasets.Pipelines.Portrayal.GeographicBounds? area)
    {
        if (entry.Processor is not IHostedDatasetProcessor hosted)
            return;
        var active = entry.Registration.ActivationFor(entry.Options).IsActiveFor(area);
        try
        {
            hosted.IsActive = active;
            if (!active)
                return;
            hosted.UpdateArea(area);
            if (!entry.Started)
            {
                entry.Started = true;
                hosted.Start();
            }
        }
        catch (ObjectDisposedException)
        {
            // Closed meanwhile.
        }
    }

    private static string StatusOf(Added entry)
    {
        if (entry.Processor is not IHostedDatasetProcessor hosted)
            return "live";
        if (!hosted.IsActive)
            return "paused";
        if (hosted.LastError is not null && hosted.LastUpdated is null)
            return "failing";
        return hosted.LastUpdated is null ? "waiting" : "live";
    }

    private DataSourceInfo Describe(SourceRegistration registration, List<Added> added) => new(
        registration.Id,
        registration.DisplayName,
        registration.Description,
        registration.Group.ToString(),
        registration.Activation.MaxViewportSpanDegrees,
        [.. SourceOptionsForm.Fields(registration).Select(f => new DataSourceOption(
            f.Name, f.Label, f.Kind.ToString().ToLowerInvariant(), f.Required, f.EnvironmentVariable, f.Description, f.Choices))],
        SourceOptionsForm.MissingRequired(registration, null, _environment) is { } missing ? Needs(registration, missing) : null,
        [.. added.Where(a => ReferenceEquals(a.Registration, registration)).Select(a => new DataSourceDataset(
            a.DatasetId.Value,
            StatusOf(a),
            (a.Processor as IHostedDatasetProcessor)?.LastUpdated,
            (a.Processor as IHostedDatasetProcessor)?.LastError?.Message))]);

    private static string Needs(SourceRegistration registration, SourceOptionField missing) =>
        missing.Kind == SourceOptionKind.Secret && missing.EnvironmentVariable is { } variable
            ? $"{registration.DisplayName} needs {missing.Label}: set the {variable} environment variable before starting the server"
            : $"{registration.DisplayName} needs the option '{missing.Name}' ({missing.Label})";

    private static string Names(IReadOnlyList<SourceOptionField> fields) =>
        fields.Count == 0 ? "no options" : string.Join(", ", fields.Select(f => f.Name));

    private static ToolResult<AddDataSourceResult> Invalid(string parameter, string reason) =>
        ToolResult<AddDataSourceResult>.Err(new InvalidArgument(parameter, reason));

    private sealed class Added(SourceRegistration registration, object? options, DatasetId datasetId, IDatasetProcessor processor)
    {
        public SourceRegistration Registration { get; } = registration;

        public object? Options { get; } = options;

        public DatasetId DatasetId { get; } = datasetId;

        public IDatasetProcessor Processor { get; } = processor;

        public bool Started { get; set; }
    }
}
