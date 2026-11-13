using System.Diagnostics;
using System.Runtime.Versioning;

namespace EncDotNet.S100.Viewer.Services.Secrets;

/// <summary>
/// Secrets in the Secret Service on Linux (GNOME Keyring, KWallet), through
/// libsecret's <c>secret-tool</c>, with the attributes
/// <c>service=SoundCharts</c> and <c>name=&lt;name&gt;</c>. The secret goes
/// through standard input, never the command line.
/// </summary>
/// <remarks>
/// Unavailable without <c>secret-tool</c> on the path or a D-Bus session, as
/// on a headless server or CI runner.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LibsecretToolSecretStore : ISecretStore
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ISecretToolRunner _runner;
    private readonly Lazy<bool> _available;

    public LibsecretToolSecretStore(ISecretToolRunner? runner = null)
    {
        _runner = runner ?? new ProcessSecretToolRunner();
        _available = new Lazy<bool>(() => _runner.IsInstalled);
    }

    public string DisplayName => Resources.Strings.Secrets_Store_Linux;

    public bool IsAvailable => _available.Value;

    public void Write(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var result = _runner.Run(
            ["store", "--label", $"{PlatformSecretStore.ServiceName}: {name}", "service", PlatformSecretStore.ServiceName, "name", name],
            standardInput: value);
        if (result.ExitCode != 0)
            throw new SecretStoreException($"secret-tool didn't save the secret (exit code {result.ExitCode}).");
    }

    public string? Read(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var result = _runner.Run(["lookup", "service", PlatformSecretStore.ServiceName, "name", name], standardInput: null);
        // secret-tool ends the secret with a newline when it writes to a terminal.
        var secret = result.Output.EndsWith('\n') ? result.Output[..^1] : result.Output;
        return result.ExitCode == 0 && secret.Length > 0 ? secret : null;
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _runner.Run(["clear", "service", PlatformSecretStore.ServiceName, "name", name], standardInput: null);
    }

    /// <summary>Runs <c>secret-tool</c>.</summary>
    internal interface ISecretToolRunner
    {
        /// <summary>Whether <c>secret-tool</c> is installed and there's a D-Bus session.</summary>
        bool IsInstalled { get; }

        /// <summary>Runs <c>secret-tool</c> with <paramref name="arguments"/>, writing <paramref name="standardInput"/> to it.</summary>
        (int ExitCode, string Output) Run(IReadOnlyList<string> arguments, string? standardInput);
    }

    private sealed class ProcessSecretToolRunner : ISecretToolRunner
    {
        public bool IsInstalled =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
            && FindOnPath("secret-tool") is not null;

        public (int ExitCode, string Output) Run(IReadOnlyList<string> arguments, string? standardInput)
        {
            var info = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
            try
            {
                using var process = Process.Start(info);
                if (process is null)
                    return (-1, string.Empty);
                if (standardInput is not null)
                    process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(Timeout))
                {
                    process.Kill();
                    return (-1, string.Empty);
                }
                return (process.ExitCode, output);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (-1, string.Empty);
            }
        }

        private static string? FindOnPath(string file) =>
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, file))
                .FirstOrDefault(File.Exists);
    }
}
