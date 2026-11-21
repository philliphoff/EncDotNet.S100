using Mapsui;
using Mapsui.Animations;
using Mapsui.Extensions;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Predicts where a Mapsui fling comes to rest (map rendering slice&#160;9,
/// open question&#160;5). Mapsui's <see cref="Navigator"/> keeps its
/// animations private, so the target can't be read back; but
/// <see cref="FlingAnimation"/> is deterministic in the release velocity, so
/// the same arithmetic gives the travel. It moves the view a fixed fraction of
/// the velocity per animation tick, so the travel depends on the frame rate;
/// this assumes 60&#160;frames a second, and the demand planner scales it by
/// what earlier flings did.
/// </summary>
public static class MapsuiFlingForecast
{
    /// <summary>The frame interval the forecast assumes, in milliseconds.</summary>
    public const double NominalFrameMilliseconds = 1000.0 / 60.0;

    // FlingAnimation: each tick moves the view by velocity × (1 − ease) × 0.016 pixels.
    private const double TickFactor = 0.016;

    /// <summary>
    /// The world travel of the view centre, and the duration, of the fling
    /// Mapsui would start for <c>Navigator.Fling(velocityX, velocityY, maxDuration)</c>,
    /// or <see langword="null"/> when it would start none.
    /// </summary>
    /// <param name="viewport">The viewport the fling starts from.</param>
    /// <param name="velocityX">Release velocity, screen pixels per second (as Mapsui's fling tracker reports it).</param>
    /// <param name="velocityY">Release velocity, screen pixels per second.</param>
    /// <param name="maxDuration">The fling's longest duration, in milliseconds.</param>
    /// <returns>The travel in EPSG:3857 metres and the duration, or null.</returns>
    public static (double TravelX, double TravelY, TimeSpan Duration)? Travel(
        Viewport viewport, double velocityX, double velocityY, long maxDuration)
    {
        // The rules of FlingAnimation.Create.
        if (maxDuration < 16)
        {
            return null;
        }

        var vx = -velocityX;
        var vy = -velocityY;
        var speed = Math.Sqrt((vx * vx) + (vy * vy));
        var durationMs = speed / 10.0;
        if (speed < 100.0 || durationMs < 16.0)
        {
            return null;
        }

        durationMs = Math.Min(durationMs, maxDuration);

        // The rule of FlingAnimation's tick, summed over the frames.
        var ticks = Math.Max(1, (int)Math.Round(durationMs / NominalFrameMilliseconds));
        var sum = 0.0;
        for (var i = 1; i <= ticks; i++)
        {
            sum += 1.0 - Easing.SinIn.Ease((double)i / ticks);
        }

        var screenX = vx * sum * TickFactor;
        var screenY = vy * sum * TickFactor;
        var origin = viewport.ScreenToWorld(0.0, 0.0);
        var moved = viewport.ScreenToWorld(screenX, screenY);
        return (moved.X - origin.X, moved.Y - origin.Y, TimeSpan.FromMilliseconds(durationMs));
    }
}
