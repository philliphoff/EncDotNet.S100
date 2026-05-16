using System.ComponentModel;
using System.Reflection;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Reflection-based contract test. Every public property of every
/// record type that crosses the MCP wire must carry a non-empty
/// <see cref="DescriptionAttribute"/>. This guards against future
/// drift — when a sibling session adds a new tool, result variant,
/// or error type, this test fails until the new properties are
/// annotated.
/// </summary>
public class AnnotationContractTests
{
    [Fact]
    public void Every_wire_crossing_property_has_a_description()
    {
        var assemblies = new[]
        {
            typeof(ListDatasetsResult).Assembly,    // EncDotNet.S100.Mcp.Tools
            typeof(BoundingBox).Assembly,           // EncDotNet.S100.Core
        };

        var types = new HashSet<Type>();
        foreach (var asm in assemblies)
        {
            foreach (var t in asm.GetExportedTypes())
            {
                if (IsWireType(t))
                {
                    types.Add(t);
                }
            }
        }

        // Sanity: the suite is meaningless if no types were collected.
        Assert.True(types.Count >= 10,
            $"AnnotationContractTests collected only {types.Count} wire types; the predicate is too narrow.");

        var failures = new List<string>();
        foreach (var t in types)
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!p.CanRead) continue;
                if (IsRecordSynthetic(p)) continue;

                var desc = p.GetCustomAttribute<DescriptionAttribute>();
                if (desc is null || string.IsNullOrWhiteSpace(desc.Description))
                {
                    failures.Add($"{t.FullName}.{p.Name} is missing a [Description] attribute.");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Wire-crossing properties without [Description]:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    private static bool IsWireType(Type t)
    {
        if (t.IsAbstract && t.IsSealed) return false; // static classes
        if (t.IsInterface) return false;
        if (t.IsEnum) return false;
        if (t.IsGenericTypeDefinition) return false;
        if (t.IsNested) return false; // skip nested types like ToolResult<T>.OkResult

        // ToolError and its subtypes are part of the agent-facing
        // contract (their non-base members surface as the error
        // `details` payload).
        if (typeof(ToolError).IsAssignableFrom(t)) return true;

        // SampledValue and its subtypes are the discriminated payload
        // returned by sample_coverage.
        if (typeof(SampledValue).IsAssignableFrom(t)) return true;

        // Shared coordinate / spec types used across every tool result.
        if (t == typeof(BoundingBox)) return true;
        if (t == typeof(SpecRef) || t == typeof(SpecVersion)) return true;
        if (t == typeof(DatasetId) || t == typeof(TimeRange)) return true;

        // Tool request / result records, plus the supporting summary /
        // reference records they contain. The suffix-based rule is the
        // codebase convention and naturally picks up sibling sessions'
        // future tools (e.g. FindAtRequest / FindAtResult).
        if (t.Namespace == "EncDotNet.S100.Mcp.Tools" && IsRecordType(t))
        {
            var name = t.Name;
            if (name.EndsWith("Request", StringComparison.Ordinal)) return true;
            if (name.EndsWith("Result", StringComparison.Ordinal)) return true;
            if (name.EndsWith("Summary", StringComparison.Ordinal)) return true;
            if (name.EndsWith("Reference", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool IsRecordType(Type t)
    {
        // C# emits a synthetic `EqualityContract` property only on
        // record types; that's the standard reflection-time signal.
        if (t.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null)
        {
            return true;
        }

        // record struct: detect via the compiler-emitted PrintMembers
        // method that all records carry.
        return t.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null
            || t.GetMethod("PrintMembers", BindingFlags.Public | BindingFlags.Instance) is not null;
    }

    private static bool IsRecordSynthetic(PropertyInfo p)
    {
        // `EqualityContract` is the only compiler-synthesised public
        // property on records and is not part of the wire payload.
        return string.Equals(p.Name, "EqualityContract", StringComparison.Ordinal);
    }
}
