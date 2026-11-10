using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using QuickPane.Models;
using QuickPane.Services;

namespace QuickPane.UI
{
    /// <summary>
    /// The pins QuickPane lists in the navigation pane of its own file and folder pickers.
    ///
    /// The pane used to attach to those pickers like any other program's dialog, but a dialog in
    /// QuickPane's own process gets none of the window events that keep a pane in step with its
    /// dialog (the shared hook skips this process), so a move or resize left the pane stale until
    /// the next one-second sweep (prisant-labs/windows-explorer-quickpane#231). The pickers now
    /// carry the pins themselves through IFileDialog::AddPlace, the way Windows lists Quick Access,
    /// and DialogPaneWatcher leaves this process's dialogs alone.
    /// </summary>
    internal static class PickerPlaces
    {
        /// <summary>More places than this makes the picker's navigation pane a second pane.</summary>
        internal const int MaxPlaces = 50;

        /// <summary>The longest the picker waits, in total, for pins to answer before it opens. Every
        /// pin is checked at once, so a pin on a share that does not answer costs this much once and
        /// is left out, rather than costing the network timeout per pin.</summary>
        internal const int ProbeBudgetMs = 400;

        /// <summary>The pins worth offering, in the order the pane shows them: each expanded group's
        /// visible tab, folders only, each folder once. A collapsed group's pins are hidden in the pane,
        /// so they stay out of the picker too. Nothing here touches the disk.</summary>
        internal static List<string> Candidates(IEnumerable<PinnedGroup> groups, int max)
        {
            var result = new List<string>();
            if (groups == null || max <= 0) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (group == null || !group.Expanded) continue;
                var tab = group.Active;
                if (tab == null || tab.Items == null) continue;
                foreach (var pin in tab.Items)
                {
                    if (pin == null || pin.IsFile || !pin.Exists) continue;
                    var path = pin.TargetPath;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!seen.Add(path.TrimEnd('\\', '/'))) continue;
                    result.Add(path);
                    if (result.Count >= max) return result;
                }
            }
            return result;
        }

        /// <summary>The candidates that answered as existing folders within budgetMs, in their original
        /// order. All checks start together on the thread pool, so the wait is bounded by budgetMs no
        /// matter how many pins are slow; a check still running at the deadline counts as "no", and
        /// finishes on its own.</summary>
        internal static List<string> Reachable(IList<string> candidates, Func<string, bool> exists, int budgetMs)
        {
            var result = new List<string>();
            if (candidates == null || candidates.Count == 0) return result;
            var check = exists ?? Directory.Exists;

            var checks = new Task<bool>[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                var path = candidates[i];
                checks[i] = Task.Run(() => { try { return check(path); } catch { return false; } });
            }
            try { Task.WaitAll(checks, Math.Max(0, budgetMs)); } catch { }

            for (int i = 0; i < checks.Length; i++)
                if (checks[i].Status == TaskStatus.RanToCompletion && checks[i].Result) result.Add(candidates[i]);
            return result;
        }

        /// <summary>The places for a picker opening now, read from the live groups. Runs on the UI
        /// thread, where the groups are written, and waits at most ProbeBudgetMs.</summary>
        internal static List<string> ForPicker()
        {
            try
            {
                var groups = App.Groups != null ? App.Groups.Groups : null;
                return Reachable(Candidates(groups, MaxPlaces), null, ProbeBudgetMs);
            }
            catch (Exception ex)
            {
                // A picker without pins is still a picker; never let this stop it opening.
                Log.Error("collecting pins for a picker", ex);
                return new List<string>();
            }
        }

        /// <summary>Add the pins to one of WPF's file dialogs. WPF passes each to AddPlace itself, and
        /// skips a place that fails.</summary>
        internal static void AddTo(Microsoft.Win32.FileDialog dialog)
        {
            if (dialog == null) return;
            foreach (var path in ForPicker())
            {
                try { dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(path)); }
                catch (Exception ex) { Log.Error("adding a pin to a file dialog", ex); }
            }
        }
    }
}
