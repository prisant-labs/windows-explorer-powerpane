using System;
using System.Collections.Generic;
using System.Linq;
using PowerPane.Interop;
using PowerPane.Models;

namespace PowerPane.Services
{
    /// <summary>
    /// Display ordering for a tab's pins (#241). A tab's mode is kept in a small marker file inside
    /// its folder, so the mode travels with the folder through renames, moves, and profile copies.
    /// The sort is applied when the tab is scanned and never rewrites the NNN_ prefixes by itself.
    /// </summary>
    internal static class PinSorter
    {
        public const string MarkerFileName = ".pane-sort";

        /// <summary>Reads the text of a marker file. Null, empty, or unknown text is Manual.</summary>
        public static PinSortMode ParseMarker(string text)
        {
            var t = (text ?? string.Empty).Trim();
            if (string.Equals(t, "name", StringComparison.OrdinalIgnoreCase)) return PinSortMode.Name;
            if (string.Equals(t, "recent", StringComparison.OrdinalIgnoreCase)) return PinSortMode.Recent;
            return PinSortMode.Manual;
        }

        /// <summary>The text to write into the marker, or null for Manual, which has no marker.</summary>
        public static string MarkerText(PinSortMode mode)
        {
            switch (mode)
            {
                case PinSortMode.Name: return "name";
                case PinSortMode.Recent: return "recent";
                default: return null;
            }
        }

        /// <summary>Natural, case-insensitive comparison, so "Item 2" sorts before "Item 10". It uses
        /// the shell's own comparison, so the pane agrees with Explorer.</summary>
        public static int CompareNames(string a, string b)
        {
            a = a ?? string.Empty;
            b = b ?? string.Empty;
            int c = NativeMethods.StrCmpLogicalW(a, b);
            return c != 0 ? c : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Comparer<PinnedFolder> ByName =
            Comparer<PinnedFolder>.Create((x, y) => CompareNames(x.DisplayName, y.DisplayName));

        /// <summary>Orders pins for display. Pins pinned to top come first, then the rest, and the
        /// tab's own mode orders each part (#301). Ties always fall back to the stored order and then
        /// the shortcut path, so equal keys never reshuffle between scans.</summary>
        public static List<PinnedFolder> Order(IEnumerable<PinnedFolder> pins, PinSortMode mode)
        {
            var list = (pins ?? Enumerable.Empty<PinnedFolder>()).ToList();
            var result = OrderPart(list.Where(p => p.PinnedToTop), mode);
            result.AddRange(OrderPart(list.Where(p => !p.PinnedToTop), mode));
            return result;
        }

        private static List<PinnedFolder> OrderPart(IEnumerable<PinnedFolder> list, PinSortMode mode)
        {
            switch (mode)
            {
                case PinSortMode.Name:
                    return list.OrderBy(p => p, ByName)
                        .ThenBy(p => p.Order)
                        .ThenBy(p => p.LinkPath, StringComparer.OrdinalIgnoreCase).ToList();
                case PinSortMode.Recent:
                    return list.OrderByDescending(p => p.AddedUtc)
                        .ThenBy(p => p, ByName)
                        .ThenBy(p => p.Order)
                        .ThenBy(p => p.LinkPath, StringComparer.OrdinalIgnoreCase).ToList();
                default:
                    return list.OrderBy(p => p.Order)
                        .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        /// <summary>The order after dropping a pin at a line. lineIndex counts the rows on screen, and
        /// the dragged row is one of them, so a line below it is one further along than it is in the
        /// list without it. Returns null when the drop changes nothing.</summary>
        public static List<PinnedFolder> PlaceAtLine(IList<PinnedFolder> shown, PinnedFolder moved, int lineIndex)
        {
            int from = shown.IndexOf(moved);
            if (from < 0) return null;
            var result = shown.Where(p => !ReferenceEquals(p, moved)).ToList();
            int at = lineIndex > from ? lineIndex - 1 : lineIndex;
            if (at < 0) at = 0;
            if (at > result.Count) at = result.Count;
            if (at == from) return null;
            result.Insert(at, moved);
            return result;
        }

        /// <summary>The order after dropping a pin at a line, where the line also decides whether the
        /// pin is pinned to top (#301). With K pins pinned to top once the dragged pin is set aside, a
        /// pinned pin stays pinned when it lands within the first K + 1 places, and an ordinary pin
        /// becomes pinned when it lands within the first K. So a drop just below the last pinned pin
        /// keeps a pinned pin pinned and leaves an ordinary pin ordinary. Only the dragged pin can
        /// change, and when it does the list holds a copy of it carrying the new flag. Returns null
        /// when the drop changes neither the order nor the flag.</summary>
        public static List<PinnedFolder> PlaceAtLineInBands(IList<PinnedFolder> shown, PinnedFolder moved, int lineIndex)
        {
            int from = shown.IndexOf(moved);
            if (from < 0) return null;
            var result = shown.Where(p => !ReferenceEquals(p, moved)).ToList();
            int at = lineIndex > from ? lineIndex - 1 : lineIndex;
            if (at < 0) at = 0;
            if (at > result.Count) at = result.Count;
            bool top = TopAfterDrop(result.Count(p => p.PinnedToTop), moved.PinnedToTop, at);
            if (at == from && top == moved.PinnedToTop) return null;
            result.Insert(at, top == moved.PinnedToTop ? moved : WithTop(moved, top));
            return result;
        }

        /// <summary>Whether a pin dropped at place <paramref name="at"/> is pinned to top, when
        /// <paramref name="pinnedCount"/> pins other than it are. A pin arriving from somewhere else
        /// is ordinary, so it becomes pinned only inside the band.</summary>
        public static bool TopAfterDrop(int pinnedCount, bool wasPinned, int at)
        {
            return wasPinned ? at <= pinnedCount : at < pinnedCount;
        }

        /// <summary>A copy of a pin with its flag set, for a caller that writes the order back.</summary>
        public static PinnedFolder WithTop(PinnedFolder p, bool top)
        {
            return new PinnedFolder
            {
                DisplayName = p.DisplayName, TargetPath = p.TargetPath, LinkPath = p.LinkPath,
                Order = p.Order, PinnedToTop = top, Exists = p.Exists, IsFile = p.IsFile, AddedUtc = p.AddedUtc
            };
        }
    }
}
