using System.Runtime.InteropServices;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Opts the process out of macOS App Nap while an activity is held. App Nap
/// throttles an app whose windows are all hidden or covered: it lowers the
/// process's CPU priority and coalesces its timers, so chart loads, tile
/// rasterising and paints run several times slower. The perf harness holds an
/// activity so a measurement doesn't depend on whether the viewer's window
/// happens to be visible.
/// </summary>
internal static class MacAppNap
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // NSActivityUserInitiatedAllowingIdleSystemSleep | NSActivityLatencyCritical.
    private const ulong ActivityOptions = 0x00EF_FFFFUL | 0xFF_0000_0000UL;

    private static readonly Lock Gate = new();
    private static IntPtr _activity;

    /// <summary>Whether this process holds the activity.</summary>
    public static bool IsDisabled
    {
        get
        {
            lock (Gate)
            {
                return _activity != IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Begins a latency-critical, user-initiated activity, which keeps App Nap
    /// off until the process exits. Does nothing off macOS, or when the activity
    /// is already held.
    /// </summary>
    /// <param name="reason">Why, as macOS shows it (<c>pmset -g assertions</c>).</param>
    /// <returns>Whether App Nap is now off for this process.</returns>
    public static bool Disable(string reason)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        lock (Gate)
        {
            if (_activity != IntPtr.Zero)
                return true;

            var utf8 = Marshal.StringToCoTaskMemUTF8(reason);
            try
            {
                var processInfo = SendIntPtr(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
                var nsReason = SendIntPtr(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8);
                var activity = SendBeginActivity(
                    processInfo, sel_registerName("beginActivityWithOptions:reason:"), ActivityOptions, nsReason);
                if (activity == IntPtr.Zero)
                    return false;

                // Keep the token alive past the autorelease pool; it's never ended.
                _activity = SendIntPtr(activity, sel_registerName("retain"));
                return true;
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendBeginActivity(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);
}
