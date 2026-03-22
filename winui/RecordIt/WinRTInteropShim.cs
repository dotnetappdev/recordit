using System;
using Microsoft.UI.Xaml;

namespace WinRT.Interop
{
    /// <summary>
    /// Thin wrapper around WinRT.Interop.WindowNative so callers can use the
    /// consistent <c>WindowNativeInterop.GetWindowHandle(window)</c> API.
    /// The conflicting <c>InitializeWithWindow</c> class was removed — the SDK
    /// already ships <c>WinRT.Interop.InitializeWithWindow.Initialize()</c>.
    /// </summary>
    public static class WindowNativeInterop
    {
        public static IntPtr GetWindowHandle(Window? window)
        {
            if (window is null) return IntPtr.Zero;
            try
            {
                return WindowNative.GetWindowHandle(window);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
