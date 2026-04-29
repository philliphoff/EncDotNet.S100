using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Capability interface for catalogues that supply Lua portrayal rules
/// (S-100 Part 9A). A catalogue implements this interface when at least
/// one of its loaded rules has <see cref="PortrayalRuleType.Lua"/>, or
/// when a future edition of its product specification may ship Lua
/// rules even if the current catalogue does not.
/// </summary>
/// <remarks>
/// Implementations should throw <see cref="KeyNotFoundException"/> from
/// <see cref="GetLuaScript"/> when the named script is not present in
/// the loaded catalogue. The "this product never has Lua" assertion
/// belongs in the rule list, not in the type system — vendor catalogues,
/// national variants, and future editions can always introduce Lua rules.
/// </remarks>
public interface ILuaRuleSource
{
    /// <summary>Returns the Lua script source for the named rule.</summary>
    Script GetLuaScript(string scriptName);
}
