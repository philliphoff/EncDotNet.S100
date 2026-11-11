namespace EncDotNet.S100.Scripting;

/// <summary>
/// Prepares an <see cref="ILuaContext"/> with the S-100 host environment that
/// portrayal catalogue Lua scripts expect. This includes colour-token lookup,
/// context parameters (safety contour, etc.), and standard helper functions
/// defined by the S-100 Portrayal Model.
/// </summary>
public static class S100LuaHost
{
    /// <summary>
    /// Configures the Lua context with standard S-100 host functions and
    /// the provided colour palette and context parameters.
    /// </summary>
    /// <param name="context">The Lua context to configure.</param>
    /// <param name="colorLookup">
    /// A function that resolves an S-52 colour token (e.g. "DEPVS") to an
    /// sRGB hex string (e.g. "#61B7FF") for the active palette.
    /// </param>
    /// <param name="contextParameters">
    /// Named context parameters (e.g. "SafetyContour" → 30.0) exposed to scripts.
    /// </param>
    public static void Configure(
        ILuaContext context,
        Func<string, string> colorLookup,
        IReadOnlyDictionary<string, object?> contextParameters)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(colorLookup);
        ArgumentNullException.ThrowIfNull(contextParameters);

        // S-100 PS 5.2 §14 — host_portrayalContextParameter(parameterName)
        context.SetGlobal("host_portrayalContextParameter",
            (Func<string, object?>)(name =>
                contextParameters.TryGetValue(name, out var value) ? value : null));

        // S-100 PS 5.2 §14 — host_colorLookup(token)
        context.SetGlobal("host_colorLookup", (Func<string, string>)colorLookup);

        // Expose all context parameters as individual globals for convenience
        // (some scripts access them directly rather than via host functions).
        foreach (var (key, value) in contextParameters)
        {
            context.SetGlobal(key, value);
        }
    }
}
