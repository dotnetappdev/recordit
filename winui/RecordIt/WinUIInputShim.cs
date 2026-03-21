using System;

namespace System.Windows.Input
{
    [Flags]
    public enum ModifierKeys
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }

    // Minimal placeholder Key enum - values may be cast from virtual-key codes when necessary
    public enum Key
    {
        None = 0
    }

    public class KeyGesture
    {
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }

        public KeyGesture(Key key, ModifierKeys modifiers = ModifierKeys.None)
        {
            Key = key;
            Modifiers = modifiers;
        }
    }

    public static class KeyInterop
    {
        public static Key KeyFromVirtualKey(int virtualKey)
        {
            return (Key)virtualKey;
        }
    }
}
