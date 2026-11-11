using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using PowerPane.Interop;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers ClassOf, which now reuses one buffer per thread instead of allocating one per call (#44).
    /// It reads only the desktop window, which always exists; no window is created or shown.
    /// </summary>
    [TestFixture]
    public class NativeMethodsTests
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [Test]
        public void ClassOf_RepeatedCalls_DoNotCarryTextOver()
        {
            // The desktop window's class is "#32769". A reused buffer must be emptied between calls, so a
            // failed read after a successful one returns "" rather than the previous class.
            Assert.That(NativeMethods.ClassOf(GetDesktopWindow()), Is.EqualTo("#32769"));
            Assert.That(NativeMethods.ClassOf(GetDesktopWindow()), Is.EqualTo("#32769"));
            Assert.That(NativeMethods.ClassOf(IntPtr.Zero), Is.EqualTo(""));
        }
    }
}
