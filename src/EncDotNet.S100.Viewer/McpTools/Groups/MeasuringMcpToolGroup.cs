using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The Measure and Range &amp; bearing tools: <c>get_measure</c>, <c>set_measure</c>, <c>get_range_bearing</c> and <c>set_range_bearing</c>.</summary>
internal sealed class MeasuringMcpToolGroup : IViewerMcpToolGroup
{
    private readonly MeasureSession _measure;
    private readonly EncDotNet.S100.Viewer.ViewModels.MeasureCardViewModel _measureCard;
    private readonly EncDotNet.S100.Viewer.Tools.RangeBearingState _rangeBearing;
    private readonly ViewerSettings _settings;

    public MeasuringMcpToolGroup(
        MeasureSession measure,
        EncDotNet.S100.Viewer.ViewModels.MeasureCardViewModel measureCard,
        EncDotNet.S100.Viewer.Tools.RangeBearingState rangeBearing,
        ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(measureCard);
        ArgumentNullException.ThrowIfNull(rangeBearing);
        ArgumentNullException.ThrowIfNull(settings);

        _measure = measure;
        _measureCard = measureCard;
        _rangeBearing = rangeBearing;
        _settings = settings;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var measureInvoker = new DispatcherRouteEditInvoker();
        yield return MeasureMcpAdapters.Create(new GetMeasureTool(_measure, _measureCard, measureInvoker));
        yield return MeasureMcpAdapters.Create(new SetMeasureTool(_measure, _measureCard, measureInvoker));
        var rangeBearingInvoker = new DispatcherRouteEditInvoker();
        yield return RangeBearingMcpAdapters.Create(new SetRangeBearingTool(_rangeBearing, _settings, rangeBearingInvoker));
        yield return RangeBearingMcpAdapters.Create(new GetRangeBearingTool(_rangeBearing, rangeBearingInvoker));
    }
}
