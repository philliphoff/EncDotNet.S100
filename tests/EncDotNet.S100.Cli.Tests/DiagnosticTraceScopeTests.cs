using System.Diagnostics;
using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <see cref="DiagnosticTraceScope"/>, which surfaces host/Lua
/// portrayal diagnostics on stderr when <c>s100 render … --debug</c> is used
/// while keeping them silent by default (issue #241).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class DiagnosticTraceScopeTests
{
    [Fact]
    public void Scope_active_mirrors_trace_to_standard_error()
    {
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            using (DiagnosticTraceScope.ToStandardError())
            {
                Trace.WriteLine("[Lua] sample diagnostic");
            }
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("[Lua] sample diagnostic", writer.ToString());
    }

    [Fact]
    public void After_dispose_trace_is_not_written_to_standard_error()
    {
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            using (DiagnosticTraceScope.ToStandardError())
            {
            }

            Trace.WriteLine("[Lua] after dispose");
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.DoesNotContain("after dispose", writer.ToString());
    }
}
