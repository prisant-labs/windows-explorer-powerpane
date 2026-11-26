namespace PowerPane.Services
{
    /// <summary>The short summary of this version on the About page (#208). It ships inside the
    /// program rather than arriving over the network, because the update check is PowerPane's only
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
        public const string Version = "4.4.0";

        public static readonly Item[] Items =
        {
            new Item("Keep a profile out of the profile row",
                "Turn off Show in the profile row on a profile's page under Settings > Groups & profiles. The tray menu still lists every profile."),
            new Item("Panes follow a light or dark switch",
                "Switching Windows between light and dark mode no longer leaves an open pane or Settings window unreadable."),
            new Item("Profiles stay separate",
                "A new profile always gets its own groups folder, and removing a profile no longer switches you to a different one."),
        };
    }
}
