namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class SettingsViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;

    public SettingsViewModel(ViewerSettings settings)
    {
        _settings = settings;
    }
}
