using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Timeline's passage lanes and Follow passage (#904, handoff E1–E3).
/// </summary>
internal sealed partial class TimelineViewModel
{
    /// <summary>The passage lanes, or <c>null</c> until <see cref="AttachPassage"/> runs.</summary>
    public PassageTimelineViewModel? Passage { get; private set; }

    /// <summary>
    /// Adds the passage lanes for Check passage and Follow passage. The app
    /// calls it once after construction; tests may leave it out.
    /// </summary>
    internal void AttachPassage(PassageCheckService check, FollowPassageService? follow)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (Passage is not null)
            throw new InvalidOperationException("The passage lanes are already attached.");
        // The raw axis: the Axis getter rebuilds lazily, and a rebuild refreshes the lanes.
        Passage = new PassageTimelineViewModel(check, follow, () => _axis, () => ActiveFormat);
        OnPropertyChanged(nameof(Passage));
    }
}
