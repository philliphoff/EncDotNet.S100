using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Avalonia data template that materialises an <see cref="IActivityTab"/>
/// into its <see cref="UserControl"/>. Registered once on the main window
/// via <c>Window.DataTemplates</c> so a single
/// <c>ContentControl Content="{Binding SelectedTab}"</c> can render any
/// tab's view.
/// </summary>
internal sealed class ActivityTabViewTemplate : IDataTemplate
{
    public bool Match(object? data) => data is IActivityTab;

    public Control? Build(object? data)
    {
        if (data is not IActivityTab tab)
        {
            return null;
        }

        var view = tab.CreateView();
        view.DataContext = tab.ViewModel;
        return view;
    }
}
