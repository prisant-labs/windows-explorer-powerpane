using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerPane.UI.Settings
{
    /// <summary>One thing the search box can find: a page, or a card inside a page. Every page
    /// declares its entries through <see cref="SettingsPage.SearchEntries"/>, which runs with no
    /// <c>SettingsStore</c>, no <c>GroupStore</c>, and no window, so the whole index can be built,
    /// and tested, before Settings is ever opened.</summary>
    internal sealed class SettingsSearchEntry
    {
        /// <summary>The page this entry opens, matching some <see cref="SettingsPage.Id"/>.</summary>
        public string PageId { get; }

        /// <summary>The card inside that page to focus after opening it, or null when the entry
        /// is the page itself (nothing more specific to land on).</summary>
        public string CardId { get; }

        public string Title { get; }
        public string Description { get; }
        public string[] Keywords { get; }

        public SettingsSearchEntry(string pageId, string cardId, string title, string description, params string[] keywords)
        {
            if (string.IsNullOrEmpty(pageId)) throw new ArgumentException("pageId is required", nameof(pageId));
            if (string.IsNullOrEmpty(title)) throw new ArgumentException("title is required", nameof(title));
            PageId = pageId;
            CardId = cardId;
            Title = title;
            Description = description ?? string.Empty;
            Keywords = keywords ?? Array.Empty<string>();
        }
    }

    /// <summary>Builds the search index from every registered page's entries and answers queries
    /// against it. A pure class with no UI, so it is covered by ordinary unit tests.</summary>
    internal static class SettingsSearchIndex
    {
        /// <summary>Flattens every page's <see cref="SettingsPage.SearchEntries"/> into one list, in
        /// page order. Calling this needs nothing but the page list itself.</summary>
        public static List<SettingsSearchEntry> Build(IEnumerable<SettingsPage> pages)
        {
            if (pages == null) return new List<SettingsSearchEntry>();
            return pages.SelectMany(p => p.SearchEntries ?? Enumerable.Empty<SettingsSearchEntry>()).ToList();
        }

        /// <summary>True when <paramref name="query"/> (case-insensitive, trimmed) appears in the
        /// entry's title, description, or any keyword. An empty query matches nothing, so the
        /// results list is empty rather than everything until the person actually types.</summary>
        public static bool Matches(SettingsSearchEntry entry, string query)
        {
            if (entry == null) return false;
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0) return false;

            if (ContainsOrdinal(entry.Title, q)) return true;
            if (ContainsOrdinal(entry.Description, q)) return true;
            for (int i = 0; i < entry.Keywords.Length; i++)
            {
                if (ContainsOrdinal(entry.Keywords[i], q)) return true;
            }
            return false;
        }

        /// <summary>Searches <paramref name="entries"/> for <paramref name="query"/>, ranking a
        /// title hit above a keyword or description hit, and within a rank keeping the index's own
        /// order (so page order, not alphabetical order, decides ties).</summary>
        public static List<SettingsSearchEntry> Search(IEnumerable<SettingsSearchEntry> entries, string query)
        {
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0 || entries == null) return new List<SettingsSearchEntry>();

            return entries
                .Select((e, i) => new { Entry = e, Index = i, Rank = Rank(e, q) })
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .ThenBy(x => x.Index)
                .Select(x => x.Entry)
                .ToList();
        }

        /// <summary>0 = title hit, 1 = description hit, 2 = keyword-only hit, -1 = no match.</summary>
        internal static int Rank(SettingsSearchEntry entry, string query)
        {
            if (entry == null) return -1;
            if (ContainsOrdinal(entry.Title, query)) return 0;
            if (ContainsOrdinal(entry.Description, query)) return 1;
            for (int i = 0; i < entry.Keywords.Length; i++)
            {
                if (ContainsOrdinal(entry.Keywords[i], query)) return 2;
            }
            return -1;
        }

        private static bool ContainsOrdinal(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
