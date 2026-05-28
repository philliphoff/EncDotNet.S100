using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Value converter that invokes a <see cref="Func{Control}"/> obtained
/// from binding to <see cref="IActivityTab.CreateIcon"/> and returns
/// the resulting <see cref="Control"/>. Without it, the binding
/// resolves to the method group itself (rendered as the delegate's
/// <c>ToString()</c>, e.g. "System.Func`1[Avalonia.Controls.Control]")
/// rather than the icon control. Each invocation produces a fresh
/// control instance so each consumer gets its own parented child.
/// </summary>
internal sealed class IconFactoryConverter : IValueConverter
{
    public static readonly IconFactoryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Func<Control> factory ? factory() : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
