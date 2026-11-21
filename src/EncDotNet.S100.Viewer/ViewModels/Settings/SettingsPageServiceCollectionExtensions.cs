using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Registers Settings pages (#1398).</summary>
internal static class SettingsPageServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="TPage"/> as a singleton and as one of the Settings panel's <see cref="ISettingsPage"/>s.</summary>
    /// <typeparam name="TPage">The page.</typeparam>
    /// <param name="services">The services.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddSettingsPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage>(this IServiceCollection services)
        where TPage : class, ISettingsPage
    {
        services.AddSingleton<TPage>();
        services.AddSingleton<ISettingsPage>(sp => sp.GetRequiredService<TPage>());
        return services;
    }
}
