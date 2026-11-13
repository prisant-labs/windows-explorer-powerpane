using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerPane.Services
{
    /// <summary>
    /// Checks a tab or group name as it is typed, so the prompt can say what is wrong instead of
    /// GroupStore quietly removing characters (#243). A tab is a folder, so its name follows folder-name
    /// rules. The store still sanitizes whatever reaches it.
    /// </summary>
    internal static class TabNameRules
    {
        internal const string InvalidCharactersMessage = "A name cannot contain \\ / : * ? \" < > |";
        internal const string TrailingMessage = "A name cannot end with a dot or a space.";

        /// <summary>Null when the name is acceptable, otherwise the message to show. A blank name returns
        /// null too: the prompt keeps OK disabled for a blank name without a message.
        /// <paramref name="otherNames"/> are the names already taken, compared ignoring case; pass null
        /// for a group name, which has no duplicate rule.</summary>
        internal static string Validate(string input, IEnumerable<string> otherNames)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            if (input.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return InvalidCharactersMessage;

            char last = input[input.Length - 1];
            if (last == '.' || last == ' ') return TrailingMessage;

            if (otherNames != null)
            {
                // Compared as the store would write it, so what is blocked is what would collide.
                string mine = GroupStore.SanitizeName(input);
                foreach (var other in otherNames)
                {
                    if (other == null) continue;
                    string existing = GroupStore.SanitizeName(other);
                    // Names the tab that is already there, so the message shows its spelling, not the typed one.
                    if (string.Equals(existing, mine, StringComparison.OrdinalIgnoreCase))
                        return "This group already has a tab named \"" + existing + "\".";
                }
            }
            return null;
        }
    }
}
