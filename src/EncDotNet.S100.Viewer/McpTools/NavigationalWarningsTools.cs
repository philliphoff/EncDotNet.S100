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
    [property: Description("Number of in-force parts whose warnings are new since their source's latest refresh (the summary's \"N new\" and the Warnings rail badge).")] int New,
    [property: Description("True when the panel's Refresh can read a source again (a listed warning came from a Library SECOM service or online feed).")] bool CanRefresh,
    [property: Description("The Library sources the listed warnings came from, by name; files opened directly have none.")] IReadOnlyList<NavigationalWarningSourceDto> Sources,
    [property: Description("Every part, in dataset order (the panel groups them by series and source).")] IReadOnlyList<NavigationalWarningDto> Warnings);

/// <summary>A Library source of warnings.</summary>
[Description("A Library source (SECOM service or online feed) that supplied loaded warnings, and how fresh they are.")]
internal sealed record NavigationalWarningSourceDto(
    [property: Description("The Library source id.")] Guid Id,
    [property: Description("The source's name, e.g. \"AMSA\".")] string Name,
    [property: Description("When the source last answered (UTC): the warnings shown are the ones received then. Null when unknown.")] DateTimeOffset? ReceivedAt,
    [property: Description("True when the source's latest refresh failed; the panel shows \"<name> hasn't answered since <time>\" with Retry.")] bool Stale,
    [property: Description("True while the source is being read again.")] bool Refreshing,
    [property: Description("The group header's source text, e.g. \"AMSA · 07:40\" or \"AMSA · 07:40, stale\", in the Local/UTC preference.")] string Header);

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
    [property: Description("True when this part is the one open from the panel.")] bool Selected,
    [property: Description("True when the warning is new: it first appeared in its source's latest refresh and its card hasn't been opened since. Always false for a file opened directly.")] bool New,
    [property: Description("The Library source id the part came from, or null for a file opened directly.")] Guid? SourceId);

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

/// <summary>The Warning cards in Object Information.</summary>
[Description("The Warning cards Object Information shows for the current pick: one lone card, or one per warning part when a pick hits several, in display order.")]
internal sealed record WarningCardsDto(
    [property: Description("Every card, in display order (points before lines before areas, then by number). Empty when no warning is open.")] IReadOnlyList<WarningCardDto> Cards,
    [property: Description("True when several cards replace the hit list (only one is open at a time).")] bool IsList,
    [property: Description("The other features of the pick, listed as rows below the cards, by their row label.")] IReadOnlyList<string> OtherHits);

/// <summary>One Warning card.</summary>
[Description("One Warning card.")]
internal sealed record WarningCardDto(
    [property: Description("Catalog id of the dataset that holds the part.")] string? DatasetId,
    [property: Description("The part's feature id.")] string? FeatureId,
    [property: Description("Series and number, e.g. \"NAVAREA X 110/26\" (the file name without a number).")] string Label,
    [property: Description("The warning type shown in the header, or null.")] string? Type,
    [property: Description("Status at the view time: \"in_force\", \"not_yet_in_force\" or \"ended\".")] string Status,
    [property: Description("True when the card's body shows.")] bool Open,
    [property: Description("True when the card is one of several, so its header opens it.")] bool Collapsible,
    [property: Description("True while the card's part is previewed on the map (hovered).")] bool Previewed,
    [property: Description("True when the card offers Zoom to (the part has a location).")] bool CanZoom,
    [property: Description("True when the header shows NEW: the warning was new when the card was made (opening it marks it seen).")] bool New,
    [property: Description("True when the footer shows NOT FOR NAVIGATION: the dataset's exchange-set metadata marks it not for navigation.")] bool NotForNavigation,
    [property: Description("Warning numbers in the displayed text that link to another listed warning, in text order.")] IReadOnlyList<WarningLinkDto> Links);

/// <summary>A link in a card's text.</summary>
[Description("A warning number in a card's message that opens another listed warning.")]
internal sealed record WarningLinkDto(
    [property: Description("The number as the text writes it, e.g. \"106/26\"; pass it to follow_warning_link.")] string Text,
    [property: Description("Catalog id of the dataset that holds the linked part.")] string DatasetId,
    [property: Description("The linked part's feature id.")] string FeatureId,
    [property: Description("The linked warning's series and number, or null.")] string? Label);

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
            panel.NewCount,
            panel.CanRefresh,
            service.Sources.Select(s => new NavigationalWarningSourceDto(
                s.Id, s.Name, s.ReceivedAt, s.IsStale, s.IsRefreshing, panel.SourceHeader(s))).ToList(),
            service.Warnings.Select(w => ToDto(w, time, selected, service.IsNew(w))).ToList());
    }

    private static NavigationalWarningDto ToDto(NavigationalWarningEntry entry, DateTimeOffset time, NavigationalWarningEntry? selected, bool isNew)
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
                && string.Equals(selected.FeatureRef, entry.FeatureRef, StringComparison.Ordinal),
            isNew,
            entry.Source?.Id);
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

/// <summary>Reads the warnings' sources again, as the panel's Refresh (or a stale group's Retry) does.</summary>
internal sealed class RefreshNavigationalWarningsTool : NavigationalWarningsToolBase
{
    public const string Name = "refresh_navigational_warnings";

    public RefreshNavigationalWarningsTool(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    public Task<ToolResult<NavigationalWarningsDto>> InvokeAsync(Guid? sourceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() =>
        {
            if (!Panel.CanRefresh)
                return ToolResult<NavigationalWarningsDto>.Err(new InvalidArgument("sourceId", "no listed warning comes from a Library SECOM service or online feed; files opened directly don't refresh"));
            if (sourceId is { } id)
            {
                if (Service.Sources.All(s => s.Id != id))
                    return ToolResult<NavigationalWarningsDto>.Err(new InvalidArgument("sourceId", $"no listed warning comes from source '{id}'; read the sources with list_navigational_warnings"));
                Service.RefreshSources([id]);
            }
            else
            {
                Panel.RefreshSources();
            }

            return ToolResult<NavigationalWarningsDto>.Ok(ToDto(Panel, Service));
        });
    }
}

/// <summary>Shared plumbing for the Warning card tools.</summary>
internal abstract class WarningCardsToolBase : NavigationalWarningsToolBase
{
    protected WarningCardsToolBase(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    internal static WarningCardsDto ToDto(PickReportViewModel? pick)
    {
        if (pick is null)
            return new WarningCardsDto([], false, []);
        IReadOnlyList<NavigationalWarningCardViewModel> cards = pick.HasWarningCardList
            ? pick.WarningCards
            : pick.WarningCard is { } lone ? [lone] : [];
        return new WarningCardsDto(
            cards.Select(c => ToDto(c, pick)).ToList(),
            pick.HasWarningCardList,
            pick.OtherHits.Select(h => h.HitListLabel).ToList());
    }

    private static WarningCardDto ToDto(NavigationalWarningCardViewModel card, PickReportViewModel pick) => new(
        card.DatasetId,
        card.Hit?.FeatureRef,
        card.Label,
        card.TypeText,
        card.Status switch
        {
            NavigationalWarningStatus.InForce => "in_force",
            NavigationalWarningStatus.NotYetInForce => "not_yet_in_force",
            _ => "ended",
        },
        card.IsOpen,
        card.IsCollapsible,
        card.Hit is not null && ReferenceEquals(pick.PreviewHit, card.Hit),
        card.CanZoom,
        card.IsNew,
        card.NotForNavigation,
        card.Links.Select(l => new WarningLinkDto(l.Text, l.Target!.DatasetId, l.Target.FeatureRef, l.Target.Warning.Label)).ToList());

    /// <summary>The card for a part, or the open card when no part is named.</summary>
    protected static NavigationalWarningCardViewModel? FindCard(PickReportViewModel pick, string? datasetId, string? featureId)
    {
        IEnumerable<NavigationalWarningCardViewModel> cards = pick.HasWarningCardList
            ? pick.WarningCards
            : pick.WarningCard is { } lone ? [lone] : [];
        return string.IsNullOrWhiteSpace(featureId)
            ? cards.FirstOrDefault(c => c.IsOpen)
            : cards.FirstOrDefault(c =>
                string.Equals(c.Hit?.FeatureRef, featureId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(datasetId) || string.Equals(c.DatasetId, datasetId, StringComparison.Ordinal)));
    }
}

/// <summary>Lists the Warning cards in Object Information.</summary>
internal sealed class GetWarningCardsTool : WarningCardsToolBase
{
    public const string Name = "get_warning_cards";

    public GetWarningCardsTool(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    public Task<ToolResult<WarningCardsDto>> InvokeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() => ToolResult<WarningCardsDto>.Ok(ToDto(Panel.PickReport)));
    }
}

/// <summary>Follows a warning-number link in a card's text, as clicking it does.</summary>
internal sealed class FollowWarningLinkTool : WarningCardsToolBase
{
    public const string Name = "follow_warning_link";

    public FollowWarningLinkTool(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    public Task<ToolResult<WarningCardsDto>> InvokeAsync(
        string text, int? occurrence = null, string? datasetId = null, string? featureId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(text))
                return ToolResult<WarningCardsDto>.Err(new InvalidArgument("text", "required; read the links with get_warning_cards"));
            if (Panel.PickReport is not { } pick || FindCard(pick, datasetId, featureId) is not { } card)
                return ToolResult<WarningCardsDto>.Err(new InvalidArgument("featureId", "no Warning card is open; open one with select_navigational_warning or pick_features"));
            var matches = card.Links.Where(l => string.Equals(l.Text, text.Trim(), StringComparison.Ordinal)).ToList();
            var n = occurrence ?? 1;
            if (n < 1 || n > matches.Count)
            {
                return ToolResult<WarningCardsDto>.Err(new InvalidArgument(
                    "text", $"the card '{card.Label}' has {matches.Count} link(s) '{text}'; read the links with get_warning_cards"));
            }

            card.OpenLinkCommand.Execute(matches[n - 1].Target);
            return ToolResult<WarningCardsDto>.Ok(ToDto(Panel.PickReport));
        });
    }
}

/// <summary>Previews a card's part on the map, as hovering the card does.</summary>
internal sealed class PreviewWarningCardTool : WarningCardsToolBase
{
    public const string Name = "preview_warning_card";

    public PreviewWarningCardTool(NavigationalWarningsViewModel panel, NavigationalWarningsService service, IRouteEditInvoker invoker)
        : base(panel, service, invoker) { }

    public Task<ToolResult<WarningCardsDto>> InvokeAsync(string? datasetId = null, string? featureId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() =>
        {
            if (Panel.PickReport is not { } pick)
                return ToolResult<WarningCardsDto>.Ok(ToDto(null));
            if (string.IsNullOrWhiteSpace(featureId))
            {
                pick.SetWarningPreview(null);
                return ToolResult<WarningCardsDto>.Ok(ToDto(pick));
            }

            if (!pick.HasWarningCardList || FindCard(pick, datasetId, featureId) is not { } card)
            {
                return ToolResult<WarningCardsDto>.Err(new InvalidArgument(
                    "featureId", $"no Warning card for '{featureId}' in a pick of several warnings; read the cards with get_warning_cards"));
            }

            pick.SetWarningPreview(card);
            return ToolResult<WarningCardsDto>.Ok(ToDto(pick));
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
                "languages. Counts in force, not in force, text-only and new match the panel's summary. Each part says " +
                "whether it is new (it first appeared in its source's latest refresh and its card hasn't been opened) " +
                "and which Library source it came from; sources say when they last answered and whether they are stale " +
                "(their latest refresh failed). Files opened directly have no source and are never new.",
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

    public static McpServerTool Create(RefreshNavigationalWarningsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The Library source to read again (from list_navigational_warnings' sources), as a stale group's Retry does; omit for every source, as the panel's Refresh does.")] Guid? sourceId = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(sourceId, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = RefreshNavigationalWarningsTool.Name,
            Description =
                "Reads the SECOM services and online feeds that supplied the loaded navigational warnings again, as the " +
                "Navigational warnings panel's Refresh does (or one source, as a stale group's Retry does), through the " +
                "Library's refresh and sync. Returns at once with what list_navigational_warnings returns; the sources " +
                "show refreshing until the read ends. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(GetWarningCardsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetWarningCardsTool.Name,
            Description =
                "Lists the Warning cards Object Information shows: one card for a lone navigational warning, or one per " +
                "warning part when a pick hits several (points before lines before areas, then by number; only one open " +
                "at a time; other features follow as rows). Each card has its dataset and feature id, label, type, status " +
                "at the view time, whether it is open, collapsible, previewed on the map and can Zoom to, and the warning " +
                "numbers in its text that link to other listed warnings.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(FollowWarningLinkTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The link's text, e.g. \"106/26\" (from get_warning_cards).")] string text,
            [Description("Which link with that text, 1-based (default 1).")] int? occurrence = null,
            [Description("Dataset of the card whose text holds the link; omit for the open card.")] string? datasetId = null,
            [Description("Feature id of the card whose text holds the link; omit for the open card.")] string? featureId = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(text, occurrence, datasetId, featureId, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = FollowWarningLinkTool.Name,
            Description =
                "Follows a warning-number link in a Warning card's message, as clicking it does: the linked warning is " +
                "selected in the Navigational warnings panel and its card opens in Object Information (framed on the map " +
                "when it has a location and is in force). Returns what get_warning_cards returns afterwards. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(PreviewWarningCardTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Dataset of the card to preview.")] string? datasetId = null,
            [Description("Feature id of the card to preview; omit to end the preview.")] string? featureId = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(datasetId, featureId, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = PreviewWarningCardTool.Name,
            Description =
                "Previews one of several Warning cards' parts on the map with the pick highlight, as hovering the card " +
                "does, without opening it; omit featureId to end the preview. Returns what get_warning_cards returns. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
