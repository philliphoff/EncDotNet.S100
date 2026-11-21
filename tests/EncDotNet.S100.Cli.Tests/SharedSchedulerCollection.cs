namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Runs test classes that wait on background disk writes alone. The disk
/// caches queue their writes on the process-wide
/// <see cref="EncDotNet.S100.Core.Scheduling.WorkScheduler.Shared"/> at background
/// priority, so while rendering tests run beside them a write can wait longer
/// than the tests allow (seen on the Windows ARM runner).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedSchedulerCollection
{
    /// <summary>Collection name shared by the classes that wait on queued disk writes.</summary>
    public const string Name = "Shared scheduler (serialized)";
}
