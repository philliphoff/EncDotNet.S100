using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Captures Spectre's completed command model without rendering terminal help.
/// </summary>
internal sealed class SkillModelCaptureHelpProvider : IHelpProvider
{
    public ICommandModel? Model { get; private set; }

    public IEnumerable<IRenderable> Write(ICommandModel model, ICommandInfo? command)
    {
        Model = model;
        return [];
    }
}
