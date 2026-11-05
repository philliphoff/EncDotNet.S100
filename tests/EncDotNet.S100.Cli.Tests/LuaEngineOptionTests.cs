using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>The <c>--lua-engine</c> option of <c>render</c>, <c>tiles</c> and <c>mcp serve</c>.</summary>
public sealed class LuaEngineOptionTests
{
    [Theory]
    [InlineData("luacsharp", LuaEngineKind.LuaCSharp)]
    [InlineData("LuaCSharp", LuaEngineKind.LuaCSharp)]
    [InlineData("moonsharp", LuaEngineKind.MoonSharp)]
    [InlineData(null, LuaEngineKind.LuaCSharp)]
    public void Resolve_maps_names_to_engines(string? value, LuaEngineKind expected)
    {
        Assert.True(LuaEngineOption.Validate(value).Successful);
        Assert.Equal(expected, LuaEngineOption.Resolve(value));
    }

    [Fact]
    public void Validate_names_the_accepted_values()
    {
        var result = LuaEngineOption.Validate("nlua");

        Assert.False(result.Successful);
        Assert.Contains("luacsharp or moonsharp", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Commands_default_to_luacsharp()
    {
        Assert.Equal("luacsharp", new RenderCommand.Settings().LuaEngine);
        Assert.Equal("luacsharp", new TilesServeCommand.Settings().LuaEngine);
        Assert.Equal("luacsharp", new McpServeCommand.Settings().LuaEngine);
    }

    [Fact]
    public void Render_rejects_an_unknown_engine()
    {
        var settings = new RenderCommand.Settings { Input = "chart.000", OutputArgument = "chart.png", LuaEngine = "nlua" };

        Assert.False(settings.Validate().Successful);
    }
}
