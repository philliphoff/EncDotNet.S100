using System.Globalization;
using System.Text.Json;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Reads a tool's <c>spec</c> argument in either shape an agent sends:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>a string, e.g. <c>"S-101"</c> or <c>"S-124/1.5.0"</c>;</description></item>
///   <item><description>the object the tools return in their results, e.g.
///   <c>{"name":"S-101","edition":{"major":2,"minor":0,"clarification":0}}</c>,
///   where <c>edition</c> is optional and may also be a <c>"M.m.c"</c> string.</description></item>
/// </list>
/// <para>
/// Results carry <c>spec</c> as an object, so agents naturally pass that
/// object straight back into the next call (issue #317). Both shapes are
/// reduced to the string form, which each tool then parses as before.
/// </para>
/// </remarks>
internal static class SpecArgumentReader
{
    /// <summary>
    /// Returns the string form of <paramref name="spec"/>, or
    /// <see langword="null"/> when it is absent, <c>null</c>, or blank.
    /// Throws <see cref="ArgumentException"/> naming <c>spec</c> for any
    /// other shape, which the tool wrappers map to <c>invalid_argument</c>.
    /// </summary>
    public static string? ReadText(JsonElement? spec)
    {
        if (spec is not { } element)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Object => ReadObject(element),
            _ => throw Invalid($"got a JSON {element.ValueKind.ToString().ToLowerInvariant()}."),
        };
    }

    private static string ReadObject(JsonElement element)
    {
        if (!TryGetProperty(element, "name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            throw Invalid("the object has no string \"name\".");
        }

        var name = nameElement.GetString()!.Trim();
        if (!TryGetProperty(element, "edition", out var edition))
        {
            return name;
        }

        return edition.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => name,
            JsonValueKind.String when string.IsNullOrWhiteSpace(edition.GetString()) => name,
            JsonValueKind.String => $"{name}/{edition.GetString()!.Trim()}",
            JsonValueKind.Object => WithEdition(name, edition),
            _ => throw Invalid("\"edition\" must be an object {\"major\",\"minor\",\"clarification\"} or a \"M.m.c\" string."),
        };
    }

    private static string WithEdition(string name, JsonElement edition)
    {
        var major = ReadPart(edition, "major");
        var minor = ReadPart(edition, "minor");
        var clarification = ReadPart(edition, "clarification");

        // The results serialise an edition-agnostic spec as 0.0.0.
        return major == 0 && minor == 0 && clarification == 0
            ? name
            : string.Create(CultureInfo.InvariantCulture, $"{name}/{major}.{minor}.{clarification}");
    }

    private static int ReadPart(JsonElement edition, string part)
    {
        if (!TryGetProperty(edition, part, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
        {
            throw Invalid($"\"edition.{part}\" must be a non-negative integer.");
        }

        return number;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ArgumentException Invalid(string detail)
        => new(
            "spec must be a string such as \"S-101\" or \"S-124/1.5.0\", or an object such as "
            + "{\"name\":\"S-101\",\"edition\":{\"major\":2,\"minor\":0,\"clarification\":0}} (edition optional); "
            + detail,
            "spec");
}
