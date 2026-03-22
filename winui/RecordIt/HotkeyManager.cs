using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RecordIt;

public class HotkeyPressedEventArgs : EventArgs
{
    public string Action { get; }
    public HotkeyPressedEventArgs(string action) => Action = action;
}

public class HotkeyManager : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private readonly Dictionary<string, KeyGesture> _bindings = new();

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public HotkeyManager()
    {
        _proc = HookCallback;
        _hookId = SetHook(_proc);
    }

    public void RegisterBinding(string action, KeyGesture gesture)
    {
        _bindings[action] = gesture;
    }

    public void UnregisterBinding(string action)
    {
        if (_bindings.ContainsKey(action)) _bindings.Remove(action);
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule!.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                var key = KeyInterop.KeyFromVirtualKey(vkCode);
                var modifiers = GetModifiers();

                foreach (var kv in _bindings)
                {
                    var gesture = kv.Value;
                    if (gesture.Key == key && gesture.Modifiers == modifiers)
                    {
                        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(kv.Key));
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static ModifierKeys GetModifiers()
    {
        ModifierKeys m = ModifierKeys.None;
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) m |= ModifierKeys.Control;
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) m |= ModifierKeys.Shift;
        if ((GetKeyState(VK_MENU) & 0x8000) != 0) m |= ModifierKeys.Alt;
        return m;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
