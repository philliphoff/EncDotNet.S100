using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 s57 convert -o &lt;output&gt; &lt;source&gt;</c> — converts an S-57 base
/// cell to an S-101 dataset by translating it with
/// <see cref="S57ToS101Translator"/> and encoding the result with
/// <see cref="S101DocumentWriter"/> (ISO/IEC 8211; S-100 Part 10a).
/// </summary>
/// <remarks>
/// This command is a thin driver over the existing translation and encoding
/// libraries; it deliberately does not alter conversion semantics, which are
/// owned by <see cref="S57ToS101Translator"/>.
/// </remarks>
internal sealed class S57ConvertCommand : Command<S57ConvertCommandSettings>
{
    public override int Execute(CommandContext context, S57ConvertCommandSettings settings)
    {
        try
        {
            var dataset = S57Dataset.Open(settings.SourcePath);
            var translator = new S57ToS101Translator();
            var document = translator.Translate(dataset);

            S101DocumentWriter.WriteToFile(settings.OutputPath, document);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Converted[/] {settings.SourcePath} [green]→[/] {settings.OutputPath} ({document.Features.Count} features).");
            return 0;
        }
        catch (NotSupportedException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Not supported:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 4;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }
}
