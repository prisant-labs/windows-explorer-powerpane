using System;
using System.Windows.Forms;
using System.Windows.Input;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Services
{
    /// <summary>
    /// One system-wide shortcut: the one that moves focus into the pane, so a Save dialog can be driven
    /// without the mouse, or the one that opens the quick switcher (#226). Each is registered only when
    /// the user turns it on, because a global hotkey takes a combination away from every other program
    /// on the machine. Each instance has its own id and its own message window.
    /// </summary>
    internal sealed class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        public const int KeyboardNavId = 0xB19;
        public const int QuickSwitcherId = 0xB1A;

        private readonly int _id;
        private readonly string _purpose;

        private readonly NativeWindow _sink;
        private bool _registered;

        public event Action Pressed;

        private sealed class Sink : NativeWindow
        {
            public Action<Message> OnMessage;
            protected override void WndProc(ref Message m)
            {
                var h = OnMessage;
                if (h != null) h(m);
                base.WndProc(ref m);
            }
        }

        public HotkeyService() : this(KeyboardNavId, "keyboard navigation") { }

        /// <param name="purpose">Names the shortcut in the log, e.g. "quick switcher".</param>
        public HotkeyService(int id, string purpose)
        {
            _id = id;
            _purpose = purpose;
            var sink = new Sink();
            sink.CreateHandle(new CreateParams());
            sink.OnMessage = m =>
            {
                if (m.Msg != WM_HOTKEY || m.WParam.ToInt32() != _id) return;
                var h = Pressed;
                if (h != null) h();
            };
            _sink = sink;
        }

        /// <summary>Apply the current settings: register, re-register or drop the shortcut.</summary>
        public void Apply(bool enabled, string spec)
        {
            Unregister();
            if (!enabled) return;

            uint mods; uint vk;
            if (!TryParse(spec, out mods, out vk))
            {
                Log.Event("shortcut", "the " + _purpose + " shortcut \"" + spec +
                    "\" could not be read, so it is off.");
                return;
            }

            _registered = NM.RegisterHotKey(_sink.Handle, _id, mods, vk);
            if (!_registered)
                Log.Event("shortcut", "the " + _purpose + " shortcut \"" + spec +
                    "\" is already taken by another program or shortcut, so it was not registered.");
            else
                Log.Info(_purpose + " shortcut registered as " + spec + ".");
        }

        private void Unregister()
        {
            if (!_registered) return;
            try { NM.UnregisterHotKey(_sink.Handle, _id); } catch { }
            _registered = false;
        }

        /// <summary>Read "ctrl+shift+Q" into the modifier flags and virtual key RegisterHotKey wants.</summary>
        public static bool TryParse(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;

            foreach (var raw in spec.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= 0x0002; continue;
                    case "alt": mods |= 0x0001; continue;
                    case "shift": mods |= 0x0004; continue;
                    case "win": case "windows": mods |= 0x0008; continue;
                }

                Key key;
                if (!Enum.TryParse(part, true, out key)) return false;
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            return vk != 0 && mods != 0;   // a bare key would swallow that key everywhere
        }

        public void Dispose()
        {
            Unregister();
            try { _sink.DestroyHandle(); } catch { }
        }
    }
}
