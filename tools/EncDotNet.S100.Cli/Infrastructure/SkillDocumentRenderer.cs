using System.Globalization;
using System.Text;
using Spectre.Console.Cli.Help;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Renders Spectre's complete command model as agent-oriented Markdown.
/// </summary>
internal static class SkillDocumentRenderer
{
    public static string Render(ICommandModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine("name: s100");
        builder.AppendLine("description: Inspect, validate, query, convert, and render S-100 nautical datasets.");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# s100 CLI");
        builder.AppendLine();
        builder.Append(SkillContent.Read("Overview.md").Trim());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("## Complete command reference");

        foreach (var command in model.Commands.Where(command => !command.IsHidden))
        {
            AppendCommand(builder, model.ApplicationName, command, []);
        }

        return builder.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendCommand(
        StringBuilder builder,
        string applicationName,
        ICommandInfo command,
        IReadOnlyList<string> parentPath)
    {
        var path = parentPath.Append(command.Name).ToArray();
        var fullPath = string.Join(" ", path);

        builder.AppendLine();
        builder.Append("### `");
        builder.Append(applicationName);
        builder.Append(' ');
        builder.Append(fullPath);
        builder.AppendLine("`");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            builder.AppendLine(command.Description.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("#### Usage");
        builder.AppendLine();
        builder.Append("```text");
        builder.AppendLine();
        builder.Append(applicationName);
        builder.Append(' ');
        builder.Append(fullPath);

        foreach (var argument in command.Parameters
                     .OfType<ICommandArgument>()
                     .Where(argument => !argument.IsHidden)
                     .OrderBy(argument => argument.Position))
        {
            builder.Append(' ');
            builder.Append(argument.Required ? '<' : '[');
            builder.Append(argument.Value);
            builder.Append(argument.Required ? '>' : ']');
        }

        if (command.Parameters.OfType<ICommandOption>().Any(option => !option.IsHidden))
        {
            builder.Append(" [options]");
        }

        if (command.IsBranch && command.Commands.Any(child => !child.IsHidden))
        {
            builder.Append(command.DefaultCommand is null ? " <command>" : " [command]");
        }

        builder.AppendLine();
        builder.AppendLine("```");

        AppendExamples(builder, applicationName, command.Examples);
        AppendArguments(builder, command.Parameters.OfType<ICommandArgument>());
        AppendOptions(builder, command.Parameters.OfType<ICommandOption>());

        if (SkillContent.TryReadCommand(fullPath, out var guidance))
        {
            builder.AppendLine();
            builder.Append(guidance.Trim());
            builder.AppendLine();
        }

        foreach (var child in command.Commands.Where(child => !child.IsHidden))
        {
            AppendCommand(builder, applicationName, child, path);
        }
    }

    private static void AppendExamples(
        StringBuilder builder,
        string applicationName,
        IReadOnlyList<string[]> examples)
    {
        if (examples.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("#### Examples");
        builder.AppendLine();
        builder.AppendLine("```bash");
        foreach (var example in examples)
        {
            builder.Append(applicationName);
            foreach (var token in example)
            {
                builder.Append(' ');
                builder.Append(QuoteShellToken(token));
            }
            builder.AppendLine();
        }
        builder.AppendLine("```");
    }

    private static void AppendArguments(
        StringBuilder builder,
        IEnumerable<ICommandArgument> arguments)
    {
        var visible = arguments
            .Where(argument => !argument.IsHidden)
            .OrderBy(argument => argument.Position)
            .ToArray();
        if (visible.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("#### Arguments");
        builder.AppendLine();
        builder.AppendLine("| Argument | Required | Description |");
        builder.AppendLine("|---|---|---|");
        foreach (var argument in visible)
        {
            builder.Append("| `");
            builder.Append(EscapeMarkdown(argument.Value));
            builder.Append("` | ");
            builder.Append(argument.Required ? "yes" : "no");
            builder.Append(" | ");
            builder.Append(EscapeMarkdownText(argument.Description ?? string.Empty));
            builder.AppendLine(" |");
        }
    }

    private static void AppendOptions(
        StringBuilder builder,
        IEnumerable<ICommandOption> options)
    {
        var visible = options.Where(option => !option.IsHidden).ToArray();
        if (visible.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("#### Options");
        builder.AppendLine();
        builder.AppendLine("| Option | Required | Default | Description |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var option in visible)
        {
            builder.Append("| `");
            builder.Append(EscapeMarkdown(FormatOption(option)));
            builder.Append("` | ");
            builder.Append(option.Required ? "yes" : "no");
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(FormatDefault(option)));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownText(option.Description ?? string.Empty));
            builder.AppendLine(" |");
        }
    }

    private static string FormatOption(ICommandOption option)
    {
        var names = option.ShortNames
            .Select(name => $"-{name}")
            .Concat(option.LongNames.Select(name => $"--{name}"));
        var result = string.Join(", ", names);

        if (!string.IsNullOrWhiteSpace(option.ValueName))
        {
            var value = option.ValueIsOptional
                ? $"[{option.ValueName}]"
                : $"<{option.ValueName}>";
            result = $"{result} {value}";
        }

        return result;
    }

    private static string FormatDefault(ICommandOption option)
    {
        var value = option.DefaultValue?.Value;
        if (value is null || option.IsFlag && value is false)
        {
            return string.Empty;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static string EscapeMarkdown(string value) =>
        value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeMarkdownText(string value) =>
        EscapeMarkdown(value)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string QuoteShellToken(string token)
    {
        if (token.Length > 0
            && token.All(character =>
                char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.' or '/' or ',' or ':'))
        {
            return token;
        }

        return $"'{token.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }
}
