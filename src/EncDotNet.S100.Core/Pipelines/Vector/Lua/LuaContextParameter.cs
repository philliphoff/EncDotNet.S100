namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// A context-parameter declaration from a Lua portrayal catalogue
/// (S-100 Part 9A): the parameter id, its encoded type, and its default
/// value, as declared in the portrayal catalogue. This is the Core-level
/// projection of a catalogue's context-parameter set, kept free of any
/// dependency on the portrayal-catalogue assembly so that
/// <see cref="ILuaRuleSource"/> can live in Core.
/// </summary>
/// <param name="Id">Parameter id (e.g. <c>SafetyContour</c>, <c>FourShades</c>, <c>TwoShades</c>).</param>
/// <param name="Type">Encoded parameter type (e.g. <c>boolean</c>, <c>real</c>, <c>text</c>).</param>
/// <param name="Default">Default value as declared in the catalogue.</param>
public sealed record LuaContextParameter(string Id, string Type, string Default);
