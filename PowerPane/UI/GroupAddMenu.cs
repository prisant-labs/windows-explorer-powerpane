using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>
    /// The "Add" entries that the "+" button, the group's right-click menu, and a tab's right-click menu
    /// share (#243), so the three can no longer drift apart. Each entry names what it adds to, through
    /// <see cref="AddMenuLabels"/>.
    /// </summary>
    internal static class GroupAddMenu
    {
        /// <summary>Add the requested entries to <paramref name="menu"/>, with a separator between the
        /// tab and group entries and the folder and file entries when a menu has both kinds.
        /// <paramref name="target"/> is the tab a folder or file goes into. <paramref name="anchor"/>
        /// is read when an entry opens a prompt, so the prompt lands next to the control.</summary>
        internal static void AddEntries(ItemsControl menu, PinnedGroup group, PinnedTab target, AddKinds kinds,
            Func<PromptAnchor?> anchor)
        {
            var headers = AddMenuLabels.Headers(group, target, kinds);
            bool addedTabOrGroup = false, separated = false;
            foreach (var h in headers)
            {
                bool isPinKind = h.Kind == AddKind.Folder || h.Kind == AddKind.File;
                if (isPinKind && addedTabOrGroup && !separated)
                {
                    menu.Items.Add(new Separator());
                    separated = true;
                }
                if (!isPinKind) addedTabOrGroup = true;
                menu.Items.Add(Entry(group, target, h, anchor));
            }
        }

        /// <summary>The "+" button's menu, for the tab that is selected now.</summary>
        internal static ContextMenu BuildMenu(PinnedGroup group, PinnedTab target, Func<PromptAnchor?> anchor)
        {
            var menu = new ContextMenu();
            AddEntries(menu, group, target, AddKinds.All, anchor);
            return menu;
        }

        /// <summary>Opens the menu below a "+" button. The group's active tab is read here, so a pin goes
        /// into the tab that is selected when the button is clicked, and the button's place on screen is
        /// read here too, before the pane can rebuild and detach it. <paramref name="onOpenChanged"/>
        /// is told when the menu opens and closes.</summary>
        internal static void Open(FrameworkElement plus, PinnedGroup group, Action<bool> onOpenChanged)
        {
            var target = group.Active;
            var anchor = PromptAnchor.Capture(plus);
            var menu = BuildMenu(group, target, () => anchor);

            // A menu opened in code raises no ContextMenuOpening, so the pane's own guard never hears
            // about it. Hold the dock open here and let go when the menu closes.
            SidebarControl.SuppressAutoHide = true;
            menu.Closed += (s, e) =>
            {
                SidebarControl.SuppressAutoHide = false;
                if (onOpenChanged != null) onOpenChanged(false);
            };
            menu.PlacementTarget = plus;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            if (onOpenChanged != null) onOpenChanged(true);
        }

        /// <summary>How visible a single-tab header's "+" is. It is hidden until the pointer is over the
        /// header, and stays visible while it has keyboard focus or its menu is open, so a keyboard user
        /// never lands on an invisible control and the button does not vanish under its own menu.</summary>
        internal static double PlusOpacity(bool headerHovered, bool plusFocused, bool menuOpen)
        {
            return headerHovered || plusFocused || menuOpen ? 1.0 : 0.0;
        }

        private static MenuItem Entry(PinnedGroup group, PinnedTab target, AddHeader h, Func<PromptAnchor?> anchor)
        {
            Action action;
            switch (h.Kind)
            {
                case AddKind.Tab:
                    action = () => AddTab(group, anchor());
                    break;
                case AddKind.GroupBelow:
                    action = () => AddGroupBelow(group, anchor());
                    break;
                case AddKind.Folder:
                    action = () => AddPin(group, target, GroupSection.PickFolder());
                    break;
                default:
                    action = () => AddPin(group, target, GroupSection.PickFile());
                    break;
            }
            var mi = GroupSection.Item(h.Header, action);
            AutomationProperties.SetName(mi, h.Text);
            return mi;
        }

        private static void AddTab(PinnedGroup group, PromptAnchor? anchor)
        {
            var v = AnchoredPrompt.Ask(anchor, new PromptOptions
            {
                Title = AddMenuLabels.NewTabTitle(group),
                InputName = "New tab name",
                Validate = text => TabNameRules.Validate(text, group.Tabs.Select(t => t.Name))
            });
            if (!string.IsNullOrWhiteSpace(v)) App.Groups.PostEdit(store => store.AddTab(group, v));
        }

        private static void AddGroupBelow(PinnedGroup group, PromptAnchor? anchor)
        {
            var v = AnchoredPrompt.Ask(anchor, new PromptOptions
            {
                Title = AddMenuLabels.NewGroupBelowTitle(group),
                InputName = "New group name",
                Validate = text => TabNameRules.Validate(text, null)
            });
            if (!string.IsNullOrWhiteSpace(v)) App.Groups.PostEdit(store => store.CreateGroupAfter(group, v));
        }

        private static void AddPin(PinnedGroup group, PinnedTab target, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (target != null) App.Groups.PostEdit(store => store.AddPin(target, path));
            else App.Groups.PostEdit(store => store.AddPin(group, path));
        }
    }
}
