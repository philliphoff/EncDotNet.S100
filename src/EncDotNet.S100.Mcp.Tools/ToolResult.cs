using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Discriminated union over a successful tool result of type
/// <typeparamref name="T"/> and a typed <see cref="ToolError"/>.
/// </summary>
/// <remarks>
/// This is a deliberately small, local implementation (no
/// <c>OneOf</c>/<c>Result</c> NuGet dependency). Use
/// <see cref="Ok(T)"/> / <see cref="Err(ToolError)"/> to construct,
/// pattern-match on <see cref="ToolResult{T}.OkResult"/> /
/// <see cref="ToolResult{T}.ErrResult"/> to consume, or call
/// <see cref="TryGetValue"/> / <see cref="TryGetError"/>.
/// </remarks>
public abstract record ToolResult<T>
{
    private ToolResult() { }

    /// <summary>Constructs a successful result.</summary>
    public static ToolResult<T> Ok(T value) => new OkResult(value);

    /// <summary>Constructs a failed result.</summary>
    public static ToolResult<T> Err(ToolError error) => new ErrResult(error);

    /// <summary>Tries to extract the success value.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        if (this is OkResult ok)
        {
            value = ok.Value;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Tries to extract the error.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out ToolError error)
    {
        if (this is ErrResult err)
        {
            error = err.Error;
            return true;
        }
        error = default;
        return false;
    }

    /// <summary>Successful variant.</summary>
    public sealed record OkResult(T Value) : ToolResult<T>;

    /// <summary>Failed variant.</summary>
    public sealed record ErrResult(ToolError Error) : ToolResult<T>;
}
