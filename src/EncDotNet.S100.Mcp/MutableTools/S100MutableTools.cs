using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Mutable;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.MutableTools;

/// <summary>
/// Builds the <b>mutating</b> S-100 MCP tool set from host capabilities,
/// independent of transport. The companion to <see cref="S100McpTools"/> (the
/// read-only set): a host that wants a stateful session appends these to the
/// read-only tools.
/// </summary>
/// <remarks>
/// <para>
/// Each capability is optional and supplied via an
/// <see cref="ICapabilityAccessor{TCapability}"/>, so a host contributes only
/// the tools it can back. Tools whose capability is present are added; the rest
/// are omitted from the returned set entirely (rather than surfaced and then
/// failing). Late attachment is handled per-invocation: when an accessor's
/// <c>Current</c> is still <see langword="null"/> at call time, the tool returns
/// a <c>host_not_ready</c> error.
/// </para>
/// <para>
/// The tools returned here are transport-agnostic <see cref="McpServerTool"/>
/// instances, shared by the Streamable-HTTP server and the stdio host, and — once
/// the desktop viewer is re-pointed at this factory — by the viewer as well.
/// </para>
/// </remarks>
public static class S100MutableTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// Creates the mutating tools backed by the supplied capabilities. A
    /// <see langword="null"/> accessor omits that capability's tools.
    /// </summary>
    /// <param name="presentation">
    /// Accessor for the presentation controller, backing <c>set_palette</c>,
    /// <c>set_display_category</c>, and <c>set_display_mode</c>. When
    /// <see langword="null"/>, none of those tools are created.
    /// </param>
    /// <param name="time">
    /// Accessor for the time controller, backing <c>set_time_step</c>. When
    /// <see langword="null"/>, that tool is not created.
    /// </param>
    /// <param name="renderer">
    /// Accessor for the image renderer, backing <c>render_to_image</c>. When
    /// <see langword="null"/>, that tool is not created.
    /// </param>
    /// <param name="catalog">
    /// The mutable dataset catalog, backing <c>open_dataset</c>,
    /// <c>close_dataset</c>, and <c>close_all_datasets</c>. Passed directly (not
    /// via an accessor) because the catalog exists for the whole session. When
    /// <see langword="null"/>, none of those tools are created.
    /// </param>
    /// <param name="viewport">
    /// Accessor for the viewport controller, backing <c>set_viewport</c>. When
    /// <see langword="null"/>, that tool is not created.
    /// </param>
    /// <returns>The mutating tools in a stable order.</returns>
    public static IReadOnlyList<McpServerTool> Create(
        ICapabilityAccessor<IPresentationController>? presentation = null,
        ICapabilityAccessor<ITimeController>? time = null,
        ICapabilityAccessor<IImageRenderer>? renderer = null,
        IMutableDatasetCatalog? catalog = null,
        ICapabilityAccessor<IViewportController>? viewport = null)
    {
        var tools = new List<McpServerTool>();

        if (catalog is not null)
        {
            tools.Add(CreateOpenDataset(new OpenDatasetTool(catalog)));
            tools.Add(CreateCloseDataset(new CloseDatasetTool(catalog)));
            tools.Add(CreateCloseAllDatasets(new CloseAllDatasetsTool(catalog)));
        }

        if (presentation is not null)
        {
            tools.Add(CreateSetPalette(new SetPaletteTool(presentation)));
            tools.Add(CreateSetDisplayCategory(new SetDisplayCategoryTool(presentation)));
            tools.Add(CreateSetDisplayMode(new SetDisplayModeTool(presentation)));
        }

        if (time is not null)
        {
            tools.Add(CreateSetTimeStep(new SetTimeStepTool(time)));
        }

        if (viewport is not null)
        {
            tools.Add(CreateSetViewport(new SetViewportTool(viewport)));
        }

        if (renderer is not null)
        {
            tools.Add(CreateRenderToImage(new RenderToImageTool(renderer)));
        }

        return tools;
    }

    private const string SetPaletteDescription =
        "Sets the map-wide colour palette (Day / Dusk / Night). Returns the palette now applied and "
        + "the previous one. MUTATING.";

    private static McpServerTool CreateSetPalette(SetPaletteTool inner) =>
        McpServerTool.Create(
            ([Description("Colour palette to apply: 'Day', 'Dusk', or 'Night' (case-insensitive).")] string palette,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(new SetPaletteRequest(palette), ct),
                    v => new JsonObject { ["palette"] = v.Palette, ["previous"] = v.Previous }),
            new McpServerToolCreateOptions
            {
                Name = SetPaletteTool.Name,
                Description = SetPaletteDescription,
                SerializerOptions = JsonOptions,
            });

    private const string SetDisplayCategoryDescription =
        "Sets the map-wide ECDIS display category (DisplayBase / Standard / OtherInformation / All). "
        + "Returns the category now applied and the previous one. MUTATING.";

    private static McpServerTool CreateSetDisplayCategory(SetDisplayCategoryTool inner) =>
        McpServerTool.Create(
            ([Description("ECDIS display category: 'DisplayBase', 'Standard', 'OtherInformation', or 'All' (case-insensitive).")] string displayCategory,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(new SetDisplayCategoryRequest(displayCategory), ct),
                    v => new JsonObject { ["displayCategory"] = v.DisplayCategory, ["previous"] = v.Previous }),
            new McpServerToolCreateOptions
            {
                Name = SetDisplayCategoryTool.Name,
                Description = SetDisplayCategoryDescription,
                SerializerOptions = JsonOptions,
            });

    private const string SetDisplayModeDescription =
        "Sets an explicit per-spec display mode (S-100 Part 9 11.7). Only S-411 sea ice declares "
        + "selectable modes today: 'ice-concentration' (default), 'ice-sod', or the provisional "
        + "'ice-navigational'. 'spec' defaults to 'S-411'. Returns the mode now applied, the previous "
        + "mode, and whether it is provisional. MUTATING.";

    private static McpServerTool CreateSetDisplayMode(SetDisplayModeTool inner) =>
        McpServerTool.Create(
            ([Description("Display mode token: 'ice-concentration', 'ice-sod', or 'ice-navigational' (or the bare 'concentration'/'sod'/'navigational' aliases), or a raw S-411 mode id.")] string mode,
             [Description("Product spec whose display mode is set; defaults to 'S-411'.")] string? spec = null,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(new SetDisplayModeRequest(mode, spec), ct),
                    v => new JsonObject
                    {
                        ["spec"] = v.Spec,
                        ["mode"] = v.Mode,
                        ["previous"] = v.Previous,
                        ["provisional"] = v.Provisional,
                    }),
            new McpServerToolCreateOptions
            {
                Name = SetDisplayModeTool.Name,
                Description = SetDisplayModeDescription,
                SerializerOptions = JsonOptions,
            });

    private const string SetTimeStepDescription =
        "Sets the map clock over time-aware products (S-104 / S-111 / S-411) to a specific step. "
        + "Supply either 'index' (0-based into the available steps) or 'timestamp' (ISO-8601, snapped "
        + "to the nearest step), not both. Returns the step now applied, its index, the step count, "
        + "and the previous timestamp. MUTATING.";

    private static McpServerTool CreateSetTimeStep(SetTimeStepTool inner) =>
        McpServerTool.Create(
            ([Description("0-based index into the available time steps. Mutually exclusive with 'timestamp'.")] int? index = null,
             [Description("ISO-8601 timestamp, snapped to the nearest available step. Mutually exclusive with 'index'.")] string? timestamp = null,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(new SetTimeStepRequest(index, timestamp), ct),
                    v => new JsonObject
                    {
                        ["mode"] = v.Mode,
                        ["index"] = v.Index,
                        ["timestamp"] = v.Timestamp,
                        ["sampleCount"] = v.SampleCount,
                        ["previous"] = v.Previous,
                    }),
            new McpServerToolCreateOptions
            {
                Name = SetTimeStepTool.Name,
                Description = SetTimeStepDescription,
                SerializerOptions = JsonOptions,
            });

    private const string SetViewportDescription =
        "Pins the geographic viewport the session renders. Supply EITHER a centre + scale "
        + "('centerLongitude'/'centerLatitude'/'scaleDenominator', decimal degrees WGS-84 and a positive "
        + "scale denominator) OR a WGS-84 bounding box "
        + "('minLongitude'/'minLatitude'/'maxLongitude'/'maxLatitude'), not both. The viewport is stored "
        + "geographically and re-fit to each render's pixel size. Rotation is north-up only "
        + "('rotationDegrees' must be 0 or omitted). Latitudes must be within the Web Mercator limit "
        + "(±85.05112878°). Returns the applied viewport and the previous one. MUTATING.";

    private static McpServerTool CreateSetViewport(SetViewportTool inner) =>
        McpServerTool.Create(
            ([Description("Centre longitude in decimal degrees, WGS-84. Pair with centerLatitude and scaleDenominator; mutually exclusive with the bounding-box form.")] double? centerLongitude = null,
             [Description("Centre latitude in decimal degrees, WGS-84. Pair with centerLongitude and scaleDenominator; mutually exclusive with the bounding-box form.")] double? centerLatitude = null,
             [Description("Map scale denominator (e.g. 50000 for 1:50000); positive. Pair with centerLongitude/centerLatitude; mutually exclusive with the bounding-box form.")] double? scaleDenominator = null,
             [Description("Clockwise rotation in degrees; north-up only, so must be 0 or omitted. Applies to the centre+scale form.")] double? rotationDegrees = null,
             [Description("Bounding-box west edge (min longitude), decimal degrees WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? minLongitude = null,
             [Description("Bounding-box south edge (min latitude), decimal degrees WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? minLatitude = null,
             [Description("Bounding-box east edge (max longitude), decimal degrees WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? maxLongitude = null,
             [Description("Bounding-box north edge (max latitude), decimal degrees WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? maxLatitude = null,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(
                        new SetViewportRequest(
                            centerLongitude, centerLatitude, scaleDenominator, rotationDegrees,
                            minLongitude, minLatitude, maxLongitude, maxLatitude),
                        ct),
                    v => new JsonObject
                    {
                        ["mode"] = v.Mode,
                        ["centerLongitude"] = v.CenterLongitude,
                        ["centerLatitude"] = v.CenterLatitude,
                        ["scaleDenominator"] = v.ScaleDenominator,
                        ["rotationDegrees"] = v.RotationDegrees,
                        ["previous"] = v.Previous,
                    }),
            new McpServerToolCreateOptions
            {
                Name = SetViewportTool.Name,
                Description = SetViewportDescription,
                SerializerOptions = JsonOptions,
            });

    private const string OpenDatasetDescription =
        "Loads a dataset file or exchange set into the session's catalog so it becomes queryable and "
        + "renderable. 'path' is a local file (S-101 .000, HDF5 .h5, GML, etc.) OR an exchange set (a "
        + "folder containing a catalogue, or a .zip of one); the kind is auto-detected. 'spec' "
        + "optionally forces a product-spec hint for single-file loads. Returns the resulting catalog "
        + "id(s), spec, and bounding box. MUTATING.";

    private static McpServerTool CreateOpenDataset(OpenDatasetTool inner) =>
        McpServerTool.Create(
            ([Description("Local filesystem path to a dataset file or an exchange set (folder containing a catalogue, or a .zip of one).")] string path,
             [Description("Optional explicit product-spec hint (e.g. \"S-102\") for single-file loads; ignored for exchange sets.")] string? spec = null,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(new OpenDatasetRequest(path, spec), ct),
                    v =>
                    {
                        var datasets = new JsonArray();
                        foreach (var d in v.Datasets)
                        {
                            datasets.Add(new JsonObject
                            {
                                ["id"] = d.Id,
                                ["spec"] = d.Spec,
                                ["southLatitude"] = d.SouthLatitude,
                                ["westLongitude"] = d.WestLongitude,
                                ["northLatitude"] = d.NorthLatitude,
                                ["eastLongitude"] = d.EastLongitude,
                            });
                        }
                        return new JsonObject
                        {
                            ["path"] = v.Path,
                            ["kind"] = v.Kind,
                            ["count"] = v.Count,
                            ["loadDurationMs"] = v.LoadDurationMs,
                            ["timedOut"] = v.TimedOut,
                            ["datasets"] = datasets,
                        };
                    }),
            new McpServerToolCreateOptions
            {
                Name = OpenDatasetTool.Name,
                Description = OpenDatasetDescription,
                SerializerOptions = JsonOptions,
            });

    private const string CloseDatasetDescription =
        "Unloads a currently-loaded dataset by its catalog id. An unknown or already-removed id "
        + "resolves gracefully as removed:false. Returns the removed dataset metadata. MUTATING.";

    private static McpServerTool CreateCloseDataset(CloseDatasetTool inner) =>
        McpServerTool.Create(
            ([Description("Catalog id of the dataset to unload.")] string id,
             CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(new CloseDatasetRequest(id), ct),
                    v => new JsonObject
                    {
                        ["id"] = v.Id,
                        ["removed"] = v.Removed,
                        ["count"] = v.Count,
                        ["removedDatasets"] = RemovedDatasetsJson(v.RemovedDatasets),
                    }),
            new McpServerToolCreateOptions
            {
                Name = CloseDatasetTool.Name,
                Description = CloseDatasetDescription,
                SerializerOptions = JsonOptions,
            });

    private const string CloseAllDatasetsDescription =
        "Unloads every currently-loaded dataset. Returns the removed dataset metadata. MUTATING.";

    private static McpServerTool CreateCloseAllDatasets(CloseAllDatasetsTool inner) =>
        McpServerTool.Create(
            (CancellationToken ct = default) =>
                DispatchAsync(
                    () => inner.InvokeAsync(ct),
                    v => new JsonObject
                    {
                        ["removed"] = v.Removed,
                        ["count"] = v.Count,
                        ["removedDatasets"] = RemovedDatasetsJson(v.RemovedDatasets),
                    }),
            new McpServerToolCreateOptions
            {
                Name = CloseAllDatasetsTool.Name,
                Description = CloseAllDatasetsDescription,
                SerializerOptions = JsonOptions,
            });

    private static JsonArray RemovedDatasetsJson(IReadOnlyList<RemovedDataset> removed)
    {
        var array = new JsonArray();
        foreach (var d in removed)
        {
            array.Add(new JsonObject { ["id"] = d.Id, ["spec"] = d.Spec });
        }
        return array;
    }

    private const string RenderToImageDescription =
        "Renders the current session state (loaded datasets, palette, time step, viewport) to a PNG "
        + "and returns it as an MCP ImageContentBlock alongside a JSON metadata block. Primary use "
        + "case: headless visual validation — open datasets, set a palette / time step, then render "
        + "and inspect the image. When both width and height are omitted the capture defaults to the "
        + "renderer's live viewport size (when it has one) so it matches the on-screen view instead of "
        + "letterboxing, and that size is echoed in the metadata as 'viewportWidth'/'viewportHeight' for "
        + "aspect-matching or pixel picks; a headless renderer has none and defaults to 1024x768. "
        + "Side-effect free. MUTATING session, but this call mutates nothing.";

    private static McpServerTool CreateRenderToImage(RenderToImageTool inner) =>
        McpServerTool.Create(
            ([Description("Output image width in pixels. When both width and height are omitted, defaults to the renderer's live viewport width if it has one (echoed as viewportWidth), otherwise 1024. Clamped to [64, 4096].")] int? width = null,
             [Description("Output image height in pixels. When both width and height are omitted, defaults to the renderer's live viewport height if it has one (echoed as viewportHeight), otherwise 768. Clamped to [64, 4096].")] int? height = null,
             [Description("Display pixel-density multiplier (1.0 = device-independent pixels; 2.0 = HiDPI). Null defaults to 1.0. Clamped to [0.5, 3.0].")] double? pixelDensity = null,
             CancellationToken ct = default) =>
                DispatchRenderAsync(() => inner.InvokeAsync(
                    new RenderToImageRequest(width, height, pixelDensity), ct)),
            new McpServerToolCreateOptions
            {
                Name = RenderToImageTool.Name,
                Description = RenderToImageDescription,
                SerializerOptions = JsonOptions,
            });

    /// <summary>
    /// Render-specific dispatch: a success surfaces the PNG as a first-class
    /// <see cref="ImageContentBlock"/> followed by a JSON metadata block; errors
    /// use the shared error payload.
    /// </summary>
    private static async Task<CallToolResult> DispatchRenderAsync(
        Func<Task<ToolResult<RenderToImageResult>>> invoke)
    {
        try
        {
            var result = await invoke().ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                var metadata = new JsonObject
                {
                    ["width"] = value!.Width,
                    ["height"] = value.Height,
                    ["pixelDensity"] = value.PixelDensity,
                    ["imageFormat"] = value.ImageFormat,
                    ["byteLength"] = value.ImageBytes.Length,
                };
                if (value.Notes is not null)
                {
                    metadata["notes"] = value.Notes;
                }
                if (value.ViewportWidth is { } vw)
                {
                    metadata["viewportWidth"] = vw;
                }
                if (value.ViewportHeight is { } vh)
                {
                    metadata["viewportHeight"] = vh;
                }

                return new CallToolResult
                {
                    Content =
                    [
                        ImageContentBlock.FromBytes(value.ImageBytes, "image/png"),
                        new TextContentBlock { Text = metadata.ToJsonString(JsonOptions) },
                    ],
                    IsError = false,
                };
            }

            result.TryGetError(out var error);
            return ToolErrorPayload.AsCallToolResult(error!, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolErrorPayload.InternalError(ex, JsonOptions);
        }
    }

    /// <summary>
    /// Runs a tool invocation and translates its <see cref="ToolResult{T}"/> into
    /// an MCP <see cref="CallToolResult"/> — success projected to JSON via
    /// <paramref name="success"/>, typed errors and unexpected exceptions to the
    /// shared error payload.
    /// </summary>
    private static async Task<CallToolResult> DispatchAsync<TResult>(
        Func<Task<ToolResult<TResult>>> invoke,
        Func<TResult, JsonObject> success)
    {
        try
        {
            var result = await invoke().ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = success(value!).ToJsonString(JsonOptions) }],
                    IsError = false,
                };
            }

            result.TryGetError(out var error);
            return ToolErrorPayload.AsCallToolResult(error!, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolErrorPayload.InternalError(ex, JsonOptions);
        }
    }
}
