using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Updates;
using Spectre.Console;
using Spectre.Console.Cli.Help;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Covers the comprehensive Markdown emitted by <c>s100 --skill</c>.
/// </summary>
public sealed class SkillOutputTests
{
    [Fact]
    public async Task SkillOptionWritesDeterministicMarkdownWithoutCheckingForUpdates()
    {
        var version = new CliVersionInfo("2.4.1", "2.4.1+abc1234");
        var updateChecker = new CountingUpdateChecker();
        var firstOutput = new StringWriter();
        var secondOutput = new StringWriter();
        var standardError = new StringWriter();

        var firstExitCode = await CliRunner.RunAsync(
            ["--skill"],
            version,
            updateChecker,
            standardError,
            standardOutput: firstOutput);
        var secondExitCode = await CliRunner.RunAsync(
            ["--skill"],
            version,
            updateChecker,
            standardError,
            standardOutput: secondOutput);

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Equal(firstOutput.ToString(), secondOutput.ToString());
        Assert.StartsWith("---\nname: s100\n", firstOutput.ToString());
        Assert.Contains("# s100 CLI", firstOutput.ToString());
        Assert.Contains(
            "Gridded S-102, S-104, and S-111 products\nsample only the intersecting region",
            firstOutput.ToString());
        Assert.DoesNotContain(
            "viewport forms are rejected for single coverage",
            firstOutput.ToString());
        Assert.Equal(-1, firstOutput.ToString().IndexOf('\u001b'));
        Assert.DoesNotContain('\r', firstOutput.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.Equal(0, updateChecker.CallCount);
    }

    [Fact]
    public void RootHelpDocumentsSkillOption()
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });

        var exitCode = CliApp.Build("test", console).Run(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("--skill", output.ToString());
        Assert.Contains("agent-oriented CLI guide", output.ToString());
        Assert.Contains("Standalone", output.ToString());
    }

    [Fact]
    public void RendererIncludesEveryVisibleCommandAndParameter()
    {
        var capture = new SkillModelCaptureHelpProvider();
        var exitCode = CliApp.Build("test", helpProvider: capture).Run(["--help"]);

        Assert.Equal(0, exitCode);
        var model = Assert.IsAssignableFrom<ICommandModel>(capture.Model);
        var document = SkillDocumentRenderer.Render(model);

        Assert.Contains("s100 render [input] [output] [options]", document);
        Assert.Contains("s100 validate <dataset> [options]", document);
        Assert.DoesNotContain("[[input]]", document);
        Assert.DoesNotContain("<<dataset>>", document);

        foreach (var (path, command) in WalkCommands(model.Commands, []))
        {
            Assert.Contains($"### `s100 {path}`", document);

            foreach (var argument in command.Parameters
                         .OfType<ICommandArgument>()
                         .Where(argument => !argument.IsHidden))
            {
                Assert.Contains($"`{argument.Value}`", document);
            }

            foreach (var option in command.Parameters
                         .OfType<ICommandOption>()
                         .Where(option => !option.IsHidden))
            {
                foreach (var longName in option.LongNames)
                {
                    Assert.Contains($"--{longName}", document);
                }
                foreach (var shortName in option.ShortNames)
                {
                    Assert.Contains($"-{shortName}", document);
                }
            }
        }
    }

    [Fact]
    public void AuthoredGuidanceReferencesRegisteredCommands()
    {
        var capture = new SkillModelCaptureHelpProvider();
        var exitCode = CliApp.Build("test", helpProvider: capture).Run(["--help"]);

        Assert.Equal(0, exitCode);
        var model = Assert.IsAssignableFrom<ICommandModel>(capture.Model);
        var registeredPaths = WalkCommands(model.Commands, [])
            .Select(item => item.Path)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(
            SkillContent.GuidedCommandPaths,
            path => Assert.Contains(path, registeredPaths));
    }

    private static IEnumerable<(string Path, ICommandInfo Command)> WalkCommands(
        IEnumerable<ICommandInfo> commands,
        IReadOnlyList<string> parentPath)
    {
        foreach (var command in commands.Where(command => !command.IsHidden))
        {
            var path = parentPath.Append(command.Name).ToArray();
            yield return (string.Join(" ", path), command);

            foreach (var child in WalkCommands(command.Commands, path))
            {
                yield return child;
            }
        }
    }

    private sealed class CountingUpdateChecker : ICliUpdateChecker
    {
        public int CallCount { get; private set; }

        public Task<CliUpdateNotice?> CheckAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<CliUpdateNotice?>(null);
        }
    }
}
