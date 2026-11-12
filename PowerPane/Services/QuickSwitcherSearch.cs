using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerPane.Services
{
    /// <summary>What a quick switcher row is. The order is the tiebreak between equal matches: a pin
    /// the user chose outranks a folder Recent noticed, and both outrank a drive or an action.</summary>
    internal enum SwitcherKind { Pin = 0, Recent = 1, Drive = 2, Action = 3 }

    /// <summary>One candidate in the quick switcher: a name to match, the path or action id it leads
    /// to, and where it came from.</summary>
    internal sealed class SwitcherEntry
    {
        public SwitcherKind Kind { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }        // a folder or file; null for an action
        public string ActionId { get; set; }    // a quick action's id; null otherwise
        public bool IsFile { get; set; }
        public string Detail { get; set; }      // shown under the name: the path, or the group, or the action's description
    }

    /// <summary>
    /// The quick switcher's search (#226). It filters entries PowerPane already holds in memory and
    /// never touches the disk or the network, because it runs on every keystroke on the UI thread,
    /// and in inside mode that thread shares Explorer's input queue. Pure, so it is unit-tested.
    /// </summary>
    internal static class QuickSwitcherSearch
    {
        public const int MaxResults = 12;

        /// <summary>Joins the sources in rank order and drops a later entry whose path an earlier one
        /// already has, so a pinned folder that is also recent shows once, as the pin.</summary>
        public static List<SwitcherEntry> Combine(IEnumerable<SwitcherEntry> pins, IEnumerable<SwitcherEntry> recent,
            IEnumerable<SwitcherEntry> drives, IEnumerable<SwitcherEntry> actions)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var all = new List<SwitcherEntry>();
            foreach (var source in new[] { pins, recent, drives, actions })
            {
                if (source == null) continue;
                foreach (var e in source)
                {
                    if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                    if (e.Path != null && !seen.Add(e.Path.TrimEnd('\\'))) continue;
                    all.Add(e);
                }
            }
            return all;
        }

        /// <summary>The entries that match <paramref name="query"/>, best first, at most
        /// <paramref name="limit"/>. An empty query lists pins and Recent in their own order, since
        /// those are the likeliest next stops; drives and actions appear once something is typed.</summary>
        public static List<SwitcherEntry> Rank(string query, IList<SwitcherEntry> entries, int limit = MaxResults)
        {
            if (entries == null) return new List<SwitcherEntry>();
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return entries.Where(e => e.Kind == SwitcherKind.Pin || e.Kind == SwitcherKind.Recent).Take(limit).ToList();

            var terms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<Tuple<int, int, SwitcherEntry>>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int score = Score(terms, e);
                if (score > 0) scored.Add(Tuple.Create(score, i, e));
            }
            return scored
                .OrderByDescending(t => t.Item1)
                .ThenBy(t => (int)t.Item3.Kind)
                .ThenBy(t => t.Item2)
                .Take(limit)
                .Select(t => t.Item3)
                .ToList();
        }

        /// <summary>0 when any term misses. Otherwise each term scores by where it lands, best first:
        /// the start of the name, the start of a word in the name, anywhere in the name, then anywhere
        /// in the path. Every term must match, so "dem proj" finds "C:\Demo\Projects".</summary>
        internal static int Score(IList<string> terms, SwitcherEntry e)
        {
            int total = 0;
            foreach (var term in terms)
            {
                int s = TermScore(term, e.Name, e.Path);
                if (s == 0) return 0;
                total += s;
            }
            return total;
        }

        private static int TermScore(string term, string name, string path)
        {
            name = name ?? string.Empty;
            if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 100;
            int at = name.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (at > 0)
            {
                char before = name[at - 1];
                return char.IsLetterOrDigit(before) && !(char.IsLower(before) && char.IsUpper(name[at])) ? 40 : 70;
            }
            if (!string.IsNullOrEmpty(path) && path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return 10;
            return 0;
        }
    }
}
