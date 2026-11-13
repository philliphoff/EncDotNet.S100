namespace EncDotNet.S100.Viewer.Services.Secrets;

/// <summary>
/// Where SoundCharts keeps secrets such as API keys, out of
/// <c>settings.json</c> (data source design note D7): Credential Manager on
/// Windows, the login Keychain on macOS, and the Secret Service (libsecret)
/// on Linux. Settings keep only a <c>sc-secret:</c> reference.
/// </summary>
internal interface ISecretStore
{
    /// <summary>The store's name for the settings pages, such as "Windows Credential Manager".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether the store works on this machine. It doesn't on a Linux machine
    /// without a Secret Service, such as a headless server or CI runner.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Saves a secret, replacing any with the same name.</summary>
    /// <param name="name">The secret's name, such as <c>ais.aisstream.apiKey</c>.</param>
    /// <param name="value">The secret.</param>
    /// <exception cref="SecretStoreException">The store refused it.</exception>
    void Write(string name, string value);

    /// <summary>Reads a secret, or <see langword="null"/> when there's none or the store can't be read.</summary>
    /// <param name="name">The secret's name.</param>
    string? Read(string name);

    /// <summary>Deletes a secret; nothing happens when there's none.</summary>
    /// <param name="name">The secret's name.</param>
    void Delete(string name);
}

/// <summary>A secret store refused to save a secret.</summary>
internal sealed class SecretStoreException : Exception
{
    public SecretStoreException(string message)
        : base(message)
    {
    }

    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A store that keeps secrets in memory, for tests.</summary>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public string DisplayName => "Memory";

    public bool IsAvailable { get; init; } = true;

    public void Write(string name, string value)
    {
        if (!IsAvailable)
            throw new SecretStoreException("The store isn't available.");
        lock (_secrets)
            _secrets[name] = value;
    }

    public string? Read(string name)
    {
        lock (_secrets)
            return _secrets.GetValueOrDefault(name);
    }

    public void Delete(string name)
    {
        lock (_secrets)
            _secrets.Remove(name);
    }
}

/// <summary>The store on a machine that has none: nothing can be saved.</summary>
internal sealed class UnavailableSecretStore : ISecretStore
{
    public string DisplayName => "None";

    public bool IsAvailable => false;

    public void Write(string name, string value) =>
        throw new SecretStoreException("There's no secret store on this machine.");

    public string? Read(string name) => null;

    public void Delete(string name)
    {
    }
}

/// <summary>Picks the secret store for this platform.</summary>
internal static class PlatformSecretStore
{
    /// <summary>The service name secrets are filed under in the platform store.</summary>
    public const string ServiceName = "SoundCharts";

    /// <summary>The store for this platform, or an <see cref="UnavailableSecretStore"/>.</summary>
    public static ISecretStore Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialSecretStore();
        if (OperatingSystem.IsMacOS())
            return new MacKeychainSecretStore();
        if (OperatingSystem.IsLinux())
        {
            var linux = new LibsecretToolSecretStore();
            return linux.IsAvailable ? linux : new UnavailableSecretStore();
        }
        return new UnavailableSecretStore();
    }
}
