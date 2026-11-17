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
        public const string Version = "4.1.1";

        public static readonly Item[] Items =
        {
            new Item("Support QuickPane's author",
                "A link below the credit on this page opens the donation page of PlexPixel, who created QuickPane, the app PowerPane began from."),
            new Item("New in 4.1.0",
                "The Add to PowerPane verb works on files and drives, each group has a + menu and Sort pins, and five quick actions flip Explorer's Show options."),
        };
    }
}
