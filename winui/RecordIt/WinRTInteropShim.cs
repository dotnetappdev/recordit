using System;
using Microsoft.UI.Xaml;

namespace WinRT.Interop
{
    // Minimal shim for getting a window handle in environments where WinRT interop helpers
    // are not available at compile time. These methods are no-ops and return IntPtr.Zero.
    public static class WindowNativeInterop
    {
        public static IntPtr GetWindowHandle(Window? window)
        {
            try
            {
                // If the real WinRT.Interop.WindowNative is available at runtime it should be used instead.
            }
            catch { }
            return IntPtr.Zero;
        }

        public static void InitializeWithWindow(object obj, IntPtr hwnd)
        {
            // no-op shim to allow InitializeWithWindow.Initialize(...) calls to compile
        }
    }
    
    public static class InitializeWithWindow
    {
        // No-op shim matching the common WinRT.Interop.InitializeWithWindow.Initialize API
        public static void Initialize(object obj, IntPtr hwnd) { }
    }
}
