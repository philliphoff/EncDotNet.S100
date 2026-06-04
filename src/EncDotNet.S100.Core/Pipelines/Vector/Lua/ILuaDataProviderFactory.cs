namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Creates a fresh, render-scoped <see cref="ILuaDataProvider"/> for each
/// portrayal pass. The factory binds the product's dataset and feature
/// catalogue at construction; <see cref="Create"/> supplies the per-render
/// <see cref="MarinerSettings"/>.
/// </summary>
/// <remarks>
/// Providers are stateful (they accumulate emitted instructions), so a new
/// instance per render avoids shared mutable state. Threading
/// <see cref="MarinerSettings"/> through <see cref="Create"/> also gives the
/// provider access to render-scoped display preferences — the seam through
/// which depth-unit selection (metres/feet/fathoms) reaches the host's spatial
/// data conversion without affecting metric rule comparisons.
/// </remarks>
public interface ILuaDataProviderFactory
{
    /// <summary>Creates a new provider for one render with the given settings.</summary>
    /// <param name="mariner">Mariner-configurable display preferences for this render.</param>
    ILuaDataProvider Create(MarinerSettings mariner);
}
