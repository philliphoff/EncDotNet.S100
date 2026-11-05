using EncDotNet.S100.Scripting;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The <c>--lua-engine</c> option shared by <c>render</c>, <c>tiles</c> and
/// <c>mcp serve</c>: which Lua engine runs the portrayal rules.
/// </summary>
internal static class LuaEngineOption
{
    /// <summary>The option's template.</summary>
    public const string Template = "--lua-engine <ENGINE>";

    /// <summary>The option's help text.</summary>
    public const string Description =
        "The Lua engine that runs the S-100 portrayal rules: luacsharp (default) or moonsharp. Both give the same output; moonsharp is kept for comparison while luacsharp settles in.";

    /// <summary>The option's default.</summary>
    public const string Default = "luacsharp";

    /// <summary>Checks an option value.</summary>
    /// <param name="value">The value given.</param>
    /// <returns>Success, or an error that names the accepted values.</returns>
    public static ValidationResult Validate(string? value) =>
        value is null || LuaEngineKinds.TryParse(value, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error(
                $"Invalid --lua-engine value '{value}'. Use {string.Join(" or ", LuaEngineKinds.Names)}.");

    /// <summary>Resolves an option value that <see cref="Validate"/> accepted.</summary>
    /// <param name="value">The value given.</param>
    /// <returns>The engine.</returns>
    public static LuaEngineKind Resolve(string? value) =>
        LuaEngineKinds.TryParse(value, out var kind) ? kind : LuaEngineKinds.Default;
}
