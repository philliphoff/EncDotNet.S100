using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// What a host gives <see cref="SourceRegistration.Create"/>: the source's
/// saved options and the services a source needs, so no source builds its own
/// HTTP client or reads its own settings.
/// </summary>
public sealed record SourceCreationContext
{
    /// <summary>
    /// The options the host saved for this dataset entry, an instance of
    /// <see cref="SourceRegistration.OptionsType"/>, or <see langword="null"/>
    /// for the registration's <see cref="SourceRegistration.DefaultOptions"/>.
    /// </summary>
    public object? Options { get; init; }

    /// <summary>The loggers the source writes to. Defaults to none.</summary>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    /// <summary>The clock the source runs on. Defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Creates an <see cref="HttpClient"/> for the source, which the source's
    /// processor owns and disposes. Hosts supply one with their hardened TLS
    /// handler and their user agent; the default is a plain client.
    /// </summary>
    public Func<HttpClient> CreateHttpClient { get; init; } = static () => new HttpClient();

    /// <summary>
    /// Reads a secret from the host's secret store by name (design note D7),
    /// or <see langword="null"/> when the host has no store or the secret
    /// isn't there. Hosts such as <c>s100</c> leave it unset and rely on
    /// environment variables.
    /// </summary>
    public Func<string, string?>? ReadSecret { get; init; }

    /// <summary>
    /// The secret for an option marked <see cref="SecretAttribute"/>: the
    /// environment variable when it's set, else the stored secret that
    /// <paramref name="value"/> refers to, else <paramref name="value"/>
    /// itself when it isn't a <see cref="SecretReference"/> (a key saved in
    /// plain text before the host had a store).
    /// </summary>
    /// <param name="value">The option's value: a reference, a plain value or <see langword="null"/>.</param>
    /// <param name="environmentVariable">The environment variable that supplies the secret first.</param>
    /// <returns>The secret, or <see langword="null"/> when there's none.</returns>
    public string? ResolveSecret(string? value, string? environmentVariable = null)
    {
        if (!string.IsNullOrWhiteSpace(environmentVariable)
            && Environment.GetEnvironmentVariable(environmentVariable) is { Length: > 0 } fromEnvironment
            && !string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (SecretReference.NameOf(value) is { } name)
            return ReadSecret?.Invoke(name) is { Length: > 0 } stored ? stored : null;
        return value;
    }

    /// <summary>
    /// The options as <typeparamref name="T"/>: <see cref="Options"/> when
    /// it's set, else <paramref name="defaults"/>.
    /// </summary>
    /// <typeparam name="T">The registration's options type.</typeparam>
    /// <param name="defaults">The options to use when the host saved none.</param>
    /// <exception cref="ArgumentException"><see cref="Options"/> is set but isn't a <typeparamref name="T"/>.</exception>
    public T GetOptions<T>(T defaults)
        where T : class =>
        Options switch
        {
            null => defaults,
            T options => options,
            var other => throw new ArgumentException(
                $"The source's options are a {other.GetType().Name}, not a {typeof(T).Name}.",
                nameof(Options)),
        };
}
