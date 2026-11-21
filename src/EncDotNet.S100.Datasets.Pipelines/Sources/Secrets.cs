namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Marks a source option that holds a secret, such as an API key (design note
/// D7). A host masks it, stores its value in a secret store rather than in its
/// settings file, and keeps only a <see cref="SecretReference"/> in the
/// options.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SecretAttribute : Attribute
{
    /// <summary>
    /// The environment variable that supplies the secret ahead of the stored
    /// value, such as <c>ENCDOTNET_AIS_STREAM_KEY</c>. Hosts without a secret
    /// store, such as <c>s100</c> and the MCP server, read only this.
    /// </summary>
    public string? EnvironmentVariable { get; init; }
}

/// <summary>
/// A reference to a secret kept in a host's secret store:
/// <c>sc-secret:</c> and the secret's name, such as
/// <c>sc-secret:ais.aisstream.apiKey</c>.
/// </summary>
public static class SecretReference
{
    /// <summary>The prefix of a reference.</summary>
    public const string Prefix = "sc-secret:";

    /// <summary>Whether <paramref name="value"/> is a reference rather than a secret's value.</summary>
    /// <param name="value">An option's value.</param>
    public static bool IsReference(string? value) =>
        value is not null
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.Length > Prefix.Length;

    /// <summary>The reference to the secret named <paramref name="name"/>.</summary>
    /// <param name="name">The secret's name, such as <c>ais.aisstream.apiKey</c>.</param>
    public static string For(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Prefix + name;
    }

    /// <summary>The secret's name in <paramref name="reference"/>, or <see langword="null"/> when it isn't a reference.</summary>
    /// <param name="reference">A reference.</param>
    public static string? NameOf(string? reference) =>
        IsReference(reference) ? reference![Prefix.Length..] : null;
}
