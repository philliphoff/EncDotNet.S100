using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The area findings (#956): S-101 restricted, military practice, caution,
/// anchorage and traffic areas and S-122 protected areas the route's
/// corridor meets, from <see cref="RouteAreaCheck"/> over the loaded data,
/// judged at the ETAs where the vessel is in each area, or at the view time
/// where the legs have no speed. Runs with or without a draft.
/// </summary>
/// <remarks>
/// It shares the <see cref="RouteCheckSourceCache"/> with
/// <see cref="RouteCheckFindingProducer"/>, so the S-101 cells' indexes are
/// built once. Each finding refers to the area's feature, so pick and
/// describe can open it.
/// </remarks>
internal sealed class AreaFindingProducer : IRouteFindingProducer
{
    private readonly RouteCheckSourceCache _sources;
    private readonly RouteAreaOptions? _options;

    /// <param name="sources">The sources shared with the chart checks, or <c>null</c> for its own.</param>
    /// <param name="options">Tuning, or <c>null</c> for the defaults with the viewer's wording.</param>
    public AreaFindingProducer(RouteCheckSourceCache? sources = null, RouteAreaOptions? options = null)
    {
        _sources = sources ?? new RouteCheckSourceCache();
        _options = options;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CheckLegs.Count == 0)
            return Task.FromResult<IReadOnlyList<RouteFinding>>([]);

        var options = _options ?? RouteAreaOptions.Default with { Text = Text(request) };
        return Task.Run<IReadOnlyList<RouteFinding>>(
            () =>
            {
                var schedule = PassageSchedule.Compute(request.Legs, request.Departure);
                return _sources.Get(request.Datasets)
                    .CheckAreas(request.CheckLegs, new RouteWarningTiming(schedule, request.ViewTime), options, cancellationToken)
                    .Findings;
            },
            cancellationToken);
    }

    /// <summary>The finding wording from <c>Strings.resx</c>, times in the Local/UTC preference.</summary>
    internal static RouteAreaText Text(RouteFindingRequest request) => new()
    {
        EntersFormat = Strings.RouteArea_EntersFormat,
        CrossesFormat = Strings.RouteArea_CrossesFormat,
        InCorridorFormat = Strings.RouteArea_InCorridorFormat,
        NotInForceFormat = Strings.RouteArea_NotInForceFormat,
        NotInForceAtFormat = Strings.RouteArea_NotInForceAtFormat,
        RestrictedArea = Strings.RouteArea_RestrictedArea,
        MilitaryPracticeArea = Strings.RouteArea_MilitaryPracticeArea,
        CautionArea = Strings.RouteArea_CautionArea,
        AnchorageArea = Strings.RouteArea_AnchorageArea,
        SeparationZone = Strings.RouteArea_SeparationZone,
        PrecautionaryArea = Strings.RouteArea_PrecautionaryArea,
        InshoreTrafficZone = Strings.RouteArea_InshoreTrafficZone,
        SubmarineTransitLane = Strings.RouteArea_SubmarineTransitLane,
        MarineProtectedArea = Strings.RouteArea_MarineProtectedArea,
        RangeFormat = Strings.RouteArea_RangeFormat,
        FromFormat = Strings.RouteArea_FromFormat,
        UntilFormat = Strings.RouteArea_UntilFormat,
        LegFormat = Strings.Check_LegFormat,
        Separator = Strings.RouteCheck_Separator,
        FormatTime = time => PassageFormatting.FormatEta(time, request.Departure, request.TimeFormat),
        Culture = CultureInfo.CurrentCulture,
    };
}
