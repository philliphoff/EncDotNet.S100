using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A link from an expanded row to another entry: a symbol the feature type is
/// drawn with, with its conditions in words, or a feature type that uses a
/// symbol. Opening it selects that entry's row (design note §4.6: every link
/// is two-way).
/// </summary>
internal sealed class ChartReferenceLinkViewModel
{
    /// <summary>Creates a link.</summary>
    /// <param name="key">The entry the link opens.</param>
    /// <param name="name">The entry's name.</param>
    /// <param name="cases">The conditions under which it is drawn, one line per case; empty for none.</param>
    /// <param name="note">A note under the cases, such as the shared rule that draws it, or <see langword="null"/>.</param>
    /// <param name="open">Selects the entry's row.</param>
    public ChartReferenceLinkViewModel(
        ReferenceKey key,
        string name,
        IReadOnlyList<string> cases,
        string? note,
        Action<ReferenceKey> open)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(open);
        Key = key;
        Name = name;
        Cases = cases;
        Note = note;
        OpenCommand = new RelayCommand(() => open(key));
    }

    /// <summary>The entry the link opens.</summary>
    public ReferenceKey Key { get; }

    /// <summary>The entry's code, such as <c>BCNLAT21</c>.</summary>
    public string Code => Key.Code;

    /// <summary>The entry's name; also the link's accessible name.</summary>
    public string Name { get; }

    /// <summary>The conditions under which the entry is drawn, in words, one line per case.</summary>
    public IReadOnlyList<string> Cases { get; }

    /// <summary>Whether there are cases to show.</summary>
    public bool HasCases => Cases.Count > 0;

    /// <summary>A note under the cases, or <see langword="null"/>.</summary>
    public string? Note { get; }

    /// <summary>Whether there is a note.</summary>
    public bool HasNote => Note is not null;

    /// <summary>Selects the entry's row.</summary>
    public ICommand OpenCommand { get; }
}
