using EncDotNet.S100.Datasets.Pipelines;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>A live source built from an id and option text (data source design note D9).</summary>
/// <param name="Registration">The source's registration.</param>
/// <param name="Options">Its options, built from the defaults and the given values.</param>
/// <param name="Processor">Its processor, not yet started.</param>
internal sealed record CreatedLiveSource(SourceRegistration Registration, object? Options, IDatasetProcessor Processor);

/// <summary>Why a live source couldn't be built.</summary>
/// <param name="Parameter">What was wrong: <c>id</c> or <c>options</c>.</param>
/// <param name="Reason">Why, in words for the user.</param>
internal sealed record LiveSourceProblem(string Parameter, string Reason);

/// <summary>
/// Builds a live source for <c>s100</c> and its MCP server (data source design
/// note D9): checks the id and options, refuses a secret given as an argument
/// (D7: it comes from its environment variable), says what's missing, and
/// creates the processor with <see cref="HeadlessSources.Context"/>.
/// </summary>
internal static class LiveSourceFactory
{
    /// <summary>Builds the source, or says why not.</summary>
    /// <param name="registry">The sources on offer.</param>
    /// <param name="id">The source's id.</param>
    /// <param name="options">Option values by name, as text; <see langword="null"/> for the defaults.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="loggerFactory">The loggers, or <see langword="null"/>.</param>
    /// <param name="problem">Why it couldn't be built.</param>
    public static CreatedLiveSource? TryCreate(
        SourceRegistry registry,
        string id,
        IReadOnlyDictionary<string, string>? options,
        Func<string, string?> environment,
        ILoggerFactory? loggerFactory,
        out LiveSourceProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        problem = null;
        if (string.IsNullOrWhiteSpace(id) || !registry.TryResolve(id, out var registration))
        {
            problem = new("id", $"no source '{id}'; 's100 sources' and list_data_sources list them");
            return null;
        }

        var fields = SourceOptionsForm.Fields(registration);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in options ?? new Dictionary<string, string>())
        {
            var field = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                problem = new("options", $"'{registration.DisplayName}' has no option '{name}'; it takes {Names(fields)}");
                return null;
            }
            // D7, D9: a secret never travels as an argument.
            if (field.Kind == SourceOptionKind.Secret)
            {
                problem = new("options", $"{field.Label} isn't accepted as an argument: set the {field.EnvironmentVariable} environment variable instead");
                return null;
            }
            values[field.Name] = value;
        }

        var given = values.Count == 0 ? null : values;
        if (SourceOptionsForm.MissingRequired(registration, given, environment) is { } missing)
        {
            problem = new("id", Needs(registration, missing));
            return null;
        }

        object? built;
        try
        {
            built = SourceOptionsForm.Build(registration, given);
        }
        catch (FormatException ex)
        {
            problem = new("options", ex.Message);
            return null;
        }

        try
        {
            var processor = registration.Create(HeadlessSources.Context(built, loggerFactory));
            return new CreatedLiveSource(registration, built, processor);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            problem = new("id", $"'{registration.DisplayName}' couldn't start: {ex.Message}");
            return null;
        }
    }

    /// <summary>What <paramref name="registration"/> needs before it can start, in words for the user.</summary>
    public static string Needs(SourceRegistration registration, SourceOptionField missing) =>
        missing.Kind == SourceOptionKind.Secret && missing.EnvironmentVariable is { } variable
            ? $"{registration.DisplayName} needs {missing.Label}: set the {variable} environment variable"
            : $"{registration.DisplayName} needs the option '{missing.Name}' ({missing.Label})";

    private static string Names(IReadOnlyList<SourceOptionField> fields) =>
        fields.Count == 0 ? "no options" : string.Join(", ", fields.Select(f => f.Name));
}
