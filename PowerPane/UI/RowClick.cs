using System;
using System.Windows;
using System.Windows.Input;
using PowerPane.Interop;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>
    /// Carries out the click action the user chose (#269) for any pane row that stands for a folder or
    /// a file: the tree rows (pins, Recent, drives) and the App Folders rows in a file dialog's pane.
    /// One place, so every row offers the same actions and none can miss a later change. The choice is
    /// read at click time, so a change in Settings applies to the next click with no rebuild.
    /// </summary>
    internal static class RowClick
    {
        /// <summary>Wires press and release tracking onto <paramref name="row"/>, and calls
        /// <paramref name="onGesture"/> only for a release that completes a press begun on the row
        /// (see <see cref="RowPress"/>). A child that marks its own release handled, such as the
        /// expand caret, keeps its click, because a handled release never reaches the row.</summary>
        public static void Track(UIElement row, Action<ClickGesture> onGesture)
        {
            var press = new RowPress();
            row.MouseEnter += (s, e) => press.Cancel();
            row.MouseLeave += (s, e) => press.Cancel();
            row.MouseDown += (s, e) =>
            {
                press.Down(e.ChangedButton);
                // Nothing else in the pane takes a middle press, and leaving it unhandled would let a
                // parent scroll viewer treat it as its own.
                if (e.ChangedButton == MouseButton.Middle) e.Handled = true;
            };
            row.MouseUp += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle) return;
                if (!press.Up(e.ChangedButton)) return;
                var gesture = ClickActions.GestureFor(e.ChangedButton, Keyboard.Modifiers);
                if (!gesture.HasValue) return;
                if (e.ChangedButton == MouseButton.Middle) e.Handled = true;
                onGesture(gesture.Value);
            };
        }

        /// <summary>Runs the chosen action for a folder or file row.</summary>
        public static void Run(DependencyObject row, ClickGesture gesture, string path, bool exists, bool isFile,
            Action<string> navigate)
        {
            try
            {
                var settings = App.Settings != null ? App.Settings.Current : null;
                string action = settings != null
                    ? ClickActions.Resolve(settings, gesture, isFile)
                    : (gesture == ClickGesture.Middle ? ClickActions.None : ClickActions.Open);
                var pane = SidebarControl.Owning(row);
                bool inDialog = pane != null && !string.IsNullOrEmpty(pane.HostApp);

                switch (ClickActions.OutcomeFor(action, isFile, exists, inDialog))
                {
                    case ClickOutcome.OpenHere:
                        if (navigate != null) navigate(path);
                        break;
                    case ClickOutcome.OpenNewWindow:
                        // Starting explorer.exe is process work, so it leaves the UI thread on the lane
                        // for navigations the user clicked, as the quick switcher's new window does.
                        WorkQueue.PostNavigation(() => ExplorerNavigator.OpenNewWindow(path));
                        break;
                    case ClickOutcome.OpenParent:
                    case ClickOutcome.OpenContainingFolder:
                        var parent = QuickActionLibrary.ParentPath(path);
                        if (!string.IsNullOrEmpty(parent) && navigate != null) navigate(parent);
                        break;
                    case ClickOutcome.OpenFile:
                        OpenFile(path);
                        break;
                    case ClickOutcome.CopyPath:
                        try { Clipboard.SetText(path); }
                        catch (Exception ex) { Log.Error("copy path on click", ex); }
                        break;
                    case ClickOutcome.QuickAction:
                        RunQuickAction(settings, action, path);
                        break;
                }
            }
            catch (Exception ex) { Log.Error("row click", ex); }
        }

        /// <summary>Opens a file in its default program, as a file row's left click always has.</summary>
        public static void OpenFile(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("open '" + path + "'", ex); }
        }

        private static void RunQuickAction(AppSettings settings, string action, string folder)
        {
            string why;
            var def = ClickActions.RunnableQuickAction(settings, action, out why);
            if (def != null && !QuickActionsSection.IsAvailable(def))
            {
                why = "the program the quick action \"" + def.Name + "\" needs is not installed";
                def = null;
            }
            if (def == null)
            {
                Log.Event("click", "a click on a row ran nothing, because " + why + ".");
                return;
            }
            // Same as the pinned row's Quick actions submenu: the folder is already known, and the
            // action's own disk or process work runs on the general worker, never the UI thread.
            WorkQueue.Post(() => QuickActionRunner.Run(def, folder, null));
        }
    }
}
