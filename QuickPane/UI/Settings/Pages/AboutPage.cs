using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using QuickPane.Services;

namespace QuickPane.UI.Settings.Pages
{
    // Carries over the footer content from SettingsPanel.xaml.cs (line 1677-1755) at commit
    // 068301b, except for one deliberate change: the update check used to run every time Settings
    // opened, and now runs only when this page opens or its "Check for updates" button is pressed.
    // Do not restore the old timing.
    //
    // SearchEntries lists "update" and "version" as keywords so a search for either finds About;
    // see SettingsSearchIndexTests for the test that pins this down.

    /// <summary>App header, an Updates card (status, "Check for updates", Download when an update
    /// exists), a Links card (source, release notes, report an issue), a Licence card, and a
    /// Third-party notices card. Last item in the navigation pane.</summary>
    internal sealed class AboutPage : SettingsPage
    {
        public override string Id => "about";
        public override string Title => "About";
        public override string Glyph => "";   // Info

        // ---- glyphs -----------------------------------------------------
        //
        // Segoe Fluent Icons code points for the cards below.
        private const string GlyphInfo = "";      // Info (shared with the nav item above)
        private const string GlyphSync = "";      // Sync
        private const string GlyphLink = "";       // Link
        private const string GlyphExternal = "";   // OpenInNewWindow
        private const string GlyphDocument = "";   // Document (licence)
        private const string GlyphCertificate = ""; // Certificate-ish (third-party notices)

        // The fork's own repository, matching UpdateService's own repo (not upstream's: offering
        // upstream's release notes or issue tracker to someone running a fork build would point them
        // at the wrong project).
        private const string RepoUrl = "https://github.com/prisant-labs/windows-explorer-quickpane";
        private const string ReleaseNotesUrl = RepoUrl + "/blob/main/CHANGELOG.md";
        private const string IssueTrackerUrl = RepoUrl + "/issues/new";

        private const string AppDescription = "Pinned folders inside File Explorer and file dialogs";
        private const string LicenceText = "MIT License. Based on QuickPane by PlexPixel.";
        private const string ThirdPartyNoticeText =
            "The Settings window's styles are adapted from Microsoft's WPF Fluent theme (dotnet/wpf, MIT). " +
            "Full notice: QuickPane/Themes/Fluent/NOTICE.md.";

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, "updates", "Check for updates",
                    "See the installed version and check for a newer release.", "update", "version", "upgrade");
                // No single control to land on here: the version sits in the header card, and the
                // licence is its own card below it. Null is the honest answer (FocusTarget simply
                // returns false and the page is still opened) rather than pointing at either one.
                yield return new SettingsSearchEntry(Id, null, "App version and licence",
                    "The installed version, and the MIT licence QuickPane is built on.", "version", "licence", "license");
                yield return new SettingsSearchEntry(Id, "links", "Source code, release notes, report an issue",
                    "Links to the repository, the changelog, and the issue tracker.", "github", "changelog", "issue");
            }
        }

        /// <summary>The update check the Updates card runs on build and on "Check for updates".
        /// A test that builds this page swaps it out, because the real one calls GitHub (#120).</summary>
        internal Action<Action<UpdateCheckResult>> CheckForUpdates { get; set; } = UpdateService.CheckAsync;

        public override UIElement Build(SettingsPageContext ctx)
        {
            var panel = new StackPanel();
            panel.Children.Add(BuildHeaderCard(ctx));
            panel.Children.Add(BuildUpdatesCard(ctx, CheckForUpdates));
            panel.Children.Add(BuildLinksCard(ctx));
            panel.Children.Add(ctx.Cards.Card("licence", GlyphDocument, "Licence", LicenceText, null));
            panel.Children.Add(ctx.Cards.Card("third-party-notices", GlyphCertificate, "Third-party notices", ThirdPartyNoticeText, null));
            return panel;
        }

        // ---- header card --------------------------------------------------

        /// <summary>App name, the running build's version (read from the assembly, same source the
        /// old footer used), and a one-line description.</summary>
        private static FrameworkElement BuildHeaderCard(SettingsPageContext ctx)
        {
            var version = new TextBlock { Text = "Version " + UpdateService.CurrentDisplay, FontSize = 12 };
            version.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return ctx.Cards.Card("about-app", GlyphInfo, "QuickPane", AppDescription, version,
                accessibleName: "QuickPane version " + UpdateService.CurrentDisplay);
        }

        // ---- updates card ---------------------------------------------------

        /// <summary>Carries over the old footer's version/update-check UI (SettingsPanel.xaml.cs,
        /// line 1677-1755), with one behaviour change: the check no longer runs every time
        /// Settings opens. It runs once when this page builds, and again whenever "Check for
        /// updates" is pressed, through the same <see cref="UpdateService"/> and off the UI thread
        /// (<see cref="UpdateService.CheckAsync"/> posts the HTTP call to <c>WorkQueue</c> itself).
        ///
        /// Built by hand rather than through <see cref="SettingsCards.Card"/>: that builder bakes
        /// its description into a <see cref="TextBlock"/> the caller never sees again, but the
        /// status text here has to change in place as a check completes. <see cref="SettingsCards.CardShell"/>
        /// is `internal` for exactly this kind of free-form card body, and <see cref="SettingsCards.Register"/>
        /// is public, so this builds its own three-column layout (mirroring the shared card's icon /
        /// text / control shape) and registers it itself instead of asking the page contract for a
        /// new card-building overload.</summary>
        private static FrameworkElement BuildUpdatesCard(SettingsPageContext ctx, Action<Action<UpdateCheckResult>> checkForUpdates)
        {
            var icon = GlyphText(GlyphSync, 20);
            icon.Margin = new Thickness(0, 0, 20, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;

            var title = new TextBlock { Text = "Updates", FontSize = 14 };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var status = new TextBlock { Text = "Checking for updates…", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(status);

            var checkButton = new Button { Content = "Check for updates" };
            AutomationProperties.SetName(checkButton, "Check for updates");

            var downloadButton = new Button { Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(downloadButton, "Download the latest QuickPane release");

            // Wired once, here, rather than inside the check callback: the callback below runs again
            // every time the button is pressed, and attaching a new Click handler on every result
            // would stack handlers and open one browser tab per past check. downloadUrl is the one
            // thing the handler needs, captured by reference and assigned by the latest result.
            string downloadUrl = null;
            downloadButton.Click += (s, e) => { if (downloadUrl != null) OpenUrl(downloadUrl); };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(checkButton);
            buttons.Children.Add(downloadButton);
            buttons.Margin = new Thickness(16, 0, 0, 0);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(icon);
            grid.Children.Add(text);
            grid.Children.Add(buttons);

            Action runCheck = () =>
            {
                status.Text = "Checking for updates…";
                status.ToolTip = null;
                checkButton.IsEnabled = false;
                downloadButton.Visibility = Visibility.Collapsed;

                checkForUpdates(r =>
                {
                    // The page may have navigated away (or rebuilt) while the check was in flight;
                    // touching controls from a stale render would act on the page that happens to be
                    // on screen now instead of the one that asked for the check.
                    if (!ctx.IsCurrent) return;

                    checkButton.IsEnabled = true;
                    status.Text = StatusText(r);
                    status.ToolTip = r?.Error;

                    if (ShowDownload(r))
                    {
                        downloadUrl = r.DownloadUrl;
                        downloadButton.Content = DownloadLabel(r.Latest);
                        AutomationProperties.SetName(downloadButton, "Download QuickPane " + UpdateService.Display(r.Latest));
                        downloadButton.ToolTip = r.DownloadUrl;
                        downloadButton.Visibility = Visibility.Visible;
                    }
                });
            };

            checkButton.Click += (s, e) => runCheck();
            runCheck();   // Runs once when the page opens, same as the old footer did on every open

            var border = ctx.Cards.CardShell(grid, hoverable: true);
            ctx.Cards.Register("updates", checkButton);
            return border;
        }

        /// <summary>The status line for a finished (or failed) check. A pure decision, unit-tested
        /// with no <c>UpdateService</c> call and no UI at all. Mirrors the old footer's own three
        /// result branches (SettingsPanel.xaml.cs's <c>BuildFooter</c>: failed, up to date, update
        /// available) exactly; <paramref name="r"/> is null only defensively, since
        /// <c>UpdateService.CheckAsync</c> never actually hands back one -- the old footer's
        /// `if (r == null) return;` left the stale "Checking for updates…" text on screen forever in
        /// that case, which is worse than reporting a failure, so this reports one instead.</summary>
        internal static string StatusText(UpdateCheckResult r)
        {
            if (r == null) return "Update check failed";
            if (!string.IsNullOrEmpty(r.Error)) return "Update check failed";
            if (!r.Available) return "Up to date";
            return "Version " + UpdateService.Display(r.Latest) + " is available";
        }

        /// <summary>Whether the Download button should be shown for this result.</summary>
        internal static bool ShowDownload(UpdateCheckResult r)
        {
            return r != null && string.IsNullOrEmpty(r.Error) && r.Available;
        }

        /// <summary>The Download button's label, e.g. "Download 3.11.0.1".</summary>
        internal static string DownloadLabel(Version latest)
        {
            return "Download " + UpdateService.Display(latest);
        }

        // ---- links card -----------------------------------------------------

        /// <summary>Source code, release notes, and report-an-issue, each opening the default
        /// browser (new, not carried over from the old footer). One bordered block with the three
        /// rows divided by a hairline, like a single merged card, rather than three separate cards
        /// with gaps between them.</summary>
        private static FrameworkElement BuildLinksCard(SettingsPageContext ctx)
        {
            var rows = new StackPanel();
            Button first = null;

            foreach (var link in Links)
            {
                if (rows.Children.Count > 0)
                {
                    var divider = new Border { Height = 1 };
                    divider.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
                    rows.Children.Add(divider);
                }

                var button = BuildLinkRow(link.Glyph, link.Title, link.Url, link.AccessibleName);
                rows.Children.Add(button);
                if (first == null) first = button;
            }

            var shell = ctx.Cards.CardShell(rows, hoverable: false);
            if (first != null) ctx.Cards.Register("links", first);
            return shell;
        }

        /// <summary>One row: an icon, a title, an "opens externally" glyph, and the whole row
        /// clickable (and Enter/Space-able, through <see cref="SettingsCards.Invokable"/>) to open
        /// <paramref name="url"/> in the default browser.</summary>
        private static Button BuildLinkRow(string glyph, string title, string url, string accessibleName)
        {
            var icon = GlyphText(glyph, 20);
            icon.Margin = new Thickness(0, 0, 20, 0);

            var text = new TextBlock { Text = title, FontSize = 14 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var external = GlyphText(GlyphExternal, 13);
            external.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            external.VerticalAlignment = VerticalAlignment.Center;

            // Vertical padding only: CardShell already adds 16 px on every side of the block that
            // holds all three rows, so a second 16 px here would push the text to 32 px from the
            // card's true edge and leave the hairline dividers with dead space above and below.
            var grid = new Grid { Margin = new Thickness(0, 12, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(external, 2);
            grid.Children.Add(icon);
            grid.Children.Add(text);
            grid.Children.Add(external);

            var border = new Border { Background = Brushes.Transparent, Child = grid };
            border.MouseEnter += (s, e) => border.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
            return SettingsCards.Invokable(border, accessibleName, () => OpenUrl(url));
        }

        internal struct LinkEntry
        {
            public string Glyph;
            public string Title;
            public string Url;
            public string AccessibleName;
        }

        /// <summary>Internal, not private: a unit test checks every URL is https and points at this
        /// fork's own repository, not upstream's.</summary>
        internal static readonly LinkEntry[] Links =
        {
            new LinkEntry { Glyph = GlyphLink, Title = "Source code on GitHub", Url = RepoUrl,
                AccessibleName = "Source code on GitHub, opens in your browser" },
            new LinkEntry { Glyph = GlyphDocument, Title = "Release notes", Url = ReleaseNotesUrl,
                AccessibleName = "Release notes, opens in your browser" },
            new LinkEntry { Glyph = GlyphInfo, Title = "Report an issue", Url = IssueTrackerUrl,
                AccessibleName = "Report an issue, opens in your browser" },
        };

        // ---- shared ----------------------------------------------------------

        private static TextBlock GlyphText(string glyph, double size)
        {
            return new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, Width = size + 4 };
        }

        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>Opens a URL in the user's default browser, exactly like the old footer's own
        /// <c>OpenUrl</c> (SettingsPanel.xaml.cs), with one deliberate tightening: <c>Process.Start</c>
        /// launches another process over COM/shell plumbing, so it goes through <c>WorkQueue</c>
        /// rather than running inline on the UI thread as the old handler did.</summary>
        private static void OpenUrl(string url)
        {
            WorkQueue.Post(() =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Error("open " + url, ex); }
            });
        }
    }
}
