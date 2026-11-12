using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerPane.Explorer;
using PowerPane.Models;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Tests
{
    /// <summary>The desktop dock's Display picker (#146) stores a monitor by a stable id and must
    /// fall back to the primary display when that id is missing or no longer connected. These
    /// decisions -- <see cref="MonitorPicker.ResolveMonitor"/>, <see cref="MonitorPicker.BuildDisplayOptions"/>,
    /// <see cref="MonitorPicker.BuildLabel"/>, and <see cref="MonitorPicker.DisplayNumberFromDeviceName"/>
    /// -- are pure, so every test here uses an invented monitor list; only
    /// <see cref="MonitorPicker.DescribeMonitors"/> itself touches real monitors, and nothing here
    /// calls it.</summary>
    [TestFixture]
    public class MonitorPickerTests
    {
        private static NM.RECT Rect(int left, int top, int right, int bottom)
        {
            return new NM.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        private static MonitorDescriptor Monitor(string id, bool primary, NM.RECT rect, string label = null)
        {
            return new MonitorDescriptor { Id = id, Label = label ?? id, MonitorRect = rect, WorkRect = rect, IsPrimary = primary };
        }

        // ---- ResolveMonitor ---------------------------------------------------

        [Test]
        public void ResolveMonitor_NullStoredId_ReturnsThePrimary()
        {
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", false, Rect(1920, 0, 3840, 1080)),
                Monitor("B", true, Rect(0, 0, 1920, 1080)),
            };
            var resolved = MonitorPicker.ResolveMonitor(monitors, null);
            Assert.That(resolved.Id, Is.EqualTo("B"));
        }

        [Test]
        public void ResolveMonitor_EmptyStoredId_ReturnsThePrimary()
        {
            var monitors = new List<MonitorDescriptor> { Monitor("A", true, Rect(0, 0, 1920, 1080)) };
            var resolved = MonitorPicker.ResolveMonitor(monitors, "");
            Assert.That(resolved.Id, Is.EqualTo("A"));
        }

        [Test]
        public void ResolveMonitor_KnownId_ReturnsThatMonitor_EvenWhenNotPrimary()
        {
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", true, Rect(0, 0, 1920, 1080)),
                Monitor("B", false, Rect(1920, 0, 3840, 1080)),
            };
            var resolved = MonitorPicker.ResolveMonitor(monitors, "B");
            Assert.That(resolved.Id, Is.EqualTo("B"));
        }

        [Test]
        public void ResolveMonitor_UnknownId_FallsBackToThePrimary()
        {
            // The chosen monitor's own id is not in the connected list -- it was unplugged.
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", true, Rect(0, 0, 1920, 1080)),
                Monitor("B", false, Rect(1920, 0, 3840, 1080)),
            };
            var resolved = MonitorPicker.ResolveMonitor(monitors, "disconnected-monitor-id");
            Assert.That(resolved.Id, Is.EqualTo("A"));
        }

        [Test]
        public void ResolveMonitor_NoMonitorReportsPrimary_ReturnsTheFirstOne()
        {
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", false, Rect(0, 0, 1920, 1080)),
                Monitor("B", false, Rect(1920, 0, 3840, 1080)),
            };
            Assert.That(MonitorPicker.ResolveMonitor(monitors, null).Id, Is.EqualTo("A"));
        }

        [Test]
        public void ResolveMonitor_EmptyList_ReturnsAZeroedDescriptor()
        {
            var resolved = MonitorPicker.ResolveMonitor(new List<MonitorDescriptor>(), "A");
            Assert.That(resolved.Id, Is.Null);
            Assert.That(AppBarHost.SameRect(resolved.MonitorRect, Rect(0, 0, 0, 0)), Is.True);
        }

        [Test]
        public void ResolveMonitor_NullList_ReturnsAZeroedDescriptor()
        {
            Assert.DoesNotThrow(() => MonitorPicker.ResolveMonitor(null, "A"));
        }

        // ---- BuildDisplayOptions -----------------------------------------------

        [Test]
        public void BuildDisplayOptions_AlwaysLeadsWithPrimaryDisplay()
        {
            var options = MonitorPicker.BuildDisplayOptions(new List<MonitorDescriptor> { Monitor("A", true, Rect(0, 0, 1920, 1080)) }, null);
            Assert.That(options[0].Key, Is.EqualTo(MonitorPicker.PrimaryDisplayId));
            Assert.That(options[0].Value, Is.EqualTo("Primary display"));
        }

        [Test]
        public void BuildDisplayOptions_OneEntryPerMonitor_AfterPrimaryDisplay()
        {
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", true, Rect(0, 0, 1920, 1080), "Display 1 (primary) - 1920 x 1080"),
                Monitor("B", false, Rect(1920, 0, 3840, 1080), "Display 2 - 1920 x 1080"),
            };
            var options = MonitorPicker.BuildDisplayOptions(monitors, null);
            Assert.That(options.Select(o => o.Key), Is.EqualTo(new[] { MonitorPicker.PrimaryDisplayId, "A", "B" }));
            Assert.That(options[1].Value, Is.EqualTo("Display 1 (primary) - 1920 x 1080"));
            Assert.That(options[2].Value, Is.EqualTo("Display 2 - 1920 x 1080"));
        }

        [Test]
        public void BuildDisplayOptions_NoMonitors_ListsOnlyPrimaryDisplay()
        {
            var options = MonitorPicker.BuildDisplayOptions(new List<MonitorDescriptor>(), null);
            Assert.That(options, Has.Count.EqualTo(1));
        }

        [Test]
        public void BuildDisplayOptions_SkipsAMonitorWithNoId()
        {
            // DescribeMonitors never emits one of these (szDevice always falls back as the id), but
            // the builder should not offer an entry a settings save could never target back.
            var monitors = new List<MonitorDescriptor> { Monitor(null, true, Rect(0, 0, 1920, 1080)) };
            var options = MonitorPicker.BuildDisplayOptions(monitors, null);
            Assert.That(options, Has.Count.EqualTo(1));
        }

        // ---- BuildDisplayOptions: saved display not connected (#162) -----------

        [Test]
        public void BuildDisplayOptions_StoredIdConnected_NoExtraEntry()
        {
            // The saved monitor is still plugged in, so it is already one of the connected entries;
            // adding a second one for it would offer the same monitor twice.
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", true, Rect(0, 0, 1920, 1080)),
                Monitor("B", false, Rect(1920, 0, 3840, 1080)),
            };
            var options = MonitorPicker.BuildDisplayOptions(monitors, "B");
            Assert.That(options.Select(o => o.Key), Is.EqualTo(new[] { MonitorPicker.PrimaryDisplayId, "A", "B" }));
        }

        [Test]
        public void BuildDisplayOptions_StoredIdDisconnected_AddsASelectableTrailingEntry()
        {
            // The saved monitor is not among the connected ones (it was unplugged); without a
            // trailing entry for it, the combo would show "Primary display" already selected, so
            // picking "Primary display" again would fire no SelectionChanged and never save (#162).
            var monitors = new List<MonitorDescriptor>
            {
                Monitor("A", true, Rect(0, 0, 1920, 1080)),
                Monitor("B", false, Rect(1920, 0, 3840, 1080)),
            };
            var options = MonitorPicker.BuildDisplayOptions(monitors, "disconnected-monitor-id");
            Assert.That(options.Select(o => o.Key),
                Is.EqualTo(new[] { MonitorPicker.PrimaryDisplayId, "A", "B", "disconnected-monitor-id" }));
            // "Primary display" is still present and is a real, distinct choice from the
            // disconnected entry -- not pre-selected the way it was before #162.
            Assert.That(options[0].Key, Is.EqualTo(MonitorPicker.PrimaryDisplayId));
            Assert.That(options[0].Key, Is.Not.EqualTo(options[3].Key));
            Assert.That(options[3].Value, Does.Not.Contain("Contoso").And.Not.Contain("DISPLAY"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void BuildDisplayOptions_BlankStoredId_MeansPrimary_NoExtraEntry(string storedId)
        {
            var monitors = new List<MonitorDescriptor> { Monitor("A", true, Rect(0, 0, 1920, 1080)) };
            var options = MonitorPicker.BuildDisplayOptions(monitors, storedId);
            Assert.That(options, Has.Count.EqualTo(2));
            Assert.That(options.Select(o => o.Key), Is.EqualTo(new[] { MonitorPicker.PrimaryDisplayId, "A" }));
        }

        // ---- BuildLabel ---------------------------------------------------------

        [Test]
        public void BuildLabel_Primary_WithNoFriendlyName()
        {
            Assert.That(MonitorPicker.BuildLabel(1, true, 2560, 1440, null), Is.EqualTo("Display 1 (primary) - 2560 x 1440"));
        }

        [Test]
        public void BuildLabel_NotPrimary_WithFriendlyName()
        {
            Assert.That(MonitorPicker.BuildLabel(2, false, 1920, 1080, "Contoso Display 27"),
                Is.EqualTo("Display 2 - 1920 x 1080 - Contoso Display 27"));
        }

        [Test]
        public void BuildLabel_BlankFriendlyName_IsOmitted()
        {
            Assert.That(MonitorPicker.BuildLabel(1, true, 1920, 1080, "   "), Is.EqualTo("Display 1 (primary) - 1920 x 1080"));
        }

        // ---- DisplayNumberFromDeviceName ----------------------------------------

        [TestCase(@"\\.\DISPLAY1", 1)]
        [TestCase(@"\\.\DISPLAY2", 2)]
        [TestCase(@"\\.\DISPLAY10", 10)]
        [TestCase("", 0)]
        [TestCase((string)null, 0)]
        [TestCase("no-digits", 0)]
        public void DisplayNumberFromDeviceName_ReadsTheTrailingNumber(string deviceName, int expected)
        {
            Assert.That(MonitorPicker.DisplayNumberFromDeviceName(deviceName), Is.EqualTo(expected));
        }

        // ---- AppSettings.DesktopDockDisplay normalization (#146) ---------------

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Normalize_BlankDesktopDockDisplay_BecomesNull(string input)
        {
            var s = AppSettings.CreateDefault();
            s.DesktopDockDisplay = input;
            s.Normalize();
            Assert.That(s.DesktopDockDisplay, Is.Null);
        }

        [Test]
        public void Normalize_KeepsAChosenDisplayId()
        {
            // Normalize must not clear a stored choice just because that monitor is not plugged in
            // right now -- MonitorPicker.ResolveMonitor is where the fallback-to-primary happens, so
            // the id is still there (and takes effect again) the next time that monitor reconnects.
            var s = AppSettings.CreateDefault();
            s.DesktopDockDisplay = @"\\?\DISPLAY#Contoso Display 27#demo-id";
            s.Normalize();
            Assert.That(s.DesktopDockDisplay, Is.EqualTo(@"\\?\DISPLAY#Contoso Display 27#demo-id"));
        }

        [Test]
        public void CreateDefault_DesktopDockDisplayIsNull()
        {
            Assert.That(AppSettings.CreateDefault().DesktopDockDisplay, Is.Null);
        }

        // ---- AppSettings.DesktopDockEdge normalization (#177) ------------------

        [TestCase("right", "right")]
        [TestCase(" Right ", "right")]
        [TestCase("left", null)]
        [TestCase("", null)]
        [TestCase("top", null)]
        [TestCase(null, null)]
        public void Normalize_DesktopDockEdge_OnlyRightSurvives(string input, string expected)
        {
            var s = AppSettings.CreateDefault();
            s.DesktopDockEdge = input;
            s.Normalize();
            Assert.That(s.DesktopDockEdge, Is.EqualTo(expected));
        }

        [Test]
        public void CreateDefault_DesktopDockEdgeIsNull()
        {
            Assert.That(AppSettings.CreateDefault().DesktopDockEdge, Is.Null);
        }
    }
}
