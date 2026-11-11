using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The Legend row a picked feature is drawn with (<see cref="LegendService.FindRow"/>,
/// #1410): Object information's key line shows it, and its <b>In legend</b>
/// link opens the tab there.
/// </summary>
/// <param name="Section">The section the row is in.</param>
/// <param name="Row">The row.</param>
internal sealed record LegendRowMatch(LegendSection Section, LegendEntry Row)
{
    /// <summary>
    /// The product the key line names: the spec ("S-102"), or a weather
    /// field's name ("Wave height"), whose section is the field.
    /// </summary>
    public string Product => Section.Id == Section.Spec ? Section.Spec : Section.Title;
}
