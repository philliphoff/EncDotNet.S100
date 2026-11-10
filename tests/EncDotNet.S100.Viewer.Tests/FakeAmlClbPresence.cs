using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>An <see cref="IAmlClbPresence"/> a test sets.</summary>
internal sealed class FakeAmlClbPresence : IAmlClbPresence
{
    public bool IsClbDrawn { get; private set; }

    public event Action? Changed;

    public void Set(bool drawn)
    {
        IsClbDrawn = drawn;
        Changed?.Invoke();
    }
}
