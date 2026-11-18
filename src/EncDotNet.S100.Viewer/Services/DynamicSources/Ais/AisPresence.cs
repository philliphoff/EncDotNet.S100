namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

/// <summary>
/// Whether an AIS entry is loaded (data source design note D8), for what
/// follows it, such as the Vessels activity tab. Set by
/// <see cref="AisHostExtension"/>.
/// </summary>
internal sealed class AisPresence
{
    private bool _isLoaded;

    /// <summary>True while an AIS entry is loaded.</summary>
    public bool IsLoaded => Volatile.Read(ref _isLoaded);

    /// <summary>Raised when <see cref="IsLoaded"/> changes.</summary>
    public event Action<bool>? Changed;

    /// <summary>Sets <see cref="IsLoaded"/>.</summary>
    /// <param name="loaded">Whether an AIS entry is loaded.</param>
    public void Set(bool loaded)
    {
        if (_isLoaded == loaded)
            return;
        Volatile.Write(ref _isLoaded, loaded);
        Changed?.Invoke(loaded);
    }
}
