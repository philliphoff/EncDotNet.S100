using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>An option a live source takes (data source design note D8, D9).</summary>
/// <param name="Name">The option's name, as <c>add_data_source</c> takes it, such as <c>Input</c>.</param>
/// <param name="Label">What it is, such as "NMEA input".</param>
/// <param name="Kind">How it's written: text, secret, boolean, integer, number, duration, url or choice.</param>
/// <param name="Required">Whether the source can't start without it.</param>
/// <param name="EnvironmentVariable">For a secret, the environment variable that supplies it; a secret is never an argument.</param>
/// <param name="Description">One line on what it does, or <see langword="null"/>.</param>
/// <param name="Choices">For a choice, the values it can take.</param>
public sealed record DataSourceOption(
    [property: Description("The option's name, as add_data_source takes it, such as 'Input'.")] string Name,
    [property: Description("What it is, such as 'NMEA input'.")] string Label,
    [property: Description("How it's written: text, secret, boolean, integer, number, duration (d.hh:mm:ss), url or choice.")] string Kind,
    [property: Description("Whether the source can't start without it.")] bool Required,
    [property: Description("For a secret, the environment variable that supplies it. A secret is never passed as an argument.")] string? EnvironmentVariable,
    [property: Description("One line on what it does.")] string? Description,
    [property: Description("For a choice, the values it can take.")] IReadOnlyList<string> Choices);

/// <summary>A live source added to the catalog, and what it's doing.</summary>
/// <param name="DatasetId">Its catalog id.</param>
/// <param name="Status">What it's doing: live, waiting, paused or failing.</param>
/// <param name="LastUpdated">When it last delivered data, or <see langword="null"/> before it has.</param>
/// <param name="LastError">Its last failure, or <see langword="null"/>.</param>
public sealed record DataSourceDataset(
    [property: Description("Its catalog id, as list_datasets and the query tools take it.")] string DatasetId,
    [property: Description("What it's doing: 'live' (has data), 'waiting' (running, no data yet), 'paused' (the viewport is wider than it runs in, or there's no viewport yet) or 'failing'.")] string Status,
    [property: Description("When it last delivered data (UTC), or null before it has.")] DateTimeOffset? LastUpdated,
    [property: Description("Its last failure, or null.")] string? LastError);

/// <summary>A live source a host can add.</summary>
/// <param name="Id">Its id, such as <c>ndbc.realtime</c>.</param>
/// <param name="Name">What it's called.</param>
/// <param name="Description">What it shows.</param>
/// <param name="Group">What kind of data it shows.</param>
/// <param name="MaxViewportSpanDegrees">The widest view it runs in by default, or <see langword="null"/> for any.</param>
/// <param name="Options">The options it takes.</param>
/// <param name="Missing">What it needs before it can start (a required option or its environment variable), or <see langword="null"/>.</param>
/// <param name="Datasets">The copies of it added to the catalog.</param>
public sealed record DataSourceInfo(
    [property: Description("Its id, as add_data_source takes it, such as 'ndbc.realtime'.")] string Id,
    [property: Description("What it's called.")] string Name,
    [property: Description("What it shows.")] string? Description,
    [property: Description("What kind of data it shows: Observations, OwnShipSensors, Vessels, Predictions or Other.")] string Group,
    [property: Description("The widest view, in degrees of latitude and longitude, it runs in by default; null runs in any view. set_viewport narrower than this to start it.")] double? MaxViewportSpanDegrees,
    [property: Description("The options it takes.")] IReadOnlyList<DataSourceOption> Options,
    [property: Description("What it needs before it can start, such as 'set the ENCDOTNET_AIS_STREAM_KEY environment variable'; null when nothing.")] string? Missing,
    [property: Description("The copies of it added to the catalog, with what each is doing.")] IReadOnlyList<DataSourceDataset> Datasets);

/// <summary>Result of <see cref="ListDataSourcesTool"/>.</summary>
public sealed record ListDataSourcesResult(
    [property: Description("The live sources this host can add, with those already added.")] IReadOnlyList<DataSourceInfo> Sources);

/// <summary>Request of <see cref="AddDataSourceTool"/>.</summary>
/// <param name="Id">The source's id.</param>
/// <param name="Options">Option values by name, as text; secrets aren't accepted.</param>
public sealed record AddDataSourceRequest(
    [property: Description("The source's id, from list_data_sources.")] string Id,
    [property: Description("Option values by name, as text. Secrets aren't accepted.")] IReadOnlyDictionary<string, string>? Options = null);

/// <summary>Result of <see cref="AddDataSourceTool"/>.</summary>
public sealed record AddDataSourceResult(
    [property: Description("The source's catalog id.")] string DatasetId,
    [property: Description("What it's doing now; see list_data_sources.")] string Status);

/// <summary>
/// A host's live sources (data source design note D9): what it can add, and
/// adding one to its catalog. <c>s100 mcp serve</c> implements it.
/// </summary>
public interface ILiveSourceHost
{
    /// <summary>The sources the host can add, with those added.</summary>
    IReadOnlyList<DataSourceInfo> Sources { get; }

    /// <summary>
    /// Adds the source <paramref name="id"/> with <paramref name="options"/>,
    /// starts it, and drives it from the host's viewport.
    /// </summary>
    /// <param name="id">The source's id.</param>
    /// <param name="options">Option values by name; secrets aren't accepted.</param>
    /// <returns>The added dataset, or the argument error that stopped it.</returns>
    ToolResult<AddDataSourceResult> Add(string id, IReadOnlyDictionary<string, string>? options);
}

/// <summary>Lists the live sources a host can add (<c>list_data_sources</c>).</summary>
public sealed class ListDataSourcesTool(ILiveSourceHost host)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_data_sources";

    /// <summary>Lists them.</summary>
    public Task<ToolResult<ListDataSourcesResult>> InvokeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ToolResult<ListDataSourcesResult>.Ok(new ListDataSourcesResult(host.Sources)));
    }
}

/// <summary>Adds a live source to the catalog (<c>add_data_source</c>).</summary>
public sealed class AddDataSourceTool(ILiveSourceHost host)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "add_data_source";

    /// <summary>Adds it.</summary>
    public Task<ToolResult<AddDataSourceResult>> InvokeAsync(AddDataSourceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Id))
            return Task.FromResult(ToolResult<AddDataSourceResult>.Err(new InvalidArgument("id", "a source id is required; list_data_sources lists them")));
        return Task.FromResult(host.Add(request.Id.Trim(), request.Options));
    }
}
