namespace EncDotNet.S100.Viewer.Resources;

// Check route's verdict card and findings list (#959). Kept in their own
// part so concurrent Routes work doesn't collide.
internal static partial class Strings
{
    public static string Check_ToReviewFormat => Get(nameof(Check_ToReviewFormat));
    public static string Check_ToCheckFormat => Get(nameof(Check_ToCheckFormat));
    public static string Check_MoreFormat => Get(nameof(Check_MoreFormat));
    public static string Check_VerdictCheckFormat => Get(nameof(Check_VerdictCheckFormat));
    public static string Check_AndMoreToReviewFormat => Get(nameof(Check_AndMoreToReviewFormat));
    public static string Check_ClearNoDraft => Get(nameof(Check_ClearNoDraft));
    public static string Check_SummaryClearance => Get(nameof(Check_SummaryClearance));
    public static string Check_SummaryRoute => Get(nameof(Check_SummaryRoute));
    public static string Check_SummaryWarnings => Get(nameof(Check_SummaryWarnings));
    public static string Check_SummaryNotChecked => Get(nameof(Check_SummaryNotChecked));
    public static string Check_SummaryAddDraft => Get(nameof(Check_SummaryAddDraft));
    public static string Check_SummaryClearanceFormat => Get(nameof(Check_SummaryClearanceFormat));
    public static string Check_SummaryClearanceNoMarginFormat => Get(nameof(Check_SummaryClearanceNoMarginFormat));
    public static string Check_SummaryNothingFound => Get(nameof(Check_SummaryNothingFound));
    public static string Check_SummaryNothingOnRoute => Get(nameof(Check_SummaryNothingOnRoute));
    public static string Check_SummaryInForceFormat => Get(nameof(Check_SummaryInForceFormat));
    public static string Check_SummaryNearFormat => Get(nameof(Check_SummaryNearFormat));
    public static string Check_SummaryNearRouteFormat => Get(nameof(Check_SummaryNearRouteFormat));
    public static string Check_ChipBelowFormat => Get(nameof(Check_ChipBelowFormat));
    public static string Check_ChipTight => Get(nameof(Check_ChipTight));
    public static string Check_ChipMeets => Get(nameof(Check_ChipMeets));
    public static string Check_WindowRouteFindings => Get(nameof(Check_WindowRouteFindings));
    public static string Check_NoDraftHeader => Get(nameof(Check_NoDraftHeader));
    public static string Tooltip_CheckSummaryRow => Get(nameof(Tooltip_CheckSummaryRow));
    public static string Tooltip_CheckAddDraft => Get(nameof(Tooltip_CheckAddDraft));
    public static string Findings_FilterAllFormat => Get(nameof(Findings_FilterAllFormat));
    public static string Findings_FilterClearanceFormat => Get(nameof(Findings_FilterClearanceFormat));
    public static string Findings_FilterRouteFormat => Get(nameof(Findings_FilterRouteFormat));
    public static string Findings_FilterWarningsFormat => Get(nameof(Findings_FilterWarningsFormat));
    public static string Findings_FilterEmpty => Get(nameof(Findings_FilterEmpty));
}
