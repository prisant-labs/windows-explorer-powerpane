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
        public const string Version = "3.12.0.0";

        public static readonly Item[] Items =
        {
            new Item("Update notice",
                "One notice for each new version, an item in the tray menu, and a dot on About."),
            new Item("Ten more quick actions",
                "New ways to copy a folder, new starter files, Open PowerShell here, Open WSL here, and Pin this folder. All start off."),
            new Item("Custom commands",
                "Name a command, give it a template such as code {path}, and it becomes a row in the pane."),
            new Item("Selection actions",
                "Eight actions that copy or open the files selected in Explorer. All start off."),
            new Item("Dock grip with a maximized window",
                "The width grip of a reserved desktop dock can be dragged while a window is maximized."),
        };
    }
}
