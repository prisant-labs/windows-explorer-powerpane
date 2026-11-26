using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>The pane is the root of an HwndSource, not a Window, so it never hears that Application
    /// resources changed. On a live light/dark switch it copies the new theme into its own Resources with
    /// ThemeService.SwapInto. These tests build elements only, no window, and check that the copy replaces
    /// the old one and that swapping an element's own Resources re-resolves its DynamicResource values.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class HostedThemeRefreshTests
    {
        private static ResourceDictionary Theme(Color background, Color text)
        {
            var d = new ResourceDictionary();
            d["SidebarBackground"] = new SolidColorBrush(background);
            d["TextPrimary"] = new SolidColorBrush(text);
            return d;
        }

        private static readonly Color LightBack = Color.FromRgb(0xF3, 0xF3, 0xF3);
        private static readonly Color LightText = Color.FromRgb(0x1A, 0x1A, 0x1A);
        private static readonly Color DarkBack = Color.FromRgb(0x1E, 0x1E, 0x1E);
        private static readonly Color DarkText = Color.FromRgb(0xFF, 0xFF, 0xFF);

        private static Color ColorOf(Brush b) { return ((SolidColorBrush)b).Color; }

        [Test]
        public void SwapInto_ReplacesThePreviousThemeAndKeepsOtherDictionaries()
        {
            var target = new ResourceDictionary();
            var own = new ResourceDictionary();
            target.MergedDictionaries.Add(own);
            var light = Theme(LightBack, LightText);
            var dark = Theme(DarkBack, DarkText);

            var held = ThemeService.SwapInto(target, null, light);
            held = ThemeService.SwapInto(target, held, dark);

            Assert.That(held, Is.SameAs(dark));
            Assert.That(target.MergedDictionaries, Is.EqualTo(new[] { own, dark }));
        }

        [Test]
        public void SwapInto_SameDictionaryOrNoTarget_ChangesNothing()
        {
            var target = new ResourceDictionary();
            var light = Theme(LightBack, LightText);
            var held = ThemeService.SwapInto(target, null, light);

            Assert.That(ThemeService.SwapInto(target, held, light), Is.SameAs(light));
            Assert.That(target.MergedDictionaries.Count, Is.EqualTo(1));
            Assert.That(ThemeService.SwapInto(null, held, Theme(DarkBack, DarkText)), Is.SameAs(light));
        }

        /// <summary>The live dark switch that left the pane unreadable: the pane's background and its
        /// title text are DynamicResource references set once, in XAML. Swapping the theme in the
        /// element's own Resources must move both to the dark values together.</summary>
        [Test]
        public void SwappingOwnResources_ReResolvesEveryDynamicResource()
        {
            var pane = new Border();
            var title = new TextBlock();
            pane.Child = title;
            pane.SetResourceReference(Border.BackgroundProperty, "SidebarBackground");
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");

            var held = ThemeService.SwapInto(pane.Resources, null, Theme(LightBack, LightText));
            Assert.That(ColorOf(pane.Background), Is.EqualTo(LightBack));
            Assert.That(ColorOf(title.Foreground), Is.EqualTo(LightText));

            held = ThemeService.SwapInto(pane.Resources, held, Theme(DarkBack, DarkText));
            Assert.That(ColorOf(pane.Background), Is.EqualTo(DarkBack));
            Assert.That(ColorOf(title.Foreground), Is.EqualTo(DarkText));

            ThemeService.SwapInto(pane.Resources, held, Theme(LightBack, LightText));
            Assert.That(ColorOf(pane.Background), Is.EqualTo(LightBack));
            Assert.That(ColorOf(title.Foreground), Is.EqualTo(LightText));
        }

        /// <summary>A pane color change swaps the theme the same way. The pane's own copy is looked up
        /// before the Application's, so without the swap the pane would keep the old color.</summary>
        [Test]
        public void PaneColorChange_ReachesThePaneThroughItsOwnCopy()
        {
            var pane = new Border();
            pane.SetResourceReference(Border.BackgroundProperty, "SidebarBackground");
            var held = ThemeService.SwapInto(pane.Resources, null, Theme(LightBack, LightText));

            var sage = Color.FromArgb(0xE6, 0xE6, 0xEF, 0xE3);
            ThemeService.SwapInto(pane.Resources, held, Theme(sage, LightText));

            Assert.That(ColorOf(pane.Background), Is.EqualTo(sage));
        }
    }
}
