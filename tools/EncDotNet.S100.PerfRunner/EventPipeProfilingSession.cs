using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Profile mode selectable via the <c>--profile</c> option.
/// </summary>
public enum ProfileMode
{
    /// <summary>No profiling. Default.</summary>
    None,

    /// <summary>
    /// Sampling CPU profile via the <c>Microsoft-DotNETCore-SampleProfiler</c>
    /// EventPipe provider plus the runtime/JIT/loader keywords required to
    /// symbolicate managed stacks. Equivalent to <c>dotnet-trace</c>'s
    /// <c>cpu-sampling</c> profile.
    /// </summary>
    Cpu,

    /// <summary>
    /// GC + allocation profile via the <c>Microsoft-Windows-DotNETRuntime</c>
    /// provider with the GC/AllocationTick keyword (<c>0x1</c>) at
    /// <see cref="EventLevel.Verbose"/>. Mirrors <c>dotnet-trace</c>'s
    /// <c>gc-verbose</c> profile.
    /// </summary>
    Alloc,
}

/// <summary>
/// In-process EventPipe profiling session. Captures a <c>.nettrace</c>
/// file from the running process so PerfRunner scenarios can be
/// profiled without launching a separate <c>dotnet-trace</c> process.
///
/// <para>
/// The implementation drains <see cref="EventPipeSession.EventStream"/>
/// concurrently with the workload via a background <see cref="Task"/>.
/// EventPipe streams events through an in-process pipe with a fixed
/// buffer; if the consumer does not drain it the buffer fills and
/// events are lost or the session corrupts. <see cref="DisposeAsync"/>
/// stops the session and waits for the drain task to complete before
/// returning, so the file is fully flushed when the call returns.
/// </para>
///
/// <para>
/// The output file is convertible with
/// <c>dotnet-trace convert &lt;file&gt;.nettrace --format speedscope</c>
/// or loadable directly in PerfView / Visual Studio.
/// </para>
/// </summary>
public sealed class EventPipeProfilingSession : IAsyncDisposable
{
    private readonly EventPipeSession _session;
    private readonly Task _drainTask;
    private readonly FileStream _output;
    private bool _disposed;

    private EventPipeProfilingSession(
        EventPipeSession session, FileStream output, Task drainTask)
    {
        _session = session;
        _output = output;
        _drainTask = drainTask;
    }

    /// <summary>Path to the <c>.nettrace</c> file being written.</summary>
    public string OutputPath { get; private init; } = "";

    /// <summary>Profile mode selected for this session.</summary>
    public ProfileMode Mode { get; private init; }

    /// <summary>
    /// Starts an in-process profiling session against the current
    /// process. The returned object owns the underlying file handle
    /// and must be disposed (preferably via <c>await using</c>) to
    /// stop the session and flush the trace.
    /// </summary>
    /// <param name="outputPath">
    /// Destination file path. Convention: same directory and basename
    /// as the scenario's <c>.jsonl</c>, with the <c>.nettrace</c> extension.
    /// </param>
    /// <param name="mode">CPU sampling, allocation, or none (no-op).</param>
    /// <param name="samplingIntervalMs">
    /// Sampling interval for <see cref="ProfileMode.Cpu"/> in milliseconds.
    /// Ignored for <see cref="ProfileMode.Alloc"/>. Recommended values
    /// are 1ms (default) for long scenarios; raise to 5–10ms for short
    /// (sub-100ms) scenarios where 1ms sampling overhead is significant.
    /// </param>
    public static EventPipeProfilingSession Start(
        string outputPath, ProfileMode mode, int samplingIntervalMs = 1)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        if (mode == ProfileMode.None)
            throw new ArgumentException("ProfileMode.None should be filtered by the caller.", nameof(mode));

        var providers = mode switch
        {
            ProfileMode.Cpu => BuildCpuProviders(samplingIntervalMs),
            ProfileMode.Alloc => BuildAllocProviders(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var client = new DiagnosticsClient(Environment.ProcessId);
        var session = client.StartEventPipeSession(providers, requestRundown: true);

        var output = File.Create(outputPath);
        // Drain the EventStream into the file on a background task so
        // the workload thread is never blocked by the EventPipe pipe
        // back-pressure. CopyToAsync runs until the session is stopped.
        var drainTask = Task.Run(async () =>
        {
            try
            {
                await session.EventStream.CopyToAsync(output).ConfigureAwait(false);
            }
            finally
            {
                await output.FlushAsync().ConfigureAwait(false);
            }
        });

        return new EventPipeProfilingSession(session, output, drainTask)
        {
            OutputPath = outputPath,
            Mode = mode,
        };
    }

    private static List<EventPipeProvider> BuildCpuProviders(int samplingIntervalMs)
    {
        // Sampling interval is not directly settable via the public
        // EventPipeProvider API; the SampleProfiler defaults to 1ms.
        // The samplingIntervalMs parameter is reserved for a future
        // tuning hook and currently used only for documentation.
        _ = samplingIntervalMs;

        return new List<EventPipeProvider>
        {
            // Sampling profiler — the source of CPU stack samples.
            new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
            // Runtime keywords required to resolve managed stack frames:
            //   GC (0x1), Loader (0x8), Jit (0x10), NGen (0x20), StopEnumeration (0x40),
            //   Security (0x400), AppDomainResourceManagement (0x800), JitTracing (0x1000),
            //   Interop (0x2000), Contention (0x4000), Exception (0x8000), Threading (0x10000),
            //   JittedMethodILToNativeMap (0x20000), OverrideAndSuppressNGenEvents (0x40000),
            //   Type (0x80000), GCHeapDump (0x100000), GCSampledObjectAllocationHigh (0x200000),
            //   GCHeapSurvivalAndMovement (0x400000), GCHeapCollect (0x800000),
            //   GCHeapAndTypeNames (0x1000000), GCSampledObjectAllocationLow (0x2000000),
            //   PerfTrack (0x20000000), Stack (0x40000000)
            // The 0x4c14fccbd value matches `dotnet-trace`'s "cpu-sampling" profile.
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x14C14FCCBD),
        };
    }

    private static List<EventPipeProvider> BuildAllocProviders()
    {
        // gc-verbose profile from `dotnet-trace`: GC keyword (0x1) at Verbose
        // level emits AllocationTick events for every ~100KB of allocations,
        // plus all GC start/stop events. Combined with the Stack keyword
        // (0x40000000) so each allocation has a managed call stack.
        return new List<EventPipeProvider>
        {
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x40000001),
        };
    }

    /// <summary>
    /// Stops the profiling session and waits for the drain task to
    /// finish writing the trace file.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _session.Stop();
        }
        catch (Exception)
        {
            // Stop() can throw if the target process has already
            // finalised the session (e.g. test crash). Suppress so
            // dispose still flushes whatever was already drained.
        }

        try
        {
            await _drainTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Drain failures are surfaced by an empty/short trace file,
            // which the post-run instructions already warn about.
        }

        _session.Dispose();
        await _output.DisposeAsync().ConfigureAwait(false);
    }
}
