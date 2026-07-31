namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Loads embedded agent guidance that supplements generated command metadata.
/// </summary>
internal static class SkillContent
{
    private const string ResourcePrefix = "EncDotNet.S100.Cli.Skill.";

    private static readonly IReadOnlyDictionary<string, string> CommandResources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["render"] = "Commands.Render.md",
            ["validate"] = "Commands.Validate.md",
            ["info"] = "Commands.Info.md",
            ["identify"] = "Commands.Identify.md",
            ["list-specs"] = "Commands.ListSpecs.md",
            ["s57 convert"] = "Commands.S57Convert.md",
        };

    public static IReadOnlyCollection<string> GuidedCommandPaths { get; } =
        CommandResources.Keys.ToArray();

    public static string Read(string resourceName)
    {
        var assembly = typeof(SkillContent).Assembly;
        var fullName = ResourcePrefix + resourceName;
        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded skill resource not found: {fullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static bool TryReadCommand(string commandPath, out string guidance)
    {
        if (CommandResources.TryGetValue(commandPath, out var resourceName))
        {
            guidance = Read(resourceName);
            return true;
        }

        guidance = string.Empty;
        return false;
    }
}
