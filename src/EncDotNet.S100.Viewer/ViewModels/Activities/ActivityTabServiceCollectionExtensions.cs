using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// DI helper that turns adding a viewer activity tab into a single line in
/// <see cref="App"/>.
/// </summary>
internal static class ActivityTabServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IActivityTab"/> singleton that wraps a
    /// view-model already registered in the service collection.
    /// </summary>
    /// <typeparam name="TViewModel">The tab's view-model type. Must be registered separately as a singleton.</typeparam>
    /// <typeparam name="TView">The tab's view (<see cref="UserControl"/>) type. Must have a parameterless constructor.</typeparam>
    /// <param name="services">The viewer service collection.</param>
    /// <param name="id">Stable string id (matches the legacy <c>ActivityKind</c> enum name for backwards compatibility with persisted settings).</param>
    /// <param name="order">Render order (ascending top-to-bottom). Tabs with <paramref name="order"/> &gt;= 1000 are pinned to the bottom of the activity bar.</param>
    /// <param name="title">Pane header text (already-resolved <see cref="Resources.Strings"/> value).</param>
    /// <param name="tooltip">Tooltip text (already-resolved <see cref="Resources.Strings"/> value).</param>
    /// <param name="iconFactory">Factory that returns a fresh icon control for each consumer.</param>
    /// <param name="persistAsLastSelected">When <c>true</c>, selecting the tab updates <see cref="ViewerSettings.LastSelectedActivity"/>.</param>
    public static IServiceCollection AddActivityTab<TViewModel, TView>(
        this IServiceCollection services,
        string id,
        int order,
        string title,
        string tooltip,
        Func<Control> iconFactory,
        bool persistAsLastSelected = true)
        where TViewModel : class
        where TView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(tooltip);
        ArgumentNullException.ThrowIfNull(iconFactory);

        services.AddSingleton<IActivityTab>(sp => new ActivityTab<TViewModel, TView>(
            id,
            order,
            title,
            tooltip,
            iconFactory,
            sp.GetRequiredService<TViewModel>(),
            persistAsLastSelected));

        return services;
    }
}
