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
        public const string Version = "4.1.0";

        public static readonly Item[] Items =
        {
            new Item("Add to PowerPane on files and drives",
                "The right-click verb, renamed from \"Pin to PowerPane\", works on files and drives too. It lets you choose a tab, and skips items already in it."),
            new Item("A + menu on each group",
                "The + in a group's tab strip adds a tab, a group, a folder, or a file. A new tab becomes the selected one."),
            new Item("Sort pins",
                "A tab's right-click menu can keep the tab sorted A to Z or by date added, or sort it once."),
            new Item("Explorer's Show options as quick actions",
                "Five quick actions turn hidden items, file name extensions, item check boxes, compact view, and the navigation pane on and off. They start off."),
            new Item("Copy path copies the selection",
                "Copy path and the other folder copies use the items selected in Explorer, one per line, and the folder when nothing is selected."),
            new Item("Select Folder dialogs and name boxes",
                "Clicking a pin in a Select Folder dialog opens the folder instead of choosing it. Name boxes open beside the control you used, in the pane's colours."),
        };
    }
}
