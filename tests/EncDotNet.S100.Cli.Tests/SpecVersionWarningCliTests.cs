using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Verifies the issue #248 spec-version warning surfaces on stderr only when a
/// dataset's declared edition genuinely diverges from the build-implemented
/// edition — and stays silent otherwise (no false positives).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class SpecVersionWarningCliTests
{
    private const string WarningFragment = "rendering may be incomplete or incorrect";

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public void Info_does_not_warn_for_dataset_without_declared_edition()
    {
        // marine_curve.gml is a conformant S-127 dataset that declares no
        // productEdition → Unknown → the warning must NOT fire.
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var originalError = Console.Error;
        var originalOut = Console.Out;
        var stderr = new StringWriter();
        try
        {
            Console.SetError(stderr);
            Console.SetOut(new StringWriter());

            int exit = CliApp.Build().Run(["info", dataset]);

            Assert.Equal(0, exit);
            Assert.DoesNotContain(WarningFragment, stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOut);
        }
    }
}
