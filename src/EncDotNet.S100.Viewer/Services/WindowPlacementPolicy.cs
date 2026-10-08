using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// A screen as seen by <see cref="WindowPlacementPolicy"/>: its working area
/// (excluding dock / taskbar / menu bar) in physical pixels and its scale
/// factor (physical pixels per device-independent unit).
/// </summary>
internal readonly record struct PlacementScreen(PixelRect WorkingArea, double Scaling, bool IsPrimary);

/// <summary>
/// Where the main window should open: the normal-state client
/// <see cref="Size"/> in device-independent units, its top-left
/// <see cref="Position"/> in physical pixels, and the initial
/// <see cref="State"/>. When <see cref="State"/> is
/// <see cref="WindowState.Maximized"/> the bounds are what the window
/// returns to when un-maximized.
/// </summary>
internal readonly record struct PlacementResult(PixelPoint Position, Size Size, WindowState State);

/// <summary>
/// Decides the main window's startup bounds (#825). Pure — screens and the
/// saved placement come in, bounds come out — so the rules are unit tested
/// without a windowing platform.
/// </summary>
/// <remarks>
/// <para><b>First run</b> (nothing saved, or the saved bounds are no longer
/// on any connected screen): <see cref="FirstRunFraction"/> of the primary
/// screen's working area, centred, never smaller than
/// <see cref="PreferredMinimumSize"/> (when the screen allows it). Screens
/// smaller than <see cref="SmallScreenThreshold"/> open maximized.</para>
/// <para><b>Later runs</b>: the saved normal bounds and maximized state,
/// placed on the screen they overlap most, shrunk to that screen's working
/// area if its resolution dropped and moved fully onto it.</para>
/// </remarks>
internal static class WindowPlacementPolicy
{
    /// <summary>Share of the working area a first-run window covers, per axis.</summary>
    public const double FirstRunFraction = 0.8;

    /// <summary>
    /// First-run windows are at least this big (DIPs) when the screen has
    /// room: below it the side panels and Timeline start to crowd the map.
    /// </summary>
    public static readonly Size PreferredMinimumSize = new(1100, 700);

    /// <summary>Working areas smaller than this (DIPs) on either axis open maximized on first run.</summary>
    public static readonly Size SmallScreenThreshold = new(1366, 768);

    /// <summary>
    /// How much of a saved window (DIPs, per axis) must still overlap a
    /// screen's working area for the saved bounds to be trusted. Less than
    /// this and the window could not reasonably be grabbed and dragged back.
    /// </summary>
    public static readonly Size MinimumVisibleSize = new(160, 80);

    /// <summary>
    /// Resolves the startup placement, or <c>null</c> when no screens are
    /// known (headless) and the window should keep its XAML defaults.
    /// </summary>
    /// <param name="saved">The persisted placement, or <c>null</c> for a first run.</param>
    /// <param name="screens">The currently connected screens.</param>
    public static PlacementResult? Resolve(WindowPlacement? saved, IReadOnlyList<PlacementScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0) return null;
        screens = screens.Select(Sanitize).ToArray();

        if (saved is { Width: > 0, Height: > 0 } && double.IsFinite(saved.Width) && double.IsFinite(saved.Height))
        {
            if (Restore(saved, screens) is { } restored) return restored;

            // Off every screen: lay out afresh but keep the maximized state.
            var fresh = FirstRun(PrimaryOf(screens));
            return saved.IsMaximized ? fresh with { State = WindowState.Maximized } : fresh;
        }

        return FirstRun(PrimaryOf(screens));
    }

    /// <summary>The first-run placement on <paramref name="screen"/>.</summary>
    public static PlacementResult FirstRun(PlacementScreen screen)
    {
        screen = Sanitize(screen);
        var area = ToDip(screen.WorkingArea, screen.Scaling);
        var small = area.Width < SmallScreenThreshold.Width || area.Height < SmallScreenThreshold.Height;

        var width = Math.Min(Math.Max(area.Width * FirstRunFraction, PreferredMinimumSize.Width), area.Width);
        var height = Math.Min(Math.Max(area.Height * FirstRunFraction, PreferredMinimumSize.Height), area.Height);
        var size = new Size(Math.Floor(width), Math.Floor(height));

        return new PlacementResult(
            Centre(screen, size),
            size,
            small ? WindowState.Maximized : WindowState.Normal);
    }

    private static PlacementResult? Restore(WindowPlacement saved, IReadOnlyList<PlacementScreen> screens)
    {
        // The saved size is in DIPs, so its pixel extent depends on which
        // screen it lands on: measure the overlap with each screen's scale.
        PlacementScreen? best = null;
        double bestArea = 0;
        foreach (var screen in screens)
        {
            var rect = ToPixelRect(new PixelPoint(saved.X, saved.Y), new Size(saved.Width, saved.Height), screen.Scaling);
            var overlap = rect.Intersect(screen.WorkingArea);
            if (overlap.Width < MinimumVisibleSize.Width * screen.Scaling
                || overlap.Height < MinimumVisibleSize.Height * screen.Scaling)
            {
                continue;
            }
            // Compare in DIPs²: the same window covers 4x the pixels on a 2x screen.
            double area = (double)overlap.Width * overlap.Height / (screen.Scaling * screen.Scaling);
            if (area > bestArea)
            {
                bestArea = area;
                best = screen;
            }
        }
        if (best is not { } target) return null;

        // Shrink to the working area (resolution dropped since the save),
        // then slide fully onto it.
        var work = ToDip(target.WorkingArea, target.Scaling);
        var size = new Size(
            Math.Min(saved.Width, Math.Floor(work.Width)),
            Math.Min(saved.Height, Math.Floor(work.Height)));
        var pixelWidth = (int)Math.Ceiling(size.Width * target.Scaling);
        var pixelHeight = (int)Math.Ceiling(size.Height * target.Scaling);
        var x = Math.Clamp(saved.X, target.WorkingArea.X, Math.Max(target.WorkingArea.X, target.WorkingArea.Right - pixelWidth));
        var y = Math.Clamp(saved.Y, target.WorkingArea.Y, Math.Max(target.WorkingArea.Y, target.WorkingArea.Bottom - pixelHeight));

        return new PlacementResult(
            new PixelPoint(x, y),
            size,
            saved.IsMaximized ? WindowState.Maximized : WindowState.Normal);
    }

    private static PlacementScreen PrimaryOf(IReadOnlyList<PlacementScreen> screens)
    {
        foreach (var screen in screens)
        {
            if (screen.IsPrimary) return screen;
        }
        return screens[0];
    }

    private static PixelPoint Centre(PlacementScreen screen, Size size)
    {
        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(size.Width * screen.Scaling);
        var height = (int)Math.Ceiling(size.Height * screen.Scaling);
        return new PixelPoint(
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    // A platform reporting a zero or non-finite scale is treated as 1:1.
    private static PlacementScreen Sanitize(PlacementScreen screen) =>
        screen.Scaling > 0 && double.IsFinite(screen.Scaling) ? screen : screen with { Scaling = 1 };

    private static Size ToDip(PixelRect rect, double scaling) =>
        new(rect.Width / scaling, rect.Height / scaling);

    private static PixelRect ToPixelRect(PixelPoint position, Size size, double scaling) =>
        new(position, new PixelSize((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling)));
}
