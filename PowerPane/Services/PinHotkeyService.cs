using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Services
{
    /// <summary>
    /// Registers the pin shortcuts (#299): one system-wide key per assigned number, all on one hidden
    /// message window. Nothing is registered while the feature is off, so no key is taken from other
    /// programs. <see cref="Apply"/> returns the numbers Windows refused, because another program
    /// already holds that combination, so Settings can say so instead of only logging it.
    /// </summary>
    internal sealed class PinHotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        // 0xB19 and 0xB1A are HotkeyService's two ids; these are the next free block, one per number.
        private const int FirstId = 0xB20;

        private readonly NativeWindow _sink;
        private readonly List<int> _registered = new List<int>();

        /// <summary>Raised on the UI thread with the number pressed.</summary>
        public event Action<int> Pressed;

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

        public PinHotkeyService()
        {
            var sink = new Sink();
            sink.CreateHandle(new CreateParams());
            sink.OnMessage = m =>
            {
                if (m.Msg != WM_HOTKEY) return;
                int slot = m.WParam.ToInt32() - FirstId + PinHotkeyModel.FirstSlot;
                if (slot < PinHotkeyModel.FirstSlot || slot > PinHotkeyModel.LastSlot) return;
                var h = Pressed;
                if (h != null) h(slot);
            };
            _sink = sink;
        }

        /// <summary>Registers one key per number in <paramref name="slots"/> with the given prefix,
        /// after dropping every key registered before. Returns the numbers Windows refused.</summary>
        public List<int> Apply(bool enabled, string modifiers, IEnumerable<int> slots)
        {
            UnregisterAll();
            var refused = new List<int>();
            if (!enabled || slots == null) return refused;

            foreach (var slot in slots)
            {
                if (_registered.Contains(slot)) continue;
                string spec = PinHotkeyModel.SlotSpec(modifiers, slot);
                uint mods, vk;
                if (spec == null || !HotkeyService.TryParse(spec, out mods, out vk))
                {
                    Log.Event("shortcut", "the pin shortcut \"" + spec + "\" could not be read, so it is off.");
                    refused.Add(slot);
                    continue;
                }
                if (NM.RegisterHotKey(_sink.Handle, FirstId + slot - PinHotkeyModel.FirstSlot, mods, vk))
                {
                    _registered.Add(slot);
                }
                else
                {
                    Log.Event("shortcut", "the pin shortcut " + PinHotkeyModel.SlotLabel(modifiers, slot) +
                        " is already taken by another program or shortcut, so it was not registered.");
                    refused.Add(slot);
                }
            }
            if (_registered.Count > 0) Log.Info("pin shortcuts registered: " + _registered.Count + ".");
            return refused;
        }

        private void UnregisterAll()
        {
            foreach (var slot in _registered)
            {
                try { NM.UnregisterHotKey(_sink.Handle, FirstId + slot - PinHotkeyModel.FirstSlot); } catch { }
            }
            _registered.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
            try { _sink.DestroyHandle(); } catch { }
        }
    }
}
