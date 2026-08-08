using System.Reflection;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Lazily initialises and caches the shared infrastructure (catalogue
/// manager, Lua engine, CRS factory, pipeline factory) so scenarios can
/// share it across iterations without paying repeated start-up costs.
/// </summary>
internal static class SharedInfrastructure
{
    private static readonly Lazy<PortrayalCatalogueManager> LazyCatalogueManager = new(CreateCatalogueManager);
    private static readonly Lazy<MoonSharpLuaEngine> LazyLuaEngine = new(() => new MoonSharpLuaEngine());
    private static readonly Lazy<ProjNetCrsTransformFactory> LazyCrsFactory = new(() => new ProjNetCrsTransformFactory());
    private static readonly Lazy<FeatureCatalogueManager> LazyFeatureCatalogueManager =
        new(() => new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

    public static PortrayalCatalogueManager CatalogueManager => LazyCatalogueManager.Value;
    public static MoonSharpLuaEngine LuaEngine => LazyLuaEngine.Value;
    public static ProjNetCrsTransformFactory CrsFactory => LazyCrsFactory.Value;
    public static FeatureCatalogueManager FeatureCatalogueManager =>
        LazyFeatureCatalogueManager.Value;

    public static Datasets.Pipelines.DatasetPipelineFactory CreatePipelineFactory()
    {
        var factoryType = typeof(Datasets.Pipelines.DatasetPipelineFactory);
        var pipelinesAssembly = factoryType.Assembly;

        // The renderer-neutral S-98 interoperability types keep the
        // EncDotNet.S100.Datasets.Pipelines.Interoperability namespace but, as of
        // issue #512 step 9, physically live in EncDotNet.S100.Core rather than
        // the Datasets.Pipelines assembly. Resolve interop type names from either
        // assembly so this reflection probe works against both the current
        // library and older base-SHA binaries that still ship them in
        // Datasets.Pipelines (perf-gate base comparison).
        var coreAssembly = typeof(ICrsTransformFactory).Assembly;
        Type? ResolveInteropType(string fullName) =>
            pipelinesAssembly.GetType(fullName, throwOnError: false)
            ?? coreAssembly.GetType(fullName, throwOnError: false);

        // Newest shape (issue #189 PR2): the Mapsui-free factory takes an
        // IDisplayPlaneAuthorityProvider in place of the former
        // IInteroperabilityAuthorityProvider (which moved to the Mapsui package).
        //
        // Note: the live constructor may carry additional *optional* trailing
        // parameters (e.g. a shared portrayal-instruction cache). We match by
        // leading prefix and let default values fill the rest, so this probe
        // survives future optional-parameter additions instead of throwing
        // MissingMethodException (see issue #491).
        var displayPlaneProviderType = ResolveInteropType("EncDotNet.S100.Datasets.Pipelines.Interoperability.IDisplayPlaneAuthorityProvider");
        if (displayPlaneProviderType is not null)
        {
            var displayPlaneCtor = FindConstructorMatchingPrefix(
                factoryType,
                [
                    typeof(PortrayalCatalogueManager),
                    typeof(ILuaEngine),
                    typeof(ICrsTransformFactory),
                    typeof(FeatureCatalogueManager),
                    displayPlaneProviderType,
                ]);
            var displayPlaneImplType = ResolveInteropType("EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider");
            if (displayPlaneCtor is not null && displayPlaneImplType is not null)
            {
                var displayPlaneProvider = Activator.CreateInstance(displayPlaneImplType)!;
                return (Datasets.Pipelines.DatasetPipelineFactory)InvokeWithDefaults(
                    displayPlaneCtor,
                    [CatalogueManager, LuaEngine, CrsFactory, FeatureCatalogueManager, displayPlaneProvider]);
            }
        }

        // Prior shape (issue #189 PR1): adds IInteroperabilityAuthorityProvider.
        // Resolved via reflection so this tooling stays compatible with base
        // SHA library binaries that do not yet expose the Interoperability
        // namespace.
        var providerInterfaceType = ResolveInteropType("EncDotNet.S100.Datasets.Pipelines.Interoperability.IInteroperabilityAuthorityProvider");
        if (providerInterfaceType is not null)
        {
            var providerCtor = FindConstructorMatchingPrefix(
                factoryType,
                [
                    typeof(PortrayalCatalogueManager),
                    typeof(ILuaEngine),
                    typeof(ICrsTransformFactory),
                    typeof(FeatureCatalogueManager),
                    providerInterfaceType,
                ]);
            if (providerCtor is not null)
            {
                var authorityType = ResolveInteropType("EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority");
                var providerImplType = ResolveInteropType("EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider");
                if (authorityType is not null && providerImplType is not null)
                {
                    var authority = Activator.CreateInstance(authorityType)!;
                    var provider = Activator.CreateInstance(providerImplType, authority)!;
                    return (Datasets.Pipelines.DatasetPipelineFactory)InvokeWithDefaults(
                        providerCtor,
                        [CatalogueManager, LuaEngine, CrsFactory, FeatureCatalogueManager, provider]);
                }
            }
        }

        var managerCtor = FindConstructorMatchingPrefix(
            factoryType,
            [
                typeof(PortrayalCatalogueManager),
                typeof(ILuaEngine),
                typeof(ICrsTransformFactory),
                typeof(FeatureCatalogueManager),
            ]);
        if (managerCtor is not null)
        {
            return (Datasets.Pipelines.DatasetPipelineFactory)InvokeWithDefaults(
                managerCtor,
                [CatalogueManager, LuaEngine, CrsFactory, FeatureCatalogueManager]);
        }

        var resolverCtor = FindConstructorMatchingPrefix(
            factoryType,
            [
                typeof(PortrayalCatalogueManager),
                typeof(ILuaEngine),
                typeof(ICrsTransformFactory),
                typeof(Func<string, Stream?>),
            ]);
        if (resolverCtor is not null)
        {
            Func<string, Stream?> resolver = Specification.TryOpenFeatureCatalogue;
            return (Datasets.Pipelines.DatasetPipelineFactory)InvokeWithDefaults(
                resolverCtor,
                [CatalogueManager, LuaEngine, CrsFactory, resolver]);
        }

        throw new MissingMethodException(
            nameof(Datasets.Pipelines.DatasetPipelineFactory),
            ".ctor(PortrayalCatalogueManager, ILuaEngine, ICrsTransformFactory, ...)");
    }

    /// <summary>
    /// Finds a constructor on <paramref name="type"/> whose leading parameter
    /// types match <paramref name="requiredLeadingTypes"/> exactly. Additional
    /// trailing parameters are permitted only when each has a compile-time
    /// default value (i.e. <see cref="ParameterInfo.HasDefaultValue"/> is
    /// <see langword="true"/>).
    /// </summary>
    /// <remarks>
    /// This is the tolerant replacement for <see cref="Type.GetConstructor(Type[])"/>,
    /// which requires an exact arity/type match and therefore misses constructors
    /// that gain optional trailing parameters over time (issue #491).
    /// </remarks>
    internal static ConstructorInfo? FindConstructorMatchingPrefix(
        Type type,
        IReadOnlyList<Type> requiredLeadingTypes)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(requiredLeadingTypes);

        foreach (var ctor in type.GetConstructors())
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length < requiredLeadingTypes.Count)
            {
                continue;
            }

            var prefixMatches = true;
            for (var i = 0; i < requiredLeadingTypes.Count; i++)
            {
                if (parameters[i].ParameterType != requiredLeadingTypes[i])
                {
                    prefixMatches = false;
                    break;
                }
            }
            if (!prefixMatches)
            {
                continue;
            }

            var trailingAllOptional = true;
            for (var i = requiredLeadingTypes.Count; i < parameters.Length; i++)
            {
                if (!parameters[i].HasDefaultValue)
                {
                    trailingAllOptional = false;
                    break;
                }
            }
            if (!trailingAllOptional)
            {
                continue;
            }

            return ctor;
        }

        return null;
    }

    /// <summary>
    /// Invokes <paramref name="ctor"/> supplying <paramref name="providedArgs"/>
    /// for the leading parameters and each trailing parameter's compile-time
    /// default value for the remainder.
    /// </summary>
    internal static object InvokeWithDefaults(ConstructorInfo ctor, object?[] providedArgs)
    {
        ArgumentNullException.ThrowIfNull(ctor);
        ArgumentNullException.ThrowIfNull(providedArgs);

        var parameters = ctor.GetParameters();
        if (providedArgs.Length > parameters.Length)
        {
            throw new ArgumentException(
                $"Provided {providedArgs.Length} arguments for a constructor that takes {parameters.Length}.",
                nameof(providedArgs));
        }

        var args = new object?[parameters.Length];
        Array.Copy(providedArgs, args, providedArgs.Length);
        for (var i = providedArgs.Length; i < parameters.Length; i++)
        {
            if (!parameters[i].HasDefaultValue)
            {
                throw new ArgumentException(
                    $"Constructor parameter '{parameters[i].Name}' at position {i} has no default value.",
                    nameof(providedArgs));
            }
            args[i] = parameters[i].DefaultValue;
        }

        return ctor.Invoke(args);
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                var source = Specification.CreatePortrayalCatalogueSource(spec);
                manager.SetSource(spec, source);
            }
        }
        return manager;
    }
}
