using System;
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
// TODO PR-M-future: resolve views via DI instead of Activator.CreateInstance
//   once view constructors need access to services. All current views have
//   parameterless constructors so this stays simple for PR-M1.
internal sealed class ActivityTabViewTemplate : IDataTemplate
{
    public bool Match(object? data) => data is IActivityTab;

    public Control? Build(object? data)
    {
        if (data is not IActivityTab tab)
        {
            return null;
        }

        var view = (Control)Activator.CreateInstance(tab.ViewType)!;
        view.DataContext = tab.ViewModel;
        return view;
    }
}
