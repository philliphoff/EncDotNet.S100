using CommunityToolkit.Mvvm.ComponentModel;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Base class for viewer view models. Inherits from
/// <see cref="ObservableObject"/> from CommunityToolkit.Mvvm so derived types
/// get <c>SetProperty</c>, <c>OnPropertyChanged</c>, and
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> for free.
/// </summary>
internal abstract class ViewModelBase : ObservableObject;
