using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerPane.UI.Settings
{
    /// <summary>Drag-to-reorder for the Pane sections and Quick actions rows (#178). The Move up / Move
    /// down buttons stay beside it as the keyboard and screen-reader path.
    ///
    /// This is a mouse-capture drag (MouseDown on the grip, CaptureMouse, MouseMove, MouseUp), not OLE
    /// <c>DragDrop.DoDragDrop</c>: DoDragDrop runs a nested message loop, and in inside mode the UI
    /// thread shares Explorer's input queue, so the loop would sit on Explorer's input too. The drop is
    /// reported through <c>onDrop(from, to)</c>, which both pages route into their shared
    /// "move item from index i to index j" static, so the order logic stays unit-tested even though a
    /// drag cannot be (tests never show a window).</summary>
    internal static class ReorderDrag
    {
        /// <summary>Pixels the mouse must travel from the press before it counts as a drag, so a plain
        /// click on the grip does not rebuild the page.</summary>
        private const double DragThreshold = 4;

        /// <summary>The insertion slot (0..n) for a pointer at <paramref name="y"/>: the slot before the
        /// first row whose vertical midpoint is below the pointer, or n (after the last row). Pure, so
        /// it is unit-tested.</summary>
        internal static int SlotFor(IList<double> rowMidpoints, double y)
        {
            if (rowMidpoints == null) return 0;
            for (int i = 0; i < rowMidpoints.Count; i++)
                if (y < rowMidpoints[i]) return i;
            return rowMidpoints.Count;
        }

        /// <summary>The index the dragged row ends at when dropped into insertion slot
        /// <paramref name="slot"/>: removing the row first shifts every later slot up by one.</summary>
        internal static int TargetIndex(int slot, int from)
        {
            return slot > from ? slot - 1 : slot;
        }

        /// <summary>A grip (a 2x3 block of dots, drawn rather than a font glyph so it needs no icon
        /// font) that drags the row at <paramref name="index"/> within <paramref name="rows"/>.
        /// <paramref name="rows"/> is read lazily on each move, so the caller may fill it after calling
        /// this. <paramref name="host"/> is the panel that contains the rows; the insertion line is
        /// drawn over it.</summary>
        internal static FrameworkElement CreateGrip(string accessibleName, IList<FrameworkElement> rows,
            FrameworkElement host, int index, Action<int, int> onDrop)
        {
            var dots = new Grid { Width = 10, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            for (int c = 0; c < 2; c++) dots.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 3; r++) dots.RowDefinitions.Add(new RowDefinition());
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 2; c++)
                {
                    var dot = new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextFillColorSecondaryBrush");
                    Grid.SetRow(dot, r);
                    Grid.SetColumn(dot, c);
                    dots.Children.Add(dot);
                }
            }

            var grip = new Border
            {
                Width = 24,
                MinHeight = 32,
                Background = Brushes.Transparent,   // transparent, not null: a null background is not hit-testable
                Cursor = Cursors.SizeAll,
                ToolTip = "Drag to reorder",
                Child = dots,
                // Focusable only so Escape reaches it during a drag; the Move buttons are the tab stops.
                Focusable = true,
                FocusVisualStyle = null,
            };
            KeyboardNavigation.SetIsTabStop(grip, false);
            SettingsCards.SetAccessibleName(grip, accessibleName);

            var drag = new DragState();

            grip.MouseLeftButtonDown += (s, e) =>
            {
                drag.Start = e.GetPosition(host);
                drag.Pressed = true;
                drag.Active = false;
                grip.Focus();
                grip.CaptureMouse();
                e.Handled = true;
            };

            grip.MouseMove += (s, e) =>
            {
                if (!drag.Pressed || !grip.IsMouseCaptured) return;
                var pos = e.GetPosition(host);
                if (!drag.Active)
                {
                    if (Math.Abs(pos.Y - drag.Start.Y) < DragThreshold) return;
                    drag.Active = true;
                    if (index >= 0 && index < rows.Count) rows[index].Opacity = 0.5;
                }
                drag.Slot = SlotAt(rows, host, pos.Y);
                int target = TargetIndex(drag.Slot, index);
                if (target == index) HideLine(drag, host);
                else ShowLine(drag, host, LineY(rows, host, drag.Slot));
            };

            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!drag.Pressed) return;
                bool commit = drag.Active;
                int target = TargetIndex(drag.Slot, index);
                // Reset first: the drop rebuilds the page, which discards this grip along with its state.
                Finish(drag, rows, host, index);
                grip.ReleaseMouseCapture();
                e.Handled = true;
                if (commit && target != index) onDrop?.Invoke(index, target);
            };

            // Escape cancels the drag in flight; losing capture (alt-tab, the window closing, a menu
            // opening) cancels it the same way, so a half-finished drag never commits.
            grip.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && drag.Pressed)
                {
                    Finish(drag, rows, host, index);
                    grip.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };
            grip.LostMouseCapture += (s, e) => Finish(drag, rows, host, index);

            return grip;
        }

        private sealed class DragState
        {
            public Point Start;
            public bool Pressed;
            public bool Active;
            public int Slot;
            public DropLineAdorner Line;
            public AdornerLayer Layer;
        }

        private static void Finish(DragState drag, IList<FrameworkElement> rows, FrameworkElement host, int index)
        {
            drag.Pressed = false;
            drag.Active = false;
            HideLine(drag, host);
            if (drag.Layer != null && drag.Line != null) drag.Layer.Remove(drag.Line);
            drag.Line = null;
            drag.Layer = null;
            if (index >= 0 && index < rows.Count) rows[index].Opacity = 1;
        }

        private static int SlotAt(IList<FrameworkElement> rows, FrameworkElement host, double y)
        {
            var mids = new List<double>(rows.Count);
            foreach (var row in rows)
                mids.Add(row.TranslatePoint(new Point(0, 0), host).Y + row.ActualHeight / 2);
            return SlotFor(mids, y);
        }

        private static double LineY(IList<FrameworkElement> rows, FrameworkElement host, int slot)
        {
            if (rows.Count == 0) return 0;
            if (slot < rows.Count) return rows[slot].TranslatePoint(new Point(0, 0), host).Y - 2;
            var last = rows[rows.Count - 1];
            return last.TranslatePoint(new Point(0, 0), host).Y + last.ActualHeight;
        }

        private static void ShowLine(DragState drag, FrameworkElement host, double y)
        {
            if (drag.Line == null)
            {
                // No adorner layer means no indicator, but the drag still works.
                drag.Layer = AdornerLayer.GetAdornerLayer(host);
                if (drag.Layer == null) return;
                drag.Line = new DropLineAdorner(host);
                drag.Layer.Add(drag.Line);
            }
            drag.Line.Update(y);
        }

        private static void HideLine(DragState drag, FrameworkElement host)
        {
            if (drag.Line != null) drag.Line.Hide();
        }
    }
}
