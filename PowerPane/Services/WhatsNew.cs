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
        public const string Version = "4.2.4";

        public static readonly Item[] Items =
        {
            new Item("Explorer's tree stays visible while you resize",
                "Dragging an Explorer window's border no longer slides Explorer's own navigation pane under PowerPane. PowerPane steps aside for the drag and comes back beside the tree."),
            new Item("Steadier file dialogs and beside pane",
                "Resizing an Open or Save dialog by its corner grip keeps PowerPane inside the dialog, and a beside pane stays up while you drag its window's right edge."),
            new Item("The desktop dock keeps its place",
                "Changing a display's scale no longer leaves the desktop dock wider or narrower than the strip of screen it reserves."),
        };
    }
}
