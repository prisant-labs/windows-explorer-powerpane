using System;
using System.Windows;
using System.Windows.Controls;
using QuickPane.Services;

namespace QuickPane.UI
{
    /// <summary>
    /// Builds a row's context menu when it opens, not when the row is built. Every pin used to carry
    /// its full menu in every pane, so the cost multiplied by pins and by open windows, and every
    /// rebuild on the input-attached UI thread paid for menus nobody opened. With 508 pins and three
    /// windows, building on open took private memory from 410 MB to 118 MB.
    /// </summary>
    /// <remarks>
    /// WPF picks the element that will show a menu as ContextMenuOpening routes, and it only picks an
    /// element that already has one, so setting the property from the handler on a row with no menu
    /// is too late. Each row therefore holds one shared empty placeholder, and the handler swaps in
    /// the real menu before WPF reads the property. The real menu is dropped again when it closes.
    /// </remarks>
    internal static class LazyContextMenu
    {
        // Per thread, because a WPF object belongs to the thread that made it.
        [ThreadStatic] private static ContextMenu _placeholder;

        internal static ContextMenu Placeholder
        {
            get { return _placeholder ?? (_placeholder = new ContextMenu()); }
        }

        /// <summary>Give <paramref name="element"/> a menu that <paramref name="build"/> makes on each open.</summary>
        public static void Attach(FrameworkElement element, Func<ContextMenu> build)
        {
            element.ContextMenu = Placeholder;
            element.ContextMenuOpening += (s, e) => Swap(element, build, ex => Log.Error("build context menu", ex));
        }

        /// <summary>Put the real menu on the element, and arrange for it to go back to the placeholder
        /// on close. A failed build still shows a menu, because marking the event handled would skip
        /// the closing event that clears SidebarControl.SuppressAutoHide and leave the dock stuck open.</summary>
        internal static ContextMenu Swap(FrameworkElement element, Func<ContextMenu> build, Action<Exception> onError)
        {
            // A right-click inside the open menu routes here too, and must not replace it.
            var current = element.ContextMenu;
            if (current != null && !ReferenceEquals(current, Placeholder) && current.IsOpen) return current;

            ContextMenu menu = null;
            try { menu = build(); }
            catch (Exception ex) { onError(ex); }
            if (menu == null) menu = Unavailable();

            menu.Closed += (s, e) =>
            {
                // A second right-click on this row can install a newer menu before this one's Closed
                // arrives, and that newer menu must stay.
                if (ReferenceEquals(element.ContextMenu, menu)) element.ContextMenu = Placeholder;
            };
            element.ContextMenu = menu;
            return menu;
        }

        private static ContextMenu Unavailable()
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "This menu is not available", IsEnabled = false });
            return menu;
        }
    }
}
