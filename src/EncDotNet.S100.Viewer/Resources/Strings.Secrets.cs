namespace EncDotNet.S100.Viewer.Resources;

/// <summary>The secret store strings (data source design note D7).</summary>
internal static partial class Strings
{
    public static string Secrets_Store_Windows => Get(nameof(Secrets_Store_Windows));
    public static string Secrets_Store_MacOS => Get(nameof(Secrets_Store_MacOS));
    public static string Secrets_Store_Linux => Get(nameof(Secrets_Store_Linux));
    public static string Settings_SecretSavedInStoreFormat => Get(nameof(Settings_SecretSavedInStoreFormat));
    public static string Settings_SecretNoStoreFormat => Get(nameof(Settings_SecretNoStoreFormat));
}
