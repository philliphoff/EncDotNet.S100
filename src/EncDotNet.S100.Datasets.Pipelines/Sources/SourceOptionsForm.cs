using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Marks an option a host shows on a source's settings page (data source
/// design note D8), with its label. Options without it keep their defaults.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SourceOptionAttribute(string label) : Attribute
{
    /// <summary>The option's label, such as <c>"API key"</c>.</summary>
    public string Label { get; } = label;

    /// <summary>One line on what the option does, for a tooltip or hint. Optional.</summary>
    public string? Description { get; init; }

    /// <summary>Whether a source can't run without a value. Defaults to <see langword="false"/>.</summary>
    public bool Required { get; init; }

    /// <summary>Where the option goes among the others; lower first, then in declaration order.</summary>
    public int Order { get; init; }
}

/// <summary>How a host edits a <see cref="SourceOptionField"/>.</summary>
public enum SourceOptionKind
{
    /// <summary>A line of text.</summary>
    Text,

    /// <summary>A secret, such as an API key: masked, and kept in a secret store (D7).</summary>
    Secret,

    /// <summary>On or off.</summary>
    Boolean,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A length of time, written as <c>[d.]hh:mm[:ss]</c>.</summary>
    Duration,

    /// <summary>An absolute URL.</summary>
    Url,

    /// <summary>One of <see cref="SourceOptionField.Choices"/>.</summary>
    Choice,
}

/// <summary>An option on a source's settings page.</summary>
/// <param name="Name">The options record's property name, such as <c>ApiKey</c>.</param>
/// <param name="Label">What the page calls it.</param>
/// <param name="Description">One line on what it does, or <see langword="null"/>.</param>
/// <param name="Kind">How it's edited.</param>
/// <param name="Required">Whether the source can't run without a value.</param>
/// <param name="EnvironmentVariable">For a <see cref="SourceOptionKind.Secret"/>, the environment variable that supplies it ahead of the stored value.</param>
/// <param name="Choices">For a <see cref="SourceOptionKind.Choice"/>, the values it can take.</param>
public sealed record SourceOptionField(
    string Name,
    string Label,
    string? Description,
    SourceOptionKind Kind,
    bool Required,
    string? EnvironmentVariable,
    IReadOnlyList<string> Choices);

/// <summary>
/// A source's settings page, from its <see cref="SourceRegistration.OptionsType"/>
/// (design note D8): the options marked <see cref="SourceOptionAttribute"/>,
/// read as and written from invariant text, so a host can save them without
/// knowing the type.
/// </summary>
public static class SourceOptionsForm
{
    /// <summary>The options a host shows for <paramref name="registration"/>, in order; empty for a source with none.</summary>
    /// <param name="registration">The source.</param>
    public static IReadOnlyList<SourceOptionField> Fields(SourceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.OptionsType is not { } type)
            return [];
        return [.. Shown(type).Select(p => FieldOf(p.Property, p.Option))];
    }

    /// <summary>
    /// The shown options' values in <paramref name="options"/> (or the
    /// registration's defaults), as invariant text; an option with no value is left out.
    /// </summary>
    /// <param name="registration">The source.</param>
    /// <param name="options">An instance of its options type, or <see langword="null"/> for its defaults.</param>
    public static IReadOnlyDictionary<string, string> Read(SourceRegistration registration, object? options)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (registration.OptionsType is not { } type)
            return values;
        options ??= registration.DefaultOptions;
        if (options is null)
            return values;
        foreach (var (property, _) in Shown(type))
        {
            if (Format(property.GetValue(options)) is { Length: > 0 } text)
                values[property.Name] = text;
        }
        return values;
    }

    /// <summary>
    /// Builds the source's options: its defaults, with each shown option in
    /// <paramref name="values"/> set from its text. Options not shown, and
    /// names it doesn't know, are ignored.
    /// </summary>
    /// <param name="registration">The source.</param>
    /// <param name="values">Option values by property name, as <see cref="Read"/> gives them; <see langword="null"/> for the defaults.</param>
    /// <returns>The options, or <see langword="null"/> for a source with no options type.</returns>
    /// <exception cref="FormatException">A value isn't valid for its option; the message names the option.</exception>
    public static object? Build(SourceRegistration registration, IReadOnlyDictionary<string, string>? values)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.OptionsType is not { } type)
            return null;
        if (values is null || values.Count == 0)
            return registration.DefaultOptions ?? Activator.CreateInstance(type);

        var options = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"{type.Name} has no parameterless constructor.");
        if (registration.DefaultOptions is { } defaults && defaults.GetType() == type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                    property.SetValue(options, property.GetValue(defaults));
            }
        }

        foreach (var (property, option) in Shown(type))
        {
            if (!values.TryGetValue(property.Name, out var text))
                continue;
            try
            {
                property.SetValue(options, Parse(property.PropertyType, text));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or UriFormatException)
            {
                throw new FormatException($"'{text}' isn't a valid {option.Label}.", ex);
            }
        }
        return options;
    }

    /// <summary>
    /// The first required option of <paramref name="registration"/> that
    /// <paramref name="values"/> leaves unset, or <see langword="null"/> when
    /// the source can start. A secret counts as set when its environment
    /// variable is (D7).
    /// </summary>
    /// <param name="registration">The source.</param>
    /// <param name="values">Its option values; <see langword="null"/> for its defaults.</param>
    /// <param name="environment">Reads an environment variable; <see langword="null"/> for the process's.</param>
    public static SourceOptionField? MissingRequired(
        SourceRegistration registration,
        IReadOnlyDictionary<string, string>? values,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        environment ??= Environment.GetEnvironmentVariable;
        values ??= Read(registration, null);
        foreach (var field in Fields(registration))
        {
            if (!field.Required || (values.TryGetValue(field.Name, out var text) && !string.IsNullOrWhiteSpace(text)))
                continue;
            if (field.EnvironmentVariable is { } variable && !string.IsNullOrWhiteSpace(environment(variable)))
                continue;
            return field;
        }
        return null;
    }

    /// <summary>Whether <paramref name="text"/> is a valid value of <paramref name="field"/>; empty is valid unless it's required.</summary>
    /// <param name="field">The option.</param>
    /// <param name="text">Its text.</param>
    public static bool IsValid(SourceOptionField field, string? text)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(text))
            return !field.Required;
        var culture = CultureInfo.InvariantCulture;
        return field.Kind switch
        {
            SourceOptionKind.Boolean => bool.TryParse(text, out _),
            SourceOptionKind.Integer => long.TryParse(text, NumberStyles.Integer, culture, out _),
            SourceOptionKind.Number => double.TryParse(text, NumberStyles.Float, culture, out var d) && double.IsFinite(d),
            SourceOptionKind.Duration => TimeSpan.TryParse(text, culture, out var t) && t >= TimeSpan.Zero,
            SourceOptionKind.Url => Uri.TryCreate(text, UriKind.Absolute, out _),
            SourceOptionKind.Choice => field.Choices.Contains(text, StringComparer.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static IEnumerable<(PropertyInfo Property, SourceOptionAttribute Option)> Shown(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select((p, index) => (Property: p, Option: p.GetCustomAttribute<SourceOptionAttribute>(), Index: index))
            .Where(p => p.Option is not null && p.Property.CanRead && p.Property.CanWrite && IsSupported(p.Property.PropertyType))
            .OrderBy(p => p.Option!.Order)
            .ThenBy(p => p.Index)
            .Select(p => (p.Property, p.Option!));

    private static bool IsSupported(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long)
            || type == typeof(double) || type == typeof(TimeSpan) || type == typeof(Uri) || type.IsEnum;
    }

    private static SourceOptionField FieldOf(PropertyInfo property, SourceOptionAttribute option)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var secret = property.GetCustomAttribute<SecretAttribute>();
        var kind = secret is not null ? SourceOptionKind.Secret
            : type == typeof(bool) ? SourceOptionKind.Boolean
            : type == typeof(int) || type == typeof(long) ? SourceOptionKind.Integer
            : type == typeof(double) ? SourceOptionKind.Number
            : type == typeof(TimeSpan) ? SourceOptionKind.Duration
            : type == typeof(Uri) ? SourceOptionKind.Url
            : type.IsEnum ? SourceOptionKind.Choice
            : SourceOptionKind.Text;
        return new SourceOptionField(
            property.Name,
            option.Label,
            option.Description,
            kind,
            option.Required,
            secret?.EnvironmentVariable,
            type.IsEnum ? Enum.GetNames(type) : []);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        Uri u => u.OriginalString,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Enum.Parse on an option's enum type, which the options type's public properties keep.")]
    private static object? Parse(Type type, string text)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (underlying is not null || !type.IsValueType)
                return type == typeof(string) ? string.Empty : null;
            throw new FormatException("A value is needed.");
        }
        var target = underlying ?? type;
        var culture = CultureInfo.InvariantCulture;
        text = text.Trim();
        if (target == typeof(string))
            return text;
        if (target == typeof(bool))
            return bool.Parse(text);
        if (target == typeof(int))
            return int.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(long))
            return long.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(double))
        {
            var d = double.Parse(text, NumberStyles.Float, culture);
            return double.IsFinite(d) ? d : throw new FormatException("Not a finite number.");
        }
        if (target == typeof(TimeSpan))
            return TimeSpan.Parse(text, culture);
        if (target == typeof(Uri))
            return new Uri(text, UriKind.Absolute);
        if (target.IsEnum)
            return Enum.Parse(target, text, ignoreCase: true);
        throw new FormatException($"{target.Name} options aren't supported.");
    }
}
