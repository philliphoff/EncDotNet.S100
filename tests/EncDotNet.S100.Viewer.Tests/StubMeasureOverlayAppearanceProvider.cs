using System;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.Tests;

internal sealed class StubMeasureOverlayAppearanceProvider : IMeasureOverlayAppearanceProvider
{
    public MeasureOverlayAppearance Current => MeasureOverlayAppearance.Default;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
