using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Something a host lets the user do with a picked feature, such as "Take
/// helm" on an AIS target (data source design note P4). An action is offered
/// for every pick it <see cref="AppliesTo"/>, whatever product or source the
/// feature came from; the host carries it out by <see cref="Id"/>.
/// </summary>
/// <param name="Id">A dotted id, such as <c>"own-ship.take-helm"</c>.</param>
/// <param name="Label">The button's label, in the host's language.</param>
/// <param name="AppliesTo">Whether the action is offered for a pick.</param>
public sealed record PickAction(string Id, string Label, Func<PickContext, bool> AppliesTo);

/// <summary>The <see cref="PickAction"/>s a host offers, in registration order.</summary>
/// <remarks>Register actions at start-up, before the first pick.</remarks>
public sealed class PickActionRegistry
{
    private readonly object _lock = new();
    private PickAction[] _actions = [];

    /// <summary>The process-wide registry a host fills at start-up; empty until it does.</summary>
    public static PickActionRegistry Default { get; } = new();

    /// <summary>The registered actions.</summary>
    public IReadOnlyList<PickAction> Actions => Volatile.Read(ref _actions);

    /// <summary>Registers an action.</summary>
    /// <param name="action">The action.</param>
    /// <exception cref="ArgumentException">An action with the same id is already registered.</exception>
    public void Register(PickAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Id);
        lock (_lock)
        {
            if (_actions.Any(a => string.Equals(a.Id, action.Id, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "The pick action '{0}' is already registered.", action.Id),
                    nameof(action));
            }
            _actions = [.. _actions, action];
        }
    }

    /// <summary>The actions offered for <paramref name="context"/>. An action whose test throws isn't offered.</summary>
    /// <param name="context">The picked feature.</param>
    public IReadOnlyList<PickAction> ActionsFor(PickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var offered = new List<PickAction>();
        foreach (var action in Actions)
        {
            bool applies;
            try
            {
                applies = action.AppliesTo(context);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                applies = false;
            }
            if (applies)
                offered.Add(action);
        }
        return offered;
    }
}
