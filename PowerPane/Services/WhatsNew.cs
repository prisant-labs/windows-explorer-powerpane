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
        public const string Version = "4.3.0";

        public static readonly Item[] Items =
        {
            new Item("A shorter desktop dock",
                "With auto-hide on, Settings > Pane > Desktop dock > Height makes the dock 80, 60 or 40 % of the display's height, centred."),
            new Item("Missing folders dim sooner",
                "A pinned folder that goes missing, such as one on a share that dropped, dims the next time the pane is drawn, and comes back when the folder does."),
            new Item("Real icons on network drives",
                "Network and removable drives, SSH drives, the Linux row, and folders on a share show their real icons once the drive answers."),
        };
    }
}
