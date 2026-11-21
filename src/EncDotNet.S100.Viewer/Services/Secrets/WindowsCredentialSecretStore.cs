using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace EncDotNet.S100.Viewer.Services.Secrets;

/// <summary>
/// Secrets in Windows Credential Manager, as generic credentials named
/// <c>SoundCharts:&lt;name&gt;</c> and kept for the current user.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string DisplayName => Resources.Strings.Secrets_Store_Windows;

    public bool IsAvailable => true;

    private static string Target(string name) => PlatformSecretStore.ServiceName + ":" + name;

    public void Write(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var blob = Encoding.Unicode.GetBytes(value);
        var target = Marshal.StringToHGlobalUni(Target(name));
        var user = Marshal.StringToHGlobalUni(Environment.UserName);
        var blobPtr = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            if (!CredWrite(ref credential, 0))
                throw new SecretStoreException("Credential Manager didn't save the secret.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally
        {
            CryptographicClear(blobPtr, blob.Length);
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
        }
    }

    public string? Read(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!CredRead(Target(name), CredTypeGeneric, 0, out var pointer))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!CredDelete(Target(name), CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
            throw new SecretStoreException("Credential Manager didn't delete the secret.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static void CryptographicClear(IntPtr pointer, int length)
    {
        for (var i = 0; i < length; i++)
            Marshal.WriteByte(pointer, i, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
