using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Interop
{
    /// <summary>How reading an Explorer selection went. Ok with no paths means nothing is selected.</summary>
    internal enum SelectionReadStatus { Ok, TooMany, Failed }

    /// <summary>
    /// Drives a specific Explorer window from outside its process. It uses the Shell.Application
    /// automation object to find the ShellBrowserWindow whose top-level HWND matches the window the
    /// pane is embedded in, then navigates it in place, which is equivalent to the user clicking a
    /// folder in the native nav pane.
    ///
    /// Every call here is late bound through Type.InvokeMember rather than the C# dynamic keyword.
    /// The dynamic path routes COM calls through the DLR, which caches a wrapper against each runtime
    /// callable wrapper using Marshal.SetComObjectData, and that cache is invalidated by the explicit
    /// Marshal.ReleaseComObject calls this class needs to make. The two together threw
    /// "InvalidOperationException: Marshal.SetComObjectData failed" out of ComObject.ObjectToComObject,
    /// which is what silently dropped folder clicks and Recent updates. InvokeMember reaches IDispatch
    /// directly, so no such cache exists and releasing an object cannot poison a later call.
    /// </summary>
    internal static class ExplorerNavigator
    {
        /// <summary>Navigate on the navigation lane. Enumerating shell windows is cross-process COM
        /// that blocks for as long as Explorer is busy, and the UI thread shares Explorer's input queue
        /// in Inside mode, so this must never run there.</summary>
        public static void NavigateAsync(IntPtr topLevelHwnd, string path)
        {
            WorkQueue.PostNavigation(() => Navigate(topLevelHwnd, path));
        }

        // 0x800700AA is ERROR_BUSY surfaced through COM: the window is mid-operation rather than wrong
        // or gone, so the call is worth making again once the shell has had a moment.
        private const int HResultResourceInUse = unchecked((int)0x800700AA);

        /// <summary>Drive Navigate2, retrying once when the shell reports the window busy. Giving up on
        /// a busy window left the click doing nothing at all.</summary>
        private static bool Navigate2WithRetry(object w, string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Call(w, "Navigate2", path);
                    return true;
                }
                catch (Exception ex) when (attempt == 0 && IsBusy(ex))
                {
                    System.Threading.Thread.Sleep(250);
                }
            }
        }

        private static bool IsBusy(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (System.Runtime.InteropServices.Marshal.GetHRForException(e) == HResultResourceInUse)
                    return true;
            return false;
        }

        /// <summary>Navigate the Explorer window identified by topLevelHwnd to path, in place.</summary>
        public static bool Navigate(IntPtr topLevelHwnd, string path)
        {
            long traced = PerfTrace.ShellCallBegin("ExplorerNavigate");
            bool ok = false;
            try { ok = NavigateCore(topLevelHwnd, path); return ok; }
            finally { PerfTrace.ShellCallEnd("ExplorerNavigate", traced, ok); }
        }

        private static bool NavigateCore(IntPtr topLevelHwnd, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            Log.Activity("explorer navigate to " + path);

            object shell = null, windows = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    Log.Event("navigate", "Shell.Application is not registered, so in-place navigation is unavailable.");
                    return false;
                }
                shell = Activator.CreateInstance(shellType);
                windows = Call(shell, "Windows");
                if (windows == null) return false;

                object w = null;
                try
                {
                    w = FindActiveTab(windows, topLevelHwnd);
                    if (w == null)
                    {
                        Log.Event("navigate", "no Explorer window matched hwnd " + topLevelHwnd.ToString("X") +
                            ", so '" + path + "' was not opened in place.");
                        return false;
                    }
                    return Navigate2WithRetry(w, path);
                }
                catch (Exception ex)
                {
                    Log.Event("navigate", "Navigate2 failed for '" + path + "'", ex);
                    return false;
                }
                finally { Release(w); }
            }
            catch (Exception ex)
            {
                Log.Event("navigate", "in-place navigation failed for '" + path + "'", ex);
                return false;
            }
            finally
            {
                Release(windows);
                Release(shell);
            }
        }

        /// <summary>The folder currently shown in the Explorer window with this top-level HWND, or null.</summary>
        public static string GetCurrentPath(IntPtr topLevelHwnd)
        {
            long traced = PerfTrace.ShellCallBegin("ExplorerCurrentPath");
            string path = null;
            try { path = GetCurrentPathCore(topLevelHwnd); return path; }
            finally { PerfTrace.ShellCallEnd("ExplorerCurrentPath", traced, path != null); }
        }

        private static string GetCurrentPathCore(IntPtr topLevelHwnd)
        {
            object shell = null, windows = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;
                shell = Activator.CreateInstance(shellType);
                windows = Call(shell, "Windows");
                if (windows == null) return null;

                object w = null;
                try
                {
                    w = FindActiveTab(windows, topLevelHwnd);
                    return w == null ? null : PathOf(w);
                }
                finally { Release(w); }
            }
            catch (Exception ex) { Log.Error("GetCurrentPath failed", ex); return null; }
            finally
            {
                Release(windows);
                Release(shell);
            }
        }

        /// <summary>More items than this is refused rather than read, because each one costs two
        /// cross-process calls, and a quietly shortened list would act on the wrong set of files.</summary>
        internal const int MaxSelectedItems = 1000;

        /// <summary>Reads the items selected in the active tab of this Explorer window into
        /// <paramref name="paths"/>, in the view's order (#191). Cross-process COM, so a caller is
        /// already off the UI thread. It runs only when a row is clicked, never to keep the pane up to
        /// date: watching a selection cheaply is unsolved, and polling would undo the performance work.
        ///
        /// A failed read is its own answer, and never an empty list. A custom command falls back to the
        /// folder when nothing is selected, so reading a failure as "nothing selected" would point a
        /// command meant for one file at the whole folder. A selected item with no place on disk fails
        /// the read for the same reason: acting on the rest would be a set the user did not choose.</summary>
        public static SelectionReadStatus GetSelectedPaths(IntPtr topLevelHwnd, out List<string> paths)
        {
            long traced = PerfTrace.ShellCallBegin("ExplorerSelection");
            var status = SelectionReadStatus.Failed;
            try { status = GetSelectedPathsCore(topLevelHwnd, out paths); return status; }
            finally { PerfTrace.ShellCallEnd("ExplorerSelection", traced, status == SelectionReadStatus.Ok); }
        }

        private static SelectionReadStatus GetSelectedPathsCore(IntPtr topLevelHwnd, out List<string> paths)
        {
            paths = new List<string>();
            object shell = null, windows = null, w = null, doc = null, items = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return SelectionReadStatus.Failed;
                shell = Activator.CreateInstance(shellType);
                windows = Call(shell, "Windows");
                if (windows == null) return SelectionReadStatus.Failed;
                w = FindActiveTab(windows, topLevelHwnd);
                doc = Get(w, "Document");
                items = Call(doc, "SelectedItems");
                if (items == null) return SelectionReadStatus.Failed;

                int count = ToInt(Get(items, "Count"));
                if (count > MaxSelectedItems) return SelectionReadStatus.TooMany;
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = Call(items, "Item", i);
                        var p = Get(item, "Path") as string;
                        if (!IsFileSystemPath(p))
                        {
                            paths.Clear();
                            return SelectionReadStatus.Failed;
                        }
                        paths.Add(p);
                    }
                    finally { Release(item); }
                }
                return SelectionReadStatus.Ok;
            }
            catch (Exception ex)
            {
                Log.Error("GetSelectedPaths failed", ex);
                paths.Clear();
                return SelectionReadStatus.Failed;
            }
            finally
            {
                Release(items);
                Release(doc);
                Release(w);
                Release(windows);
                Release(shell);
            }
        }

        /// <summary>False for a shell item with no place on disk, such as a Control Panel entry, whose
        /// "path" is a "::{GUID}" parsing name. No program could do anything with that.</summary>
        internal static bool IsFileSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            return path.Length >= 3 && path[1] == ':' && path[2] == '\\' && char.IsLetter(path[0]);
        }

        /// <summary>
        /// The shell window for the tab in front of the Explorer window with this top-level HWND, or
        /// null. The caller releases it.
        ///
        /// Shell.Application.Windows() lists one entry per Windows 11 tab, and every tab reports the
        /// top-level CabinetWClass handle as its HWND, so taking the first match drove the oldest tab
        /// whichever tab was in front (prisant-labs/windows-explorer-powerpane#35). Each tab's own
        /// ShellTabWindowClass handle comes from its IShellBrowser, and the active tab is the topmost
        /// ShellTabWindowClass child. ExplorerWindow lays the tabs out with SWP_NOZORDER, so it never
        /// disturbs that order. A window with at most one tab, which is every Windows 10 window and
        /// most Windows 11 windows, stops at the first match instead of reading the rest of the list,
        /// because reading every entry costs two cross-process calls per Explorer tab on the desktop
        /// (prisant-labs/windows-explorer-powerpane#40). The tab in front is read again after the list,
        /// because the list can take long enough for the user to switch tabs
        /// (prisant-labs/windows-explorer-powerpane#72). The one-tab shortcut can still miss a tab
        /// opened while the list is read; the next click or Recent sample sees it.
        /// </summary>
        private static object FindActiveTab(object windows, IntPtr topLevelHwnd)
        {
            long target = topLevelHwnd.ToInt64();
            var matches = new List<object>();
            try
            {
                IntPtr firstTab = NM.FindWindowEx(topLevelHwnd, IntPtr.Zero, "ShellTabWindowClass", null);
                long secondTab = firstTab == IntPtr.Zero ? 0
                    : NM.FindWindowEx(topLevelHwnd, firstTab, "ShellTabWindowClass", null).ToInt64();
                bool firstMatchWins = HasAtMostOneTab(secondTab);
                int count = ToInt(Get(windows, "Count"));
                for (int i = 0; i < count; i++)
                {
                    object w = null;
                    try { w = Call(windows, "Item", i); }
                    catch { }
                    if (w == null) continue;
                    if (HwndOf(w) == target)
                    {
                        matches.Add(w);
                        if (firstMatchWins) break;
                    }
                    else Release(w);
                }
                if (matches.Count == 0) return null;

                int pick = 0;
                if (matches.Count > 1)
                {
                    long active = NM.FindWindowEx(topLevelHwnd, IntPtr.Zero, "ShellTabWindowClass", null).ToInt64();
                    var tabs = new long[matches.Count];
                    for (int i = 0; i < tabs.Length; i++) tabs[i] = TabHwndOf(matches[i]);
                    pick = PickActiveTab(tabs, active);
                }
                var chosen = matches[pick];
                matches[pick] = null;   // handed to the caller, so the finally block must not release it
                return chosen;
            }
            finally
            {
                foreach (var m in matches) Release(m);
            }
        }

        /// <summary>True when the Explorer window has no second ShellTabWindowClass child, so the first
        /// shell window matching its HWND is the only one and the rest of the list need not be read.</summary>
        internal static bool HasAtMostOneTab(long secondTab) { return secondTab == 0; }

        /// <summary>Which of the shell windows sharing one top-level HWND to drive, given each one's
        /// tab handle (0 where the lookup failed) and the active tab's handle (0 when unknown). Falls
        /// back to the first, which is what every build before #35 did, so a failed lookup is never
        /// worse than before. Returns -1 only for an empty list.</summary>
        internal static int PickActiveTab(IList<long> tabHandles, long activeTab)
        {
            if (tabHandles == null || tabHandles.Count == 0) return -1;
            if (activeTab != 0)
                for (int i = 0; i < tabHandles.Count; i++)
                    if (tabHandles[i] == activeTab) return i;
            return 0;
        }

        /// <summary>The ShellTabWindowClass handle of one tab's shell window, or 0. This is the
        /// documented route from a shell window to its browser: QueryService for the top-level
        /// browser, then IOleWindow.GetWindow, which IShellBrowser inherits. Both interfaces marshal
        /// across processes, so this works from PowerPane even though the tab lives in Explorer.</summary>
        private static long TabHwndOf(object w)
        {
            object browser = null;
            try
            {
                var sp = w as IOleServiceProvider;
                if (sp == null) return 0;
                Guid sid = SidSTopLevelBrowser, iid = IidIShellBrowser;
                if (sp.QueryService(ref sid, ref iid, out browser) != 0) return 0;
                var sb = browser as IShellBrowser;
                if (sb == null) return 0;
                IntPtr hwnd;
                return sb.GetWindow(out hwnd) == 0 ? hwnd.ToInt64() : 0;
            }
            catch { return 0; }
            finally { Release(browser); }
        }

        private static readonly Guid SidSTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IidIShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IOleServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
        }

        // Only the first vtable slot is declared. GetWindow comes from IOleWindow, which IShellBrowser
        // extends, and nothing here calls the later methods.
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig]
            int GetWindow(out IntPtr phwnd);
        }

        /// <summary>Read the folder path from a shell window, preferring the folder object and falling
        /// back to the location URL for shells that return nothing from the first route.</summary>
        private static string PathOf(object w)
        {
            object doc = null, folder = null, self = null;
            try
            {
                doc = Get(w, "Document");
                if (doc != null)
                {
                    folder = Get(doc, "Folder");
                    if (folder != null)
                    {
                        self = Get(folder, "Self");
                        if (self != null)
                        {
                            var p = Get(self, "Path") as string;
                            if (!string.IsNullOrEmpty(p)) return p;
                        }
                    }
                }
            }
            catch { }
            finally { Release(self); Release(folder); Release(doc); }

            try
            {
                var url = Get(w, "LocationURL") as string;   // e.g. file:///C:/Demo/Projects
                if (!string.IsNullOrEmpty(url))
                {
                    var u = new Uri(url);
                    if (u.IsFile) return u.LocalPath;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Open a folder in a brand new Explorer window (used by "Open in new window").</summary>
        public static void OpenNewWindow(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Event("navigate", "opening '" + path + "' in a new window failed", ex);
            }
        }

        // ---- late-bound COM helpers -----------------------------------------

        private const BindingFlags CallFlags = BindingFlags.InvokeMethod;
        private const BindingFlags GetFlags = BindingFlags.GetProperty;

        private static object Call(object target, string member, params object[] args)
        {
            if (target == null) return null;
            return target.GetType().InvokeMember(member, CallFlags, null, target, args);
        }

        private static object Get(object target, string member)
        {
            if (target == null) return null;
            return target.GetType().InvokeMember(member, GetFlags, null, target, null);
        }

        /// <summary>The window handle of a shell window. Items that are not Explorer windows, such as an
        /// Internet Explorer frame, throw here rather than returning a handle.</summary>
        private static long HwndOf(object w)
        {
            try { return Convert.ToInt64(Get(w, "HWND")); }
            catch { return 0; }
        }

        private static int ToInt(object v)
        {
            try { return v == null ? 0 : Convert.ToInt32(v); }
            catch { return 0; }
        }

        private static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
            catch { }
        }
    }
}
