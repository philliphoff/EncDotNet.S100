namespace EncDotNet.S100.Portrayals;

/// <summary>
/// An S-100 Part 9 context parameter: a named, typed input to the portrayal
/// rules (e.g. safety contour, shallow-contour depth, symbol style) whose value
/// the mariner or host application may override. Declared in the catalogue's
/// <c>context</c> section as <c>parameter</c> elements.
/// </summary>
public sealed class ContextParameter
{
    /// <summary>
    /// Parameter identifier, from <c>parameter/@id</c> (e.g. <c>SafetyContour</c>);
    /// the name the rules use to look the value up.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The parameter's <c>description</c> block.</summary>
    public required Description Description { get; init; }

    /// <summary>
    /// Value type, from <c>parameter/type</c> as written in the catalogue
    /// (e.g. <c>Boolean</c>, <c>Double</c>, <c>String</c>).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Default value in its textual form, from <c>parameter/default</c>; used
    /// when no override is supplied.
    /// </summary>
    public required string Default { get; init; }

    /// <summary>
    /// Optional XPath condition, from <c>parameter/@enable</c>, over other
    /// parameters' values that determines whether this parameter applies
    /// (e.g. <c>//FourShades[1]='true'</c>); <see langword="null"/> when absent.
    /// Captured as declared; not evaluated by this library.
    /// </summary>
    public string? Enable { get; init; }

    /// <summary>
    /// Value constraints from <c>parameter/validate</c>, or
    /// <see langword="null"/> when the parameter declares none.
    /// </summary>
    public ContextParameterValidation? Validation { get; init; }
}
