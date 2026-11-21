namespace EncDotNet.S100.Core.Scheduling;

/// <summary>
/// What a unit of work is for. The scheduler runs a better class first: a
/// free worker always takes the best queued job across every stage.
/// </summary>
public enum WorkClass
{
    /// <summary>Needed for the frame on screen now.</summary>
    Visible = 0,

    /// <summary>Likely needed soon: the next view of a pan or zoom.</summary>
    Predicted = 1,

    /// <summary>Pre-warm, idle fill and cache writes.</summary>
    Background = 2,

    /// <summary>Validation, which never competes with drawing.</summary>
    Validation = 3,
}

/// <summary>
/// The pipeline stage a job belongs to. The scheduler doesn't give stages
/// quotas; it uses the stage to report where time goes and to find the
/// allocation-heavy jobs it admits by GC pause share.
/// </summary>
public enum WorkStage
{
    /// <summary>Reading a dataset.</summary>
    Parse,

    /// <summary>Translating a dataset to another model (S-57 to S-101).</summary>
    Translate,

    /// <summary>Running portrayal rules.</summary>
    Portray,

    /// <summary>Building a vector scene from drawing instructions.</summary>
    Scene,

    /// <summary>Clipping pattern fills.</summary>
    Clip,

    /// <summary>Composing the chart stack.</summary>
    Stack,

    /// <summary>Rasterising tiles.</summary>
    Raster,

    /// <summary>Encoding images.</summary>
    Encode,

    /// <summary>Reading or writing caches.</summary>
    Io,

    /// <summary>Validating data.</summary>
    Validate,
}

/// <summary>
/// A job's priority: its <see cref="WorkClass"/> first, then its urgency.
/// </summary>
/// <param name="Class">The class. A lower value runs first.</param>
/// <param name="Urgency">
/// Order within the class: lower runs first. Use seconds until the result is
/// needed or distance from the view centre; any scale works as long as the
/// jobs that compete within a class use the same one.
/// </param>
public readonly record struct WorkPriority(WorkClass Class, double Urgency = 0) : IComparable<WorkPriority>
{
    /// <summary>A <see cref="WorkClass.Visible"/> priority.</summary>
    /// <param name="urgency">Order within the class; lower runs first.</param>
    /// <returns>The priority.</returns>
    public static WorkPriority Visible(double urgency = 0) => new(WorkClass.Visible, urgency);

    /// <summary>A <see cref="WorkClass.Predicted"/> priority.</summary>
    /// <param name="urgency">Order within the class; lower runs first.</param>
    /// <returns>The priority.</returns>
    public static WorkPriority Predicted(double urgency = 0) => new(WorkClass.Predicted, urgency);

    /// <summary>A <see cref="WorkClass.Background"/> priority.</summary>
    /// <param name="urgency">Order within the class; lower runs first.</param>
    /// <returns>The priority.</returns>
    public static WorkPriority Background(double urgency = 0) => new(WorkClass.Background, urgency);

    /// <summary>A <see cref="WorkClass.Validation"/> priority.</summary>
    /// <param name="urgency">Order within the class; lower runs first.</param>
    /// <returns>The priority.</returns>
    public static WorkPriority Validation(double urgency = 0) => new(WorkClass.Validation, urgency);

    /// <summary>Whether this priority runs before <paramref name="other"/>.</summary>
    /// <param name="other">The priority to compare with.</param>
    /// <returns><see langword="true"/> when this one is strictly better.</returns>
    public bool IsBetterThan(WorkPriority other) => CompareTo(other) < 0;

    /// <inheritdoc />
    public int CompareTo(WorkPriority other)
    {
        var byClass = Class.CompareTo(other.Class);
        return byClass != 0 ? byClass : Urgency.CompareTo(other.Urgency);
    }

    /// <summary>The better of two priorities.</summary>
    /// <param name="a">One priority.</param>
    /// <param name="b">The other.</param>
    /// <returns>Whichever runs first; <paramref name="a"/> on a tie.</returns>
    public static WorkPriority Best(WorkPriority a, WorkPriority b) => b.IsBetterThan(a) ? b : a;
}

/// <summary>
/// Identifies a job for single-flight: scheduling a key that is already queued
/// or running joins that job instead of starting another.
/// </summary>
/// <param name="Owner">
/// What the work belongs to, such as a layer's state or a cell. Compared with
/// its own <see cref="object.Equals(object)"/>, so a class without an override
/// compares by reference.
/// </param>
/// <param name="Item">The item within the owner, such as a tile key.</param>
public readonly record struct WorkKey(object Owner, object? Item = null);
