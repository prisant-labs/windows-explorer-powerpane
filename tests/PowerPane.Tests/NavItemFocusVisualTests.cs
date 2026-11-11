using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using NUnit.Framework;

namespace PowerPane.Tests
{
    /// <summary>#150: a keyboard-focused nav item showed thin lines above and below it, across
    /// the full width of the navigation column -- the Fluent theme's default focus ring, drawn
    /// around the whole (stretched) Button instead of around SettingsWindow.BuildNavItem's `bg`
    /// Border, which is inset by Margin(4,2,4,0) and CornerRadius(4). NavItemFocusVisualStyle
    /// (Themes/Fluent/Resources/DefaultFocusVisualStyle.xaml) gives the nav button its own ring
    /// matching that margin and radius. Loads the real ported XAML, like
    /// FluentThemeServiceTests, so a resource-key mistake in the port fails the build instead of
    /// only showing up later in the sandbox. Needs STA because ControlTemplate.LoadContent()
    /// realizes an actual Rectangle (see the remark in FluentThemeServiceTests).</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class NavItemFocusVisualTests
    {
        private static Rectangle LoadFocusRectangle(string styleKey)
        {
            var uri = new Uri("/PowerPane;component/Themes/Fluent/Resources/DefaultFocusVisualStyle.xaml", UriKind.Relative);
            var dict = (ResourceDictionary)Application.LoadComponent(uri);
            var style = (Style)dict[styleKey];
            var setter = (Setter)style.Setters[0];
            var template = (ControlTemplate)setter.Value;
            return (Rectangle)template.LoadContent();
        }

        [Test]
        public void NavItemFocusVisualStyle_MatchesTheNavPillsMarginAndRadius()
        {
            var rect = LoadFocusRectangle("NavItemFocusVisualStyle");

            // SettingsWindow.BuildNavItem's `bg` Border: Margin(4,2,4,0), CornerRadius(4).
            Assert.That(rect.Margin, Is.EqualTo(new Thickness(4, 2, 4, 0)));
            Assert.That(rect.RadiusX, Is.EqualTo(4.0));
            Assert.That(rect.RadiusY, Is.EqualTo(4.0));
            Assert.That(rect.StrokeThickness, Is.EqualTo(2.0));
        }

        [Test]
        public void DefaultControlFocusVisualStyle_StillUsesTheWholeControlsBounds()
        {
            // Unchanged by #150: every other Fluent control (buttons, toggles, the breadcrumb)
            // keeps the original -3 ring around its own bounds.
            var rect = LoadFocusRectangle("DefaultControlFocusVisualStyle");

            Assert.That(rect.Margin, Is.EqualTo(new Thickness(-3)));
            Assert.That(rect.RadiusX, Is.EqualTo(4.0));
            Assert.That(rect.RadiusY, Is.EqualTo(4.0));
        }
    }
}
