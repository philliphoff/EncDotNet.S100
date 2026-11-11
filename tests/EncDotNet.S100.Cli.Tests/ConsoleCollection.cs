namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Serializes test classes that redirect <see cref="System.Console"/> output
/// via <c>Console.SetOut</c> / <c>Console.SetError</c>. The console streams are
/// process-global, so these classes must not run in parallel with one another or
/// captured output bleeds across tests (e.g. corrupting JSON assertions).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    /// <summary>Collection name shared by all console-capturing CLI test classes.</summary>
    public const string Name = "Console (serialized)";
}
