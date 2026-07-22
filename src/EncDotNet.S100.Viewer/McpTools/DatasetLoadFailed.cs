using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// A dataset open request was dispatched and reported success, yet no new
/// dataset appeared in the viewer's catalog. Returned by
/// <see cref="OpenDatasetTool"/> when the load path completed but produced
/// no portrayable dataset (e.g. an exchange set whose products are all
/// unsupported, or a file the pipeline accepted but could not surface).
/// Distinguished from <see cref="InvalidArgument"/> in that the caller's
/// request was well-formed; the load simply yielded nothing.
/// </summary>
/// <param name="Reason">Single-sentence description of why no dataset was produced.</param>
[Description("Raised when a dataset load completed without surfacing any new dataset in the viewer's catalog.")]
internal sealed record DatasetLoadFailed(
    [property: Description("Single-sentence description of why no dataset was produced.")] string Reason)
    : ToolError("dataset_load_failed", $"The dataset load produced no result: {Reason}.");
