namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// The processor factory the tests' headless catalogs share, as the CLI shares
/// one per <c>mcp serve</c> run. It lives for the test run.
/// </summary>
internal static class CliTestProcessors
{
    /// <summary>A bundled all-products factory on the default Lua engine.</summary>
    public static BundledDatasetProcessorFactory Bundled { get; } = BundledDatasetProcessorFactory.Create(EncDotNet.S100.Scripting.LuaEngineKinds.Default);
}
