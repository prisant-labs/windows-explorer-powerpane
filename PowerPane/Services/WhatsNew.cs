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
        public const string Version = "4.2.2";

        public static readonly Item[] Items =
        {
            new Item("One Copy path for folder clicks",
                "Clicking a folder lists Quick action: Copy path only, without the built-in Copy path that did the same thing. A click you set to Copy path keeps copying."),
            new Item("New in 4.2.1",
                "The title of this window lines up with its caption buttons, and the installer includes THIRD-PARTY-NOTICES.txt."),
        };
    }
}
