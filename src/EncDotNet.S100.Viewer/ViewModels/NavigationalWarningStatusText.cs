using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Says why another warning ended a navigational warning (#992), as the
/// Warning card's reason line and the panel's "Not in force" rows show it.
/// </summary>
internal static class NavigationalWarningStatusText
{
    /// <summary>
    /// "Cancelled — not in in-force bulletin NAVAREA X 110/26", "Cancelled by
    /// NAVAREA X 104/26" or "Replaced by in-force bulletin …";
    /// <see langword="null"/> when no other warning ended it.
    /// </summary>
    /// <param name="state">The warning's status.</param>
    /// <param name="brief">
    /// The panel row's shorter form for a bulletin ("Not in bulletin NAVAREA X
    /// 110/26"), which fits the row; the card and MCP give the full reason.
    /// </param>
    public static string? Reason(NavigationalWarningState state, bool brief = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.By is not { } by)
            return null;
        var format = state.Reason switch
        {
            NavigationalWarningStatusReason.NotInBulletin => brief ? Strings.NavWarnings_NotInBulletinFormat : Strings.Pick_Warning_EndedNotInBulletinFormat,
            NavigationalWarningStatusReason.BulletinReplaced => Strings.Pick_Warning_EndedReplacedFormat,
            NavigationalWarningStatusReason.Cancelled => Strings.Pick_Warning_EndedCancelledByFormat,
            _ => null,
        };
        return format is null
            ? null
            : string.Format(CultureInfo.CurrentCulture, format, by.Label ?? by.Title ?? string.Empty).Trim();
    }

    /// <summary>The reason's code for MCP: <c>"validity"</c>, <c>"cancellation_date"</c>, <c>"cancelled"</c>, <c>"not_in_bulletin"</c>, <c>"bulletin_replaced"</c>, or <see langword="null"/> while in force.</summary>
    public static string? Code(NavigationalWarningStatusReason reason) => reason switch
    {
        NavigationalWarningStatusReason.Validity => "validity",
        NavigationalWarningStatusReason.CancellationDate => "cancellation_date",
        NavigationalWarningStatusReason.Cancelled => "cancelled",
        NavigationalWarningStatusReason.NotInBulletin => "not_in_bulletin",
        NavigationalWarningStatusReason.BulletinReplaced => "bulletin_replaced",
        _ => null,
    };
}
