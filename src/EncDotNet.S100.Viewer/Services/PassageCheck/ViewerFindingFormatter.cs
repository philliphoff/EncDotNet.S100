using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The findings' wording from <c>Strings.resx</c>, in the app's culture, the
/// mariner's depth unit and the Local/UTC preference (#959).
/// </summary>
/// <remarks>
/// Depths show in metres, or in feet for any other unit, as the Passage card
/// shows draft and UKC (<see cref="PassageFormatting"/>). ETAs show compactly
/// against the departure (<see cref="PassageFormatting.FormatEta"/>), and a
/// warning's validity in full (<see cref="TimeFormatting"/>).
/// </remarks>
internal sealed class ViewerFindingFormatter : RouteFindingFormatter
{
    private readonly TimeFormat _timeFormat;
    private readonly DateTimeOffset _departure;

    /// <param name="depthUnit">The mariner's depth unit.</param>
    /// <param name="timeFormat">Local or UTC.</param>
    /// <param name="departure">The departure ETAs are shown against.</param>
    public ViewerFindingFormatter(DepthUnit depthUnit, TimeFormat timeFormat, DateTimeOffset departure)
        : base(CultureInfo.CurrentCulture, depthUnit == DepthUnit.Metres ? DepthUnit.Metres : DepthUnit.Feet)
    {
        _timeFormat = timeFormat;
        _departure = departure;
    }

    protected override string FormatTime(DateTimeOffset time) => TimeFormatting.Format(time, _timeFormat);

    protected override string FormatEta(DateTimeOffset time) => PassageFormatting.FormatEta(time, _departure, _timeFormat);

    protected override string Separator => Strings.RouteCheck_Separator;
    protected override string LegFormat => Strings.Check_LegFormat;
    protected override string LegsFormat => Strings.Findings_LegsFormat;
    protected override string LengthFormat => Strings.RouteCheck_LengthFormat;

    protected override string CrossesLand => Strings.RouteCheck_CrossesLand;
    protected override string LandInCorridor => Strings.RouteCheck_LandInCorridor;
    protected override string CrossesDrying => Strings.RouteCheck_CrossesDrying;
    protected override string DryingInCorridor => Strings.RouteCheck_DryingInCorridor;
    protected override string OutsideChartCoverage => Strings.RouteCheck_OutsideChartCoverage;
    protected override string DangerInCorridorFormat => Strings.RouteCheck_DangerInCorridorFormat;
    protected override string DangerNearRouteFormat => Strings.RouteCheck_DangerNearRouteFormat;
    protected override string Wreck => Strings.RouteCheck_Wreck;
    protected override string Obstruction => Strings.RouteCheck_Obstruction;
    protected override string Rock => Strings.RouteCheck_Rock;
    protected override string CrossesSafetyContour => Strings.RouteCheck_CrossesSafetyContour;
    protected override string SafetyContourInCorridor => Strings.RouteCheck_SafetyContourInCorridor;
    protected override string SoundingsBelowSafetyDepth => Strings.RouteCheck_SoundingsBelowSafetyDepth;
    protected override string CrossesS102Shoal => Strings.RouteCheck_CrossesS102Shoal;
    protected override string S102ShoalInCorridor => Strings.RouteCheck_S102ShoalInCorridor;
    protected override string ToPortFormat => Strings.RouteCheck_ToPortFormat;
    protected override string ToStarboardFormat => Strings.RouteCheck_ToStarboardFormat;
    protected override string OnTrack => Strings.RouteCheck_OnTrack;
    protected override string LeastDepthFormat => Strings.RouteCheck_LeastDepthFormat;
    protected override string SoundingCountFormat => Strings.RouteCheck_SoundingCountFormat;
    protected override string S102CellWithUncertaintyFormat => Strings.RouteCheck_S102CellWithUncertaintyFormat;
    protected override string S102CellFormat => Strings.RouteCheck_S102CellFormat;
    protected override string DepthUnknown => Strings.RouteCheck_DepthUnknown;
    protected override string Awash => Strings.RouteCheck_Awash;
    protected override string CoversAndUncovers => Strings.RouteCheck_CoversAndUncovers;
    protected override string AboveWater => Strings.RouteCheck_AboveWater;
    protected override string BelowSafetyDepthFormat => Strings.RouteCheck_BelowSafetyDepthFormat;
    protected override string BelowKeelFormat => Strings.RouteCheck_BelowKeelFormat;
    protected override string BelowSafetyContourFormat => Strings.RouteCheck_BelowSafetyContourFormat;

    protected override string AreaEntersFormat => Strings.RouteArea_EntersFormat;
    protected override string AreaCrossesFormat => Strings.RouteArea_CrossesFormat;
    protected override string AreaInCorridorFormat => Strings.RouteArea_InCorridorFormat;
    protected override string AreaNotInForceFormat => Strings.RouteArea_NotInForceFormat;
    protected override string AreaNotInForceAtFormat => Strings.RouteArea_NotInForceAtFormat;
    protected override string RangeFormat => Strings.RouteArea_RangeFormat;
    protected override string FromFormat => Strings.RouteArea_FromFormat;
    protected override string UntilFormat => Strings.RouteArea_UntilFormat;

    protected override string AreaKindLabel(RouteAreaKind kind) => kind switch
    {
        RouteAreaKind.RestrictedArea => Strings.RouteArea_RestrictedArea,
        RouteAreaKind.MilitaryPracticeArea => Strings.RouteArea_MilitaryPracticeArea,
        RouteAreaKind.CautionArea => Strings.RouteArea_CautionArea,
        RouteAreaKind.AnchorageArea => Strings.RouteArea_AnchorageArea,
        RouteAreaKind.SeparationZone => Strings.RouteArea_SeparationZone,
        RouteAreaKind.PrecautionaryArea => Strings.RouteArea_PrecautionaryArea,
        RouteAreaKind.InshoreTrafficZone => Strings.RouteArea_InshoreTrafficZone,
        RouteAreaKind.SubmarineTransitLane => Strings.RouteArea_SubmarineTransitLane,
        _ => Strings.RouteArea_MarineProtectedArea,
    };

    protected override string WarningInsideAreaFormat => Strings.Findings_WarningInsideFormat;
    protected override string WarningCrossesLineFormat => Strings.Findings_WarningCrossesFormat;
    protected override string WarningNearFormat => Strings.Findings_WarningNearFormat;
    protected override string WarningNotInForceFormat => Strings.Findings_WarningNotInForceFormat;
    protected override string WarningCancelledFormat => Strings.Findings_WarningCancelledFormat;
    protected override string WarningNotInBulletinFormat => Strings.Findings_WarningNotInBulletinFormat;
    protected override string WarningInForceAtFormat => Strings.Findings_WarningInForceAtFormat;
    protected override string WarningNotInForceAtFormat => Strings.Findings_WarningNotInForceAtFormat;
    protected override string WarningCancelledStatus => Strings.Findings_WarningCancelled;
    protected override string WarningNotInBulletinStatusFormat => Strings.Findings_WarningNotInBulletin;
    protected override string ValidityRangeFormat => Strings.Findings_WarningRangeFormat;
    protected override string ValidityFromFormat => Strings.Pick_Warning_ValidFrom;
    protected override string ValidityUntilFormat => Strings.Pick_Warning_ValidUntil;
    protected override string ValidityOpen => Strings.Pick_Warning_ValidUntilCancelled;

    protected override string BelowMinutesFormat => Strings.Findings_BelowMinutesFormat;
    protected override string BelowDistanceFormat => Strings.Findings_BelowDistanceFormat;
    protected override string MinAtFormat => Strings.Findings_MinAtFormat;
    protected override string MinFormat => Strings.Findings_MinFormat;
    protected override string ChartedDepthFromS101 => Strings.Findings_S101Only;
    protected override string OutsideS102Format => Strings.Findings_OutsideS102Format;
    protected override string DatumUnconfirmed => Strings.Findings_DatumUnconfirmed;
    protected override string DatumUnconfirmedMetaFormat => Strings.Findings_DatumUnconfirmedMetaFormat;
    protected override string WaterLevelEnds => Strings.Findings_WaterLevelEnds;
    protected override string WaterLevelStartsLate => Strings.Findings_WaterLevelStartsLate;
    protected override string NoWaterLevelCoverage => Strings.Findings_NoWaterLevelCoverage;
    protected override string WaterLevelNoData => Strings.Findings_WaterLevelNoData;
    protected override string DatumMismatch => Strings.Findings_DatumMismatch;
    protected override string NoChartedDepth => Strings.Findings_NoChartedDepth;
    protected override string StretchFromFormat => Strings.Findings_FromFormat;
    protected override string StretchLengthFormat => Strings.Check_LegDistanceFormat;
    protected override string TideNotAppliedForFormat => Strings.Findings_TideNotAppliedForFormat;
    protected override string TideNotApplied => Strings.Findings_TideNotApplied;
    protected override string NoDepthSource => Strings.Findings_NoDepthSource;
}
