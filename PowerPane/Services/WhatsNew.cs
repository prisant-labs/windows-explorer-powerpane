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
        public const string Version = "4.0.0.0";

        public static readonly Item[] Items =
        {
            new Item("QuickPane is now Explorer PowerPane",
                "A new name, with its own program and data folders. Settings and backups copied from QuickPane find your pins in the new folder."),
            new Item("\"Remove pin\"",
                "A pin's menu says \"Remove pin\" instead of \"Remove Shortcut\", the word the rest of the app uses."),
        };
    }
}
