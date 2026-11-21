using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Tide predictions in view for tests: fixed, or none.</summary>
internal sealed class FixedTidePredictions(TidePredictionsInView value) : ITidePredictionsInView
{
    /// <summary>No station list.</summary>
    public static FixedTidePredictions None { get; } = new(TidePredictionsInView.None);

    public event EventHandler? Changed { add { } remove { } }

    public TidePredictionsInView For(GeoBounds? view) => value;
}
