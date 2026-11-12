using System.IO;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers DriveService.QueryDetail, the quick pass's decision of whether to read IsReady, the
    /// volume label, and the size for a drive. A fixed drive used to get that detail even on the quick
    /// (synchronous) pass, on the assumption that a fixed drive always answers fast; it does not: custom
    /// icon handlers, cloud placeholders, junctions, and slow disks can all block it
    /// (prisant-labs/windows-explorer-powerpane#76).
    /// </summary>
    [TestFixture]
    public class DriveServiceTests
    {
        [Test]
        public void QueryDetail_QuickPass_NeverQueriesDetail_EvenForAFixedDrive()
        {
            Assert.That(DriveService.QueryDetail(false, DriveType.Fixed), Is.False);
        }

        [Test]
        public void QueryDetail_QuickPass_NeverQueriesDetail_ForANetworkDrive()
        {
            Assert.That(DriveService.QueryDetail(false, DriveType.Network), Is.False);
        }

        [Test]
        public void QueryDetail_FullPass_AlwaysQueriesDetail()
        {
            Assert.That(DriveService.QueryDetail(true, DriveType.Removable), Is.True);
        }
    }
}
