using System.Collections.ObjectModel;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Finds the Add-to-Library dialog's choices by group, as the user sees them.</summary>
internal static class AddToLibraryDialogTestExtensions
{
    /// <summary>The shown choices of the group titled <paramref name="title"/>, or of the only group.</summary>
    public static ObservableCollection<LibraryChoice> Choices(this AddToLibraryDialogViewModel vm, string? title = null) =>
        (title is null ? Assert.Single(vm.FacetGroups) : vm.FacetGroups.Single(g => g.Title == title)).Options;
}
