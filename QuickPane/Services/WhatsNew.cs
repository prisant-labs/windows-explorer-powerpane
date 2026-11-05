namespace QuickPane.Services
{
    /// <summary>The short summary of this version on the About page (#208). It ships inside the
    /// program rather than arriving over the network, because the update check is QuickPane's only
    /// request of its own, and because this way it also works offline.
    ///
    /// Update this at every release, as docs/DEVELOPMENT.md's release steps say. A unit test fails
    /// when <see cref="Version"/> differs from the assembly's version, so a release cannot ship the
    /// last release's summary by accident. Keep it to a few lines: the full notes are one click away.</summary>
    internal static class WhatsNew
    {
        internal struct Item
        {
            public readonly string Title;
            public readonly string Summary;

            public Item(string title, string summary)
            {
                Title = title;
                Summary = summary;
            }
        }

        /// <summary>The version these lines describe, spelled as its release tag spells it after the "v".</summary>
        public const string Version = "3.12.0.1";

        public static readonly Item[] Items =
        {
            new Item("Lighter with many pins",
                "A pinned folder's right-click menu is built when it opens, so many pins and windows use far less memory."),
            new Item("What's new on About",
                "This card: a short summary of each version, with a link to its full notes."),
            new Item("Clearer settings rows",
                "Pane sections and Quick actions rows have their own icons, an info button, and more room around the switch."),
            new Item("Remote drives",
                "The SSH section is called Remote drives everywhere. A header you renamed keeps your name."),
            new Item("Backup & restore",
                "The card stays at its own height instead of stretching to fill the page."),
        };
    }
}
