using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace EncDotNet.S100.Viewer.Services.Secrets;

/// <summary>
/// Secrets in the user's login Keychain on macOS, as generic passwords with
/// the service <c>SoundCharts</c> and the secret's name as the account.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;

    private static readonly byte[] Service = Encoding.UTF8.GetBytes(PlatformSecretStore.ServiceName);

    public string DisplayName => Resources.Strings.Secrets_Store_MacOS;

    public bool IsAvailable => true;

    public void Write(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var account = Encoding.UTF8.GetBytes(name);
        var password = Encoding.UTF8.GetBytes(value);
        try
        {
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                out _, IntPtr.Zero, out var item);
            if (status == ErrSecSuccess)
            {
                try
                {
                    status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password);
                }
                finally
                {
                    CFRelease(item);
                }
            }
            else if (status == ErrSecItemNotFound)
            {
                status = SecKeychainAddGenericPassword(
                    IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    (uint)password.Length, password, IntPtr.Zero);
            }
            if (status != ErrSecSuccess)
                throw new SecretStoreException($"The Keychain didn't save the secret (OSStatus {status}).");
        }
        finally
        {
            Array.Clear(password);
        }
    }

    public string? Read(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var account = Encoding.UTF8.GetBytes(name);
        var status = SecKeychainFindGenericPasswordData(
            IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out var length, out var data, IntPtr.Zero);
        if (status != ErrSecSuccess || data == IntPtr.Zero)
            return null;
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            var value = Encoding.UTF8.GetString(bytes);
            Array.Clear(bytes);
            return value;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
        }
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var account = Encoding.UTF8.GetBytes(name);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, IntPtr.Zero, out var item);
        if (status != ErrSecSuccess)
            return;
        try
        {
            status = SecKeychainItemDelete(item);
            if (status != ErrSecSuccess)
                throw new SecretStoreException($"The Keychain didn't delete the secret (OSStatus {status}).");
        }
        finally
        {
            CFRelease(item);
        }
    }

    // SecKeychainFindGenericPassword returning only the item: passwordLength
    // and passwordData are optional out parameters, passed as null here.
    [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, IntPtr passwordData,
        out IntPtr itemRef);

    // The same function returning the password data and no item.
    [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")]
    private static extern int SecKeychainFindGenericPasswordData(
        IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData,
        IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData,
        IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
