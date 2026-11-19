using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The <see cref="SourceRegistration"/>s a host offers, by id. The sibling of
/// <see cref="ProductRegistry"/>.
/// </summary>
/// <remarks>
/// Register sources at start-up. The process-wide <see cref="Default"/>
/// registry holds the built-in sources (<see cref="S100Sources"/>).
/// </remarks>
public sealed class SourceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceRegistration> _byId = new(StringComparer.OrdinalIgnoreCase);
    private SourceRegistration[] _snapshot = [];

    /// <summary>The process-wide registry, pre-loaded with the built-in sources.</summary>
    public static SourceRegistry Default { get; } = CreateDefault();

    /// <summary>Creates a registry with the built-in sources.</summary>
    public static SourceRegistry CreateDefault()
    {
        var registry = new SourceRegistry();
        registry.Register(S100Sources.NdbcObservations);
        registry.Register(S100Sources.NmeaWeather);
        registry.Register(S100Sources.AisStream);
        return registry;
    }

    /// <summary>The registered sources, in registration order.</summary>
    public IReadOnlyList<SourceRegistration> Registrations => Volatile.Read(ref _snapshot);

    /// <summary>Registers a source.</summary>
    /// <param name="registration">The source.</param>
    /// <exception cref="ArgumentException">The source's id is empty or already registered.</exception>
    public void Register(SourceRegistration registration) => Register(registration, replace: false);

    /// <summary>
    /// Registers <paramref name="registration"/>, or with
    /// <paramref name="replace"/> puts it in place of the one with its id,
    /// keeping its position, as a host does to wrap a built-in source.
    /// </summary>
    /// <param name="registration">The source.</param>
    /// <param name="replace">Whether to replace a source with the same id instead of refusing it.</param>
    /// <exception cref="ArgumentException">A source with the same id is registered and <paramref name="replace"/> is <see langword="false"/>.</exception>
    public void Register(SourceRegistration registration, bool replace)
    {
        if (replace)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.Spec);
            lock (_gate)
            {
                if (_byId.TryGetValue(registration.Id, out var existing))
                {
                    _byId[registration.Id] = registration;
                    _snapshot = [.. _snapshot.Select(r => ReferenceEquals(r, existing) ? registration : r)];
                    return;
                }
            }
        }

        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Spec);
        lock (_gate)
        {
            if (!_byId.TryAdd(registration.Id, registration))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "The source '{0}' is already registered.", registration.Id),
                    nameof(registration));
            }
            _snapshot = [.. _snapshot, registration];
        }
    }

    /// <summary>Finds the source with id <paramref name="id"/>, ignoring case.</summary>
    /// <param name="id">The source's id.</param>
    /// <param name="registration">The source, when it's registered.</param>
    public bool TryResolve(string id, [NotNullWhen(true)] out SourceRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
            return _byId.TryGetValue(id, out registration);
    }

    /// <summary>The source with id <paramref name="id"/>.</summary>
    /// <param name="id">The source's id.</param>
    /// <exception cref="NotSupportedException">No source has that id.</exception>
    public SourceRegistration Resolve(string id) =>
        TryResolve(id, out var registration)
            ? registration
            : throw new NotSupportedException(
                string.Format(CultureInfo.InvariantCulture, "No source is registered with id '{0}'.", id));

    /// <summary>
    /// Builds the processor of the source with id <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The source's id.</param>
    /// <param name="context">The options and services for the source.</param>
    /// <exception cref="NotSupportedException">No source has that id.</exception>
    public IDatasetProcessor Create(string id, SourceCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var registration = Resolve(id);
        return registration.Create(context with { Options = context.Options ?? registration.DefaultOptions });
    }
}
