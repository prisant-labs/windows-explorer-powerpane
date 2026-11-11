using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Explorer
{
    /// <summary>One connected monitor, for the Pane page's Display picker (#146) and for
    /// <see cref="AppBarHost"/>'s placement. <see cref="Id"/> is the monitor's device-interface
    /// path from <c>EnumDisplayDevices</c>, stable across reboots, unlike <c>MONITORINFOEX</c>'s
    /// own <c>szDevice</c> ("\\.\DISPLAY2"), which can renumber when a display is added, removed,
    /// or the user changes which one is primary.</summary>
    internal struct MonitorDescriptor
    {
        public string Id;
        public string Label;
        public NM.RECT MonitorRect;
        public NM.RECT WorkRect;
        public bool IsPrimary;
    }

    /// <summary>Lists connected monitors for the Pane page's Display row, and resolves a stored
    /// choice to the one monitor <see cref="AppBarHost"/> should place the desktop dock on. The
    /// decisions below are pure, internal static methods, tested with invented monitor lists; only
    /// <see cref="DescribeMonitors"/> itself calls into Windows (#146).</summary>
    internal static class MonitorPicker
    {
        /// <summary>The stored value meaning "track whichever monitor Windows currently calls
        /// primary", the Display row's default. An unknown id -- the chosen monitor disconnected --
        /// falls back to the same thing, never to a fixed monitor that might not exist either.</summary>
        public const string PrimaryDisplayId = "";

        /// <summary>The monitor <see cref="AppBarHost"/> should place the dock on: the one whose
        /// <see cref="MonitorDescriptor.Id"/> matches <paramref name="storedId"/>, or the current
        /// primary when <paramref name="storedId"/> is null/empty (the default) or names a monitor
        /// not present in <paramref name="monitors"/> (disconnected). A zeroed descriptor comes back
        /// only when <paramref name="monitors"/> itself is empty -- the OS reported no monitors at
        /// all -- which <see cref="AppBarHost"/> never positions from; it falls back to the window's
        /// own nearest-monitor lookup instead.</summary>
        internal static MonitorDescriptor ResolveMonitor(List<MonitorDescriptor> monitors, string storedId)
        {
            if (monitors == null || monitors.Count == 0) return default(MonitorDescriptor);

            if (!string.IsNullOrEmpty(storedId))
            {
                foreach (var m in monitors)
                    if (string.Equals(m.Id, storedId, StringComparison.Ordinal)) return m;
                // Named monitor not connected: fall through to the primary, same as the default.
            }

            foreach (var m in monitors)
                if (m.IsPrimary) return m;

            // Nothing reported itself primary -- should not happen on a real desktop, but a monitor
            // beats returning nothing.
            return monitors[0];
        }

        /// <summary>The Display combo's entries: "Primary display" first, storing
        /// <see cref="PrimaryDisplayId"/> (null/empty) so it always tracks the OS primary live, then
        /// one entry per connected monitor with its own stable id and label, then -- only when
        /// <paramref name="storedId"/> names a monitor not among them -- one more entry for it. Without
        /// that trailing entry, a disconnected choice silently showed as "Primary display" already
        /// selected, so picking "Primary display" again fired no <c>SelectionChanged</c> and the
        /// stale id never got saved over; reconnecting the old monitor then moved the dock back to it
        /// with no warning (#162).</summary>
        internal static List<KeyValuePair<string, string>> BuildDisplayOptions(List<MonitorDescriptor> monitors, string storedId)
        {
            var options = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(PrimaryDisplayId, "Primary display"),
            };
            bool storedIsConnected = false;
            if (monitors != null)
            {
                foreach (var m in monitors)
                {
                    if (string.IsNullOrEmpty(m.Id)) continue;
                    options.Add(new KeyValuePair<string, string>(m.Id, m.Label));
                    // Ordinal, matching ResolveMonitor's own comparison -- not Combo's
                    // OrdinalIgnoreCase, which only decides which entry a combo selects.
                    if (!string.IsNullOrEmpty(storedId) && string.Equals(m.Id, storedId, StringComparison.Ordinal))
                        storedIsConnected = true;
                }
            }
            if (!string.IsNullOrEmpty(storedId) && !storedIsConnected)
            {
                // No real monitor model name here -- just enough for the person to recognize this is
                // their old choice and pick something else, or reconnect it.
                options.Add(new KeyValuePair<string, string>(storedId, "Saved display (not connected)"));
            }
            return options;
        }

        /// <summary>A label like "Display 2 (primary) - 2560 x 1440 - Contoso Display 27", built the same
        /// way for the real monitor list and for a test's invented one.</summary>
        internal static string BuildLabel(int displayNumber, bool isPrimary, int widthPx, int heightPx, string friendlyName)
        {
            string label = "Display " + displayNumber.ToString(CultureInfo.InvariantCulture) +
                (isPrimary ? " (primary)" : "") + " - " +
                widthPx.ToString(CultureInfo.InvariantCulture) + " x " + heightPx.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(friendlyName)) label += " - " + friendlyName.Trim();
            return label;
        }

        /// <summary>The trailing number from a <c>MONITORINFOEX.szDevice</c> name such as
        /// "\\.\DISPLAY2" (2), for the label only -- never the stored identity (see
        /// <see cref="MonitorDescriptor.Id"/>), because Windows can renumber it. Anything with no
        /// trailing digits labels as "Display 0" rather than throwing.</summary>
        internal static int DisplayNumberFromDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return 0;
            int end = deviceName.Length;
            int start = end;
            while (start > 0 && char.IsDigit(deviceName[start - 1])) start--;
            if (start == end) return 0;
            int n;
            return int.TryParse(deviceName.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        /// <summary>Every monitor Windows currently reports, each paired with its stable device-
        /// interface id -- falling back to its own <c>szDevice</c> adapter name if the
        /// <c>EDD_GET_DEVICE_INTERFACE_NAME</c> lookup fails, since an unstable id still beats having
        /// none. Touches the OS; <see cref="ResolveMonitor"/> and <see cref="BuildDisplayOptions"/>
        /// do not, so a test exercises the decision with an invented list instead of real monitors.</summary>
        internal static List<MonitorDescriptor> DescribeMonitors()
        {
            var result = new List<MonitorDescriptor>();
            NM.MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdc, ref NM.RECT clipRect, IntPtr data) =>
            {
                var mi = new NM.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(NM.MONITORINFOEX)) };
                if (!NM.GetMonitorInfo(hMonitor, ref mi)) return true;   // keep enumerating the rest

                bool primary = (mi.dwFlags & NM.MONITORINFOF_PRIMARY) != 0;
                string id = mi.szDevice;
                string friendlyName = null;

                // szDevice ("\\.\DISPLAY2") names the adapter; asking EnumDisplayDevices about that
                // adapter's first monitor (iDevNum 0 -- the dock always targets a whole monitor,
                // never a second display daisy-chained off the same output) gives the stable
                // DeviceID and the friendly DeviceString.
                var dd = new NM.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(NM.DISPLAY_DEVICE)) };
                if (NM.EnumDisplayDevices(mi.szDevice, 0, ref dd, NM.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    if (!string.IsNullOrEmpty(dd.DeviceID)) id = dd.DeviceID;
                    if (!string.IsNullOrWhiteSpace(dd.DeviceString)) friendlyName = dd.DeviceString;
                }

                result.Add(new MonitorDescriptor
                {
                    Id = id,
                    Label = BuildLabel(DisplayNumberFromDeviceName(mi.szDevice), primary,
                        mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top, friendlyName),
                    MonitorRect = mi.rcMonitor,
                    WorkRect = mi.rcWork,
                    IsPrimary = primary,
                });
                return true;
            };
            NM.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            return result;
        }
    }
}
