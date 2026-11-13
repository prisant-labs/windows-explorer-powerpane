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

        /// <summary>Orders pins for display. Ties always fall back to the stored order and then the
        /// shortcut path, so equal keys never reshuffle between scans.</summary>
        public static List<PinnedFolder> Order(IEnumerable<PinnedFolder> pins, PinSortMode mode)
        {
            var list = (pins ?? Enumerable.Empty<PinnedFolder>()).ToList();
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
    }
}
