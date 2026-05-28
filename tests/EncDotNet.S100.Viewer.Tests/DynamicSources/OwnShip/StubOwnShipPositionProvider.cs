using System;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

/// <summary>
/// Push-driven test double: no timer, no math. Tests call
/// <see cref="Push"/> to drive <see cref="OwnShipSource"/>
/// state-machine transitions deterministically.
/// </summary>
internal sealed class StubOwnShipPositionProvider : IOwnShipPositionProvider
{
    public OwnShipPosition? Current { get; private set; }

    public event EventHandler<OwnShipPosition>? Updated;

    public void Push(OwnShipPosition fix)
    {
        Current = fix;
        Updated?.Invoke(this, fix);
    }
}
