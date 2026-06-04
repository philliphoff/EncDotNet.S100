namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Binds a product's declared Lua context parameter to a value derived from
/// <see cref="MarinerSettings"/>. A binding pairs the <i>declared</i> parameter
/// id with a factory that computes its value and a serializer that encodes that
/// value as a Lua string literal.
/// </summary>
/// <remarks>
/// <para>
/// This is where per-product mapping policy lives — including name differences
/// and inversions. For example, S-101 declares <c>FourShades</c> while S-131
/// declares its inverse <c>TwoShades</c>; the S-131 binding expresses that as
/// <c>new(&quot;TwoShades&quot;, m =&gt; !m.FourShades, LuaValueSerializers.Bool)</c>.
/// </para>
/// <para>
/// The generic <see cref="LuaRuleExecutor"/> applies a binding only when its
/// <see cref="DeclaredId"/> is present in the catalogue's declared
/// <see cref="ILuaRuleSource.ContextParameters"/>, and skips it when
/// <see cref="ValueFactory"/> returns <see langword="null"/> (e.g. an optional
/// language code that the user left blank). This keeps the engine from calling
/// <c>PortrayalSetContextParameter</c> for parameters the catalogue never
/// declared, which the Lua side would reject.
/// </para>
/// </remarks>
/// <param name="DeclaredId">The catalogue-declared parameter id this binding targets.</param>
/// <param name="ValueFactory">
/// Computes the override value from the render's settings, or returns
/// <see langword="null"/> to leave the catalogue default in place.
/// </param>
/// <param name="Serialize">Encodes the value as the Lua literal string.</param>
public sealed record LuaContextParameterBinding(
    string DeclaredId,
    Func<MarinerSettings, object?> ValueFactory,
    Func<object?, string> Serialize);
