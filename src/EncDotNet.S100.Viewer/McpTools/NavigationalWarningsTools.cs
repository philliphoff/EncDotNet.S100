using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Projections;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The Navigational warnings panel's list (#932).</summary>
[Description("Every navigational warning part in the loaded S-124 datasets, as the Navigational warnings panel lists them, with each part's status at the panel's time.")]
internal sealed record NavigationalWarningsDto(
    [property: Description("The time status is read at: \"view_time\" (the Timeline's view time, now while Live) or \"now\".")] string Time,
    [property: Description("The instant status is read at (UTC).")] DateTimeOffset StatusTime,
    [property: Description("Number of parts in force at statusTime.")] int InForce,
    [property: Description("Number of parts not in force at statusTime (ended or not yet started).")] int NotInForce,
    [property: Description("Number of in-force parts with no location, which draw nothing on the map.")] int TextOnly,
    [property: Description("True when at least one S-124 dataset is loaded.")] bool HasSources,
    [property: Description("True while an S-124 dataset is still loading.")] bool Loading,
    [property: Description("The in-map note the panel shows for the open part, or null.")] string? MapNote,
    [property: Description("Every part, in dataset order (the panel groups them by series and source).")] IReadOnlyList<NavigationalWarningDto> Warnings);

/// <summary>One warning part.</summary>
[Description("One navigational warning part.")]
internal sealed record NavigationalWarningDto(
    [property: Description("Catalog id of the dataset that holds the part (as list_datasets).")] string DatasetId,
    [property: Description("The part's feature id within the dataset; pass it with datasetId to select_navigational_warning or describe_feature.")] string FeatureId,
    [property: Description("Series and number as mariners write them, e.g. \"NAVAREA X 106/26\", or null without a number.")] string? Label,
    [property: Description("The series, e.g. \"NAVAREA X\", or null.")] string? Series,
    [property: Description("Number and two-digit year, e.g. \"106/26\", or null.")] string? Number,
    [property: Description("The detailed warning type, e.g. \"Cable Operations\", or null.")] string? Type,
    [property: Description("The general warning type, or null.")] string? GeneralType,
    [property: Description("The general area, e.g. \"Coral Sea\", or null.")] string? Area,
    [property: Description("The locality within the area, or null.")] string? Locality,
    [property: Description("The issuing authority, shown as the group's source, or null.")] string? Issuer,
    [property: Description("When the warning was published (UTC), or null.")] DateTimeOffset? Published,
    [property: Description("Status at statusTime: \"in_force\", \"not_yet_in_force\" or \"ended\".")] string Status,
    [property: Description("Geometry: \"point\", \"curve\", \"surface\", or \"none\" for a text-only part.")] string Geometry,
    [property: Description("True when the part has no location, so nothing is drawn for it and only the list reaches it.")] bool TextOnly,
    [property: Description("The part's WGS-84 bounds, or null when it has no location.")] NavigationalWarningBoundsDto? Bounds,
    [property: Description("Validity ranges (UTC); either end may be null (open).")] IReadOnlyList<NavigationalWarningValidityDto> Validity,
    [property: Description("Message language codes in document order, e.g. [\"eng\", \"fra\"].")] IReadOnlyList<string?> Languages,
    [property: Description("True when this part is the one open from the panel.")] bool Selected);

/// <summary>A validity range.</summary>
[Description("A period when a warning applies.")]
internal sealed record NavigationalWarningValidityDto(
    [property: Description("Start (UTC), or null when open.")] DateTimeOffset? Start,
    [property: Description("End (UTC), or null when open.")] DateTimeOffset? End);

/// <summary>A part's bounds.</summary>
[Description("WGS-84 bounds in decimal degrees.")]
internal sealed record NavigationalWarningBoundsDto(
    [property: Description("Southern latitude.")] double MinLatitude,
    [property: Description("Western longitude.")] double MinLongitude,
    [property: Description("Northern latitude.")] double MaxLatitude,
    [property: Description("Eastern longitude.")] double MaxLongitude);

/// <summary>Shared plumbing for the warnings tools.</summary>
internal abstract class NavigationalWarningsToolBase
{
    protected NavigationalWarningsToolBase(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(invoker);
        Panel = panel;
        Service = service;
        Invoker = invoker;
    }

    protected NavigationalWarningsViewModel Panel { get; }

    protected NavigationalWarningsService Service { get; }

    protected IRouteEditInvoker Invoker { get; }

    internal static NavigationalWarningsDto ToDto(NavigationalWarningsViewModel panel, NavigationalWarningsService service)
    {
        var time = panel.StatusTime;
        var selected = (panel.SelectedItem as NavigationalWarningRowItem)?.Entry;
        return new NavigationalWarningsDto(
            panel.IsNowFilter ? "now" : "view_time",
            time,
            panel.InForceCount,
            panel.NotInForceCount,
            panel.TextOnlyCount,
            service.HasWarningDatasets,
            service.IsLoading,
            panel.MapNote,
            service.Warnings.Select(w => ToDto(w, time, selected)).ToList());
    }

    private static NavigationalWarningDto ToDto(NavigationalWarningEntry entry, DateTimeOffset time, NavigationalWarningEntry? selected)
    {
        var w = entry.Warning;
        NavigationalWarningBoundsDto? bounds = null;
        if (entry.Extent is { } extent)
        {
            var (minLon, minLat) = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
            var (maxLon, maxLat) = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);
            bounds = new NavigationalWarningBoundsDto(minLat, minLon, maxLat, maxLon);
        }

        return new NavigationalWarningDto(
            entry.DatasetId,
            entry.FeatureRef,
            w.Label,
            w.Series,
            w.Number,
            w.WarningType,
            w.GeneralType,
            w.GeneralArea,
            w.Locality,
            w.Issuer,
            w.PublicationTime,
            w.StatusAt(time) switch
            {
                NavigationalWarningStatus.InForce => "in_force",
                NavigationalWarningStatus.NotYetInForce => "not_yet_in_force",
                _ => "ended",
            },
            entry.Geometry switch
            {
                S100GeometryType.Point => "point",
                S100GeometryType.Curve => "curve",
                S100GeometryType.Surface => "surface",
                _ => "none",
            },
            entry.IsTextOnly,
            bounds,
            w.Validity.Select(v => new NavigationalWarningValidityDto(v.Start, v.End)).ToList(),
            w.Texts.Select(t => t.Language).ToList(),
            selected is not null
                && ReferenceEquals(selected.Dataset, entry.Dataset)
                && string.Equals(selected.FeatureRef, entry.FeatureRef, StringComparison.Ordinal));
    }
}

/// <summary>Lists the warnings, as the Navigational warnings panel does.</summary>
internal sealed class ListNavigationalWarningsTool : NavigationalWarningsToolBase
{
    public const string Name = "list_navigational_warnings";

    public ListNavigationalWarningsTool(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    public Task<ToolResult<NavigationalWarningsDto>> InvokeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() => ToolResult<NavigationalWarningsDto>.Ok(ToDto(Panel, Service)));
    }
}

/// <summary>Selects a warning in the panel, as clicking its row does.</summary>
internal sealed class SelectNavigationalWarningTool : NavigationalWarningsToolBase
{
    public const string Name = "select_navigational_warning";

    public SelectNavigationalWarningTool(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    public Task<ToolResult<NavigationalWarningsDto>> InvokeAsync(string datasetId, string featureId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(datasetId))
                return ToolResult<NavigationalWarningsDto>.Err(new InvalidArgument("datasetId", "required; read it with list_navigational_warnings"));
            if (string.IsNullOrWhiteSpace(featureId))
                return ToolResult<NavigationalWarningsDto>.Err(new InvalidArgument("featureId", "required; read it with list_navigational_warnings"));
            if (Service.Find(datasetId, featureId) is not { } entry)
            {
                return ToolResult<NavigationalWarningsDto>.Err(new InvalidArgument(
                    "featureId", $"no warning part '{featureId}' in dataset '{datasetId}'; read the ids with list_navigational_warnings"));
            }

            Panel.Select(entry);
            return ToolResult<NavigationalWarningsDto>.Ok(ToDto(Panel, Service));
        });
    }
}

/// <summary>Wraps the warnings tools as <see cref="McpServerTool"/>s.</summary>
internal static class NavigationalWarningsMcpAdapters
{
    public static McpServerTool Create(ListNavigationalWarningsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListNavigationalWarningsTool.Name,
            Description =
                "Lists every navigational warning part in the loaded S-124 datasets, as the Navigational warnings panel " +
                "does, including text-only parts that have no location and draw nothing on the map. Each part has its " +
                "dataset and feature id, label (series and number), type, area, issuer, publication time, status at the " +
                "panel's time (view time by default, or now), geometry kind and bounds, validity ranges and message " +
                "languages. Counts in force, not in force and text-only match the panel's summary.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(SelectNavigationalWarningTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Catalog id of the dataset that holds the part (from list_navigational_warnings).")] string datasetId,
            [Description("The part's feature id (from list_navigational_warnings).")] string featureId,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(datasetId, featureId, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SelectNavigationalWarningTool.Name,
            Description =
                "Selects a warning part in the Navigational warnings panel, as clicking its row does: Object Information " +
                "opens on its Warning card and, when the part has a location and is in force, the map frames and " +
                "highlights it. A part not in force leaves the map where it is; a text-only part shows the in-map note " +
                "\"… has no location, so nothing is drawn for it.\" Returns what list_navigational_warnings returns. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
