using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PowerPane.Models;
using PowerPane.Services;
using WinForms = System.Windows.Forms;

namespace PowerPane.UI.Settings.Pages
{
    // Carries over every control from SettingsPanel.xaml.cs's "Connections" tab, at commit
    // 068301b. SshfsService.IsInstalled() was that page's one disk check on the UI thread
    // (SettingsPanel.xaml.cs line 671): StartInstallCheck below moves it onto WorkQueue and shows
    // the setup cards once it answers. Add connection's drive-letter probe also runs on WorkQueue,
    // since adding a connection is saving a setting. Everything else here --
    // SshfsService.Mount/Unmount, Process.Start for a download link, Clipboard.SetText for the
    // winget commands -- keeps the same direct call the old handler made; moving the rest as well
    // was judged out of scope for this page, and is tracked in #97 (whether Settings needs its own
    // UI thread).

    /// <summary>An info bar plus setup cards (WinFsp, SSHFS-Win, winget commands) when either
    /// component is missing, one expandable card per connection with every field from the
    /// inventory, and an "Add a connection" card.</summary>
    internal sealed class SshPage : SettingsPage
    {
        private const string PageGlyph = "\uE968";   // Globe
        private const string DeleteGlyph = "\uE74D"; // Delete (trash can)

        // Instance state survives across Build() calls because SettingsWindow keeps one SshPage
        // for the window's lifetime (see BuildPages()) and calls Build() again on every open,
        // navigation, and Rebuild(). _pendingRemove is keyed by SshProfile reference, which stays
        // stable across a Rebuild() because Rebuild() re-reads the same AppSettings.SshProfiles
        // list rather than replacing it; this is a judgment call (not in the page contract, which
        // says nothing about surviving confirm state across renders) but it is what lets a delete
        // confirmation row -- the same inline pattern every delete/remove in Settings uses --
        // survive the Rebuild() that
        // AuthMethod/Connect/Disconnect/Add/Remove themselves trigger, without inventing a
        // dialog. It is reset whenever the connection is actually removed or the user cancels.
        private readonly HashSet<SshProfile> _pendingRemove = new HashSet<SshProfile>();

        private bool _installCheckStarted;
        private bool _installChecked;
        private bool _installed;

        /// <summary>Records an install-check answer as if <see cref="StartInstallCheck"/> had
        /// returned it, so the next Build shows (or skips) the setup cards with no disk check.
        /// For tests: the real answer arrives through WorkQueue.PostUI, which a test host with no
        /// Application never runs (#120).</summary>
        internal void SetInstallState(bool installed)
        {
            _installCheckStarted = true;
            _installChecked = true;
            _installed = installed;
        }

        // The newest SettingsPageContext this page was built with. StartInstallCheck's worker can
        // answer after the user has navigated away and then *back* to this page, which gives a
        // fresh context whose IsCurrent is true even though the stale ctx captured when the check
        // was started would say false; checking the newest context instead of the one the check
        // closed over is what lets the setup cards still appear in that case.
        private SettingsPageContext _lastCtx;

        public override string Id => "ssh";
        public override string Title => "Remote drives";
        public override string Glyph => PageGlyph;

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get { yield return new SettingsSearchEntry(Id, null, Title, "Mount a remote folder over SSH as a drive letter.", "ssh", "sshfs", "winfsp", "mount", "connection"); }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            _lastCtx = ctx;
            var s = ctx.Settings.Current;
            if (s.SshProfiles == null) s.SshProfiles = new List<SshProfile>();

            var root = new StackPanel();

            if (_installChecked && !_installed)
            {
                root.Children.Add(ctx.Cards.InfoBar(
                    "Remote drives need two free components",
                    "Install WinFsp and SSHFS-Win, then reopen Settings.",
                    InfoBarSeverity.Warning));
                root.Children.Add(BuildComponentCard(ctx, "ssh-setup-winfsp", "WinFsp",
                    "The filesystem driver Windows needs to show a remote folder as a drive.",
                    "https://github.com/winfsp/winfsp/releases/latest"));
                root.Children.Add(BuildComponentCard(ctx, "ssh-setup-sshfswin", "SSHFS-Win",
                    "The SSH filesystem itself, which WinFsp mounts.",
                    "https://github.com/winfsp/sshfs-win/releases/latest"));
                root.Children.Add(BuildWingetCard(ctx));
            }
            else if (!_installChecked)
            {
                StartInstallCheck(ctx);
            }

            for (int i = 0; i < s.SshProfiles.Count; i++)
                root.Children.Add(BuildConnectionCard(ctx, s, s.SshProfiles[i], i));

            root.Children.Add(BuildAddConnectionCard(ctx, s));

            return root;
        }

        // ---- install check (off the UI thread) ------------------------------------------

        /// <summary>Runs <see cref="SshfsService.IsInstalled"/> (a disk check -- two
        /// <c>File.Exists</c> calls) off the UI thread, then hands the answer back through
        /// <see cref="WorkQueue.PostUI"/> and asks for a rebuild so the setup cards appear once the
        /// answer is known. Rebuilds through <see cref="_lastCtx"/> (the newest context this page
        /// was built with), not the <paramref name="ctx"/> the check started with: the user may
        /// have navigated away and back to this page while the check was running, which gives a
        /// new, current context even though the original one's <see cref="SettingsPageContext.IsCurrent"/>
        /// would now read false.</summary>
        private void StartInstallCheck(SettingsPageContext ctx)
        {
            if (_installCheckStarted) return;
            _installCheckStarted = true;

            WorkQueue.Post(() =>
            {
                bool installed;
                try { installed = SshfsService.IsInstalled(); }
                catch (Exception ex)
                {
                    // Fail open: a rare error probing two fixed paths should not wrongly tell the
                    // user to reinstall two working components.
                    Log.Error("SshPage: install check failed", ex);
                    installed = true;
                }

                WorkQueue.PostUI(() =>
                {
                    _installChecked = true;
                    _installed = installed;
                    if (_lastCtx != null && _lastCtx.IsCurrent) _lastCtx.Rebuild();
                });
            });
        }

        // ---- setup cards -----------------------------------------------------

        private Border BuildComponentCard(SettingsPageContext ctx, string cardId, string name, string what, string url)
        {
            var download = new Button { Content = "Download" };
            download.Click += (a, b) => OpenUrl(url);
            return ctx.Cards.Card(cardId, PageGlyph, name, what, download, "Download " + name);
        }

        private FrameworkElement BuildWingetCard(SettingsPageContext ctx)
        {
            const string commands = "winget install -e --id WinFsp.WinFsp\r\nwinget install -e --id SSHFS-Win.SSHFS-Win";

            var box = new TextBox
            {
                Text = commands,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            AutomationProperties.SetName(box, "Install commands");

            var copy = new Button { Content = "Copy", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(copy, "Copy install commands");
            copy.Click += (a, b) =>
            {
                try { System.Windows.Clipboard.SetText(commands); copy.Content = "Copied"; }
                catch (Exception ex) { Log.Error("SshPage: copy install commands", ex); }
            };

            var row = new DockPanel { LastChildFill = true, Tag = box }; // Card()'s RegisterAndName/search focus should land on the commands box, not this panel
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(copy);
            row.Children.Add(box);

            return ctx.Cards.Card("ssh-setup-winget", PageGlyph, "Install from a terminal",
                "Run each installer and accept the defaults, or install both from a terminal. WinFsp asks to restart Windows when it finishes.", row, "Install commands");
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("SshPage: open " + url, ex); }
        }

        // ---- connection cards -------------------------------------------------

        private UIElement BuildConnectionCard(SettingsPageContext ctx, AppSettings s, SshProfile p, int index)
        {
            string cardId = "ssh-conn-" + index;
            bool connected = SshfsService.IsMounted(p.Name);
            string status = connected ? "Connected" : "Not connected";

            var expander = ctx.Cards.Expander(cardId, PageGlyph, p.Name, status,
                headerControl: null, initiallyExpanded: true, accessibleName: p.Name + " connection");

            // Accessible names below follow one consistent pattern: most fields keep a plain name
            // ("Hostname", "Port"), and only Authentication, Reconnect, Connect/Disconnect, and
            // Remove connection fold the connection's name in.
            expander.AddChild(ctx.Cards.ChildRow("Name",
                CommitTextBox(p.Name, v =>
                {
                    if (string.Equals(v, p.Name, StringComparison.Ordinal)) return;
                    p.Name = v;
                    ctx.Settings.Save();
                    // Renames the header in place rather than rebuilding: this runs from the box's
                    // LostKeyboardFocus, and a rebuild there would tear the tree down while focus
                    // moves to the next control, dropping keyboard focus or swallowing the click that
                    // moved it. The old window only saved here (068301b line 711); names that fold
                    // the connection's name in catch up on the next rebuild, as they did then.
                    expander.Title = v;
                }),
                "Connection name"));

            expander.AddChild(ctx.Cards.ChildRow("Hostname",
                CommitTextBox(p.Hostname, v => { p.Hostname = v; ctx.Settings.Save(); }),
                "Hostname"));

            expander.AddChild(ctx.Cards.ChildRow("Port",
                ctx.Cards.NumberBox(p.Port, 1, 65535, 1, v => { p.Port = (int)v; ctx.Settings.Save(); },
                    "Port", "Increase port", "Decrease port"),
                "Port"));

            expander.AddChild(ctx.Cards.ChildRow("Auto-login username",
                CommitTextBox(p.Username, v => { p.Username = v; ctx.Settings.Save(); }),
                "Auto-login username"));

            // Rebuild() swaps the private-key or password row below in, exactly like the old radio
            // group's Checked handler (SettingsPanel.xaml.cs lines 735-738).
            expander.AddChild(ctx.Cards.ChildRow("Authentication",
                ctx.Cards.Combo(AuthenticationOptions, p.AuthMethod,
                    v => { p.AuthMethod = v; ctx.Settings.Save(); ctx.Rebuild(); }, minWidth: 160),
                "Authentication for " + p.Name));

            if (string.Equals(p.AuthMethod, "password", StringComparison.OrdinalIgnoreCase))
                expander.AddChild(ctx.Cards.ChildRow("Auto-login password", PasswordRow(ctx, p), "Auto-login password"));
            else
                expander.AddChild(ctx.Cards.ChildRow("Private key file", KeyFileRow(ctx, p), "Private key file for authentication"));

            expander.AddChild(ctx.Cards.ChildRow("Remote path",
                CommitTextBox(p.RemotePath, v => { p.RemotePath = v; ctx.Settings.Save(); }),
                "Remote path"));

            expander.AddChild(ctx.Cards.ChildRow("Drive letter",
                CommitTextBox(p.DriveLetter, v => { p.DriveLetter = v; ctx.Settings.Save(); }),
                "Drive letter"));

            expander.AddChild(ctx.Cards.ChildRow("Keepalive: seconds between packets",
                ctx.Cards.NumberBox(p.KeepAliveInterval, 0, 300, 5, v => { p.KeepAliveInterval = (int)v; ctx.Settings.Save(); },
                    "Keepalive seconds", "Increase keepalive seconds", "Decrease keepalive seconds"),
                "Keepalive seconds"));

            expander.AddChild(ctx.Cards.ChildRow("Reconnect automatically",
                ctx.Cards.Toggle(p.Reconnect, on => { p.Reconnect = on; ctx.Settings.Save(); }, "Reconnect " + p.Name + " automatically"),
                "Reconnect " + p.Name + " automatically"));

            if (_pendingRemove.Contains(p))
            {
                expander.AddChild(ctx.Cards.ConfirmRow("Remove " + p.Name + "?", "Remove",
                    onConfirm: () => RemoveConnection(ctx, s, p, connected),
                    onCancel: () => { _pendingRemove.Remove(p); ctx.Rebuild(); }));
            }
            else
            {
                expander.AddChild(BuildActionsRow(ctx, p, connected));
            }

            return expander.Root;
        }

        private FrameworkElement BuildActionsRow(SettingsPageContext ctx, SshProfile p, bool connected)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var connectBtn = new Button { Content = connected ? "Disconnect" : "Connect", Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(connectBtn, (connected ? "Disconnect " : "Connect ") + p.Name);
            connectBtn.Click += (a, b) => ConnectOrDisconnect(ctx, p, connected);
            panel.Children.Add(connectBtn);

            var removeBtn = ctx.Cards.IconButton(DeleteGlyph, "Remove connection " + p.Name, () =>
            {
                _pendingRemove.Add(p);
                ctx.Rebuild();
            });
            panel.Children.Add(removeBtn);

            return ctx.Cards.TrailingChildRow(panel);
        }

        private void ConnectOrDisconnect(SettingsPageContext ctx, SshProfile p, bool connected)
        {
            if (connected)
            {
                SshfsService.Unmount(p);
            }
            else
            {
                var err = SshfsService.Mount(p);
                if (err != null)
                {
                    WinForms.MessageBox.Show(err, "PowerPane");
                    return;
                }
            }
            ctx.Rebuild();
        }

        private void RemoveConnection(SettingsPageContext ctx, AppSettings s, SshProfile p, bool connected)
        {
            if (connected) SshfsService.Unmount(p);
            s.SshProfiles.Remove(p);
            _pendingRemove.Remove(p);
            ctx.Settings.Save();
            ctx.Rebuild();
        }

        private UIElement BuildAddConnectionCard(SettingsPageContext ctx, AppSettings s)
        {
            var add = new Button { Content = "Add connection" };
            AutomationProperties.SetName(add, "Add connection");
            add.Click += (a, b) =>
            {
                // Probing S: to Z: is up to eight Directory.Exists calls, and one on a disconnected
                // mapped drive can block for seconds, so it runs on WorkQueue; the add itself
                // stays on the UI thread, which owns SettingsStore. The connection is added even if
                // the user has left the page meanwhile, since they asked for it; only the redraw
                // waits for this render to still be on screen.
                add.IsEnabled = false;
                WorkQueue.Post(() =>
                {
                    string letter = NextFreeDriveLetter(root => System.IO.Directory.Exists(root));
                    WorkQueue.PostUI(() =>
                    {
                        // Read Current again, as the old handler did at click time: a Restore while
                        // the probe ran may have replaced the settings object this page was built on.
                        var cur = ctx.Settings.Current;
                        if (cur.SshProfiles == null) cur.SshProfiles = new List<SshProfile>();
                        int next = cur.SshProfiles.Count + 1;
                        cur.SshProfiles.Add(new SshProfile { Name = "Connection " + next, DriveLetter = letter });
                        ctx.Settings.Save();
                        if (ctx.IsCurrent) ctx.Rebuild();
                    });
                });
            };
            return ctx.Cards.Card("ssh-add", PageGlyph, "Add a connection",
                "Mount another folder over SSH as a drive letter.", add, "Add connection");
        }

        // ---- field builders ---------------------------------------------------

        /// <summary>The two-option (value, label) pairs for <see cref="SshPage"/>'s Authentication
        /// combo box: the value is <see cref="SshProfile.AuthMethod"/>'s own stored value.</summary>
        private static readonly KeyValuePair<string, string>[] AuthenticationOptions =
        {
            new KeyValuePair<string, string>("key", "Private key"),
            new KeyValuePair<string, string>("password", "Password"),
        };

        /// <summary>Editable text box + Browse button, matching the old key-file row
        /// (SettingsPanel.xaml.cs lines 793-812) exactly: typing a path directly still works, which
        /// the shared <c>SettingsCards.PathField</c> cannot do once given a Browse callback (it
        /// forces the box read-only). Built here instead of changing that shared helper, which
        /// every other path field in Settings still relies on being read-only with Browse.</summary>
        private static FrameworkElement KeyFileRow(SettingsPageContext ctx, SshProfile p)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new TextBox { Text = p.PrivateKeyPath ?? string.Empty, MinWidth = 200 };
            Action<string> commit = v => { p.PrivateKeyPath = v; ctx.Settings.Save(); };
            box.LostKeyboardFocus += (a, b) => commit(box.Text);
            box.KeyDown += (a, b) => { if (b.Key == Key.Enter) commit(box.Text); };
            Grid.SetColumn(box, 0);
            grid.Children.Add(box);

            var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
            browse.ToolTip = "PuTTY keys must be converted first: PuTTYgen -> Conversions -> Export OpenSSH key";
            AutomationProperties.SetName(browse, "Browse for private key file");
            browse.Click += (a, b) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Private key (*.*)|*.*" };
                PickerPlaces.AddTo(dlg);
                if (dlg.ShowDialog() == true)
                {
                    box.Text = dlg.FileName;
                    commit(dlg.FileName);
                }
            };
            Grid.SetColumn(browse, 1);
            grid.Children.Add(browse);

            grid.Tag = box; // what ChildRow's accessible name and search focus should land on
            return grid;
        }

        private static FrameworkElement PasswordRow(SettingsPageContext ctx, SshProfile p)
        {
            var box = new PasswordBox { Password = p.Password ?? string.Empty, MinWidth = 200 };
            box.LostKeyboardFocus += (a, b) => { p.Password = box.Password; ctx.Settings.Save(); };
            return box;
        }

        /// <summary>LostKeyboardFocus, not the logical LostFocus, matches the old handlers exactly
        /// (SettingsPanel.xaml.cs lines 711, 728, 817 and others): it also fires when the window
        /// itself is deactivated (e.g. the user clicks the close button without tabbing out first),
        /// where LostFocus would not, so an edit in progress is still saved.</summary>
        private static TextBox CommitTextBox(string text, Action<string> onCommit)
        {
            var box = new TextBox { Text = text ?? string.Empty, MinWidth = 200 };
            Action commit = () => onCommit(box.Text);
            box.LostKeyboardFocus += (a, b) => commit();
            box.KeyDown += (a, b) => { if (b.Key == Key.Enter) commit(); };
            return box;
        }

        // ---- testable decisions ---------------------------------------

        /// <summary>Picks the first unused drive letter S:-Z:, matching the old
        /// <c>NextFreeDriveLetter</c> (SettingsPanel.xaml.cs lines 864-872) exactly, but with the
        /// existence check injected so the decision is testable with no real disk.</summary>
        internal static string NextFreeDriveLetter(Func<string, bool> driveRootExists)
        {
            for (char c = 'S'; c <= 'Z'; c++)
            {
                string letter = c + ":";
                if (!driveRootExists(letter + @"\")) return letter;
            }
            return "S:";
        }
    }
}
