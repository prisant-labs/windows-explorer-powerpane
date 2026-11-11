using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>
    /// The pins PowerPane lists in the navigation pane of its own file and folder pickers.
    ///
    /// The pane used to attach to those pickers like any other program's dialog, but a dialog in
    /// PowerPane's own process gets none of the window events that keep a pane in step with its
    /// dialog (the shared hook skips this process), so a move or resize left the pane stale until
    /// the next one-second sweep (prisant-labs/windows-explorer-powerpane#231). The pickers now
    /// carry the pins themselves through IFileDialog::AddPlace, the way Windows lists Quick Access,
    /// and DialogPaneWatcher leaves this process's dialogs alone.
    /// </summary>
    internal static class PickerPlaces
    {
        /// <summary>More places than this makes the picker's navigation pane a second pane.</summary>
        internal const int MaxPlaces = 50;

        /// <summary>How many pins are checked at most. Some candidates turn out to be files or gone,
        /// so this is larger than MaxPlaces, but bounded so a huge group cannot start hundreds of
        /// checks.</summary>
        internal const int MaxCandidates = 150;

        /// <summary>The longest the picker waits, in total, for pins to answer before it opens. Every
        /// pin is checked at once, so a pin on a share that does not answer costs this much once and
        /// is left out, rather than costing the network timeout per pin.</summary>
        internal const int ProbeBudgetMs = 400;

        /// <summary>The pins worth checking, in the order the pane shows them: each expanded group's
        /// visible tab, each path once. A collapsed group's pins are hidden in the pane, so they stay
        /// out of the picker too. Nothing here touches the disk, and nothing here trusts a pin's
        /// IsFile or Exists: those are scan-time snapshots, and before its first probe IsFile is a
        /// guess from the extension, which would drop a folder named like "Site.v2". Reachable's
        /// folder check is what removes files and missing targets.</summary>
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
                    if (pin == null) continue;
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
        /// order, at most max of them. Each drive or share is checked on its own thread, all at once,
        /// so the wait is bounded by budgetMs however many pins are slow, and a share that does not
        /// answer holds up only its own pins. A check still running at the deadline counts as "no",
        /// and finishes on its own.
        ///
        /// Not the thread pool: with one pool task per pin, pins on hung shares took every pool
        /// thread on a two-core machine, and a local pin queued behind them missed the deadline.
        /// </summary>
        internal static List<string> Reachable(IList<string> candidates, Func<string, bool> exists, int budgetMs, int max)
        {
            var result = new List<string>();
            if (candidates == null || candidates.Count == 0 || max <= 0) return result;
            var check = exists ?? Directory.Exists;

            // 0 = not answered yet, 1 = a folder, 2 = not a folder. Written by the volume threads,
            // read here after the wait; a slot still 0 at the deadline is a "no".
            var answers = new int[candidates.Count];
            var tasks = new List<Task>();
            foreach (var volume in ByVolume(candidates))
            {
                var indexes = volume;
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    foreach (var i in indexes)
                    {
                        bool ok;
                        try { ok = check(candidates[i]); } catch { ok = false; }
                        Volatile.Write(ref answers[i], ok ? 1 : 2);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
            try { Task.WaitAll(tasks.ToArray(), Math.Max(0, budgetMs)); } catch { }

            for (int i = 0; i < answers.Length && result.Count < max; i++)
                if (Volatile.Read(ref answers[i]) == 1) result.Add(candidates[i]);
            return result;
        }

        /// <summary>Candidate indexes grouped by drive or share root, in first-seen order. A path
        /// whose root cannot be read gets a group of its own.</summary>
        internal static List<List<int>> ByVolume(IList<string> candidates)
        {
            var groups = new List<List<int>>();
            var byRoot = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                string root = null;
                try { root = Path.GetPathRoot(candidates[i]); } catch { }
                List<int> group;
                if (string.IsNullOrEmpty(root) || !byRoot.TryGetValue(root, out group))
                {
                    group = new List<int>();
                    groups.Add(group);
                    if (!string.IsNullOrEmpty(root)) byRoot[root] = group;
                }
                group.Add(i);
            }
            return groups;
        }

        /// <summary>The places for a picker opening now, read from the live groups. Runs on the UI
        /// thread, where the groups are written, and waits at most ProbeBudgetMs.</summary>
        internal static List<string> ForPicker()
        {
            try
            {
                var groups = App.Groups != null ? App.Groups.Groups : null;
                return Reachable(Candidates(groups, MaxCandidates), null, ProbeBudgetMs, MaxPlaces);
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
