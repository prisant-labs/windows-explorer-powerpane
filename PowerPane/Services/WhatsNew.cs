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
        public const string Version = "4.2.1";

        public static readonly Item[] Items =
        {
            new Item("A level title bar",
                "The icon and title at the top of Settings line up with the minimize, maximize, and close buttons, and a maximized Settings window no longer cuts off its top and left edges."),
            new Item("Third-party notices included",
                "The installer now puts THIRD-PARTY-NOTICES.txt beside PowerPane, with the licence of the Fluent styles this window is built on."),
        };
    }
}
