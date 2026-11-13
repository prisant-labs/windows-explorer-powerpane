using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>The checks the name prompt makes as a tab or group name is typed (#243).</summary>
    [TestFixture]
    public class TabNameRulesTests
    {
        private static readonly string[] Taken = { "Mira", "Orion", "Docs" };

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void BlankName_HasNoMessage(string input)
        {
            Assert.That(TabNameRules.Validate(input, Taken), Is.Null);
        }

        [TestCase("a\\b")]
        [TestCase("a/b")]
        [TestCase("a:b")]
        [TestCase("a*b")]
        [TestCase("a?b")]
        [TestCase("a\"b")]
        [TestCase("a<b")]
        [TestCase("a>b")]
        [TestCase("a|b")]
        public void InvalidCharacter_GivesTheCharactersMessage(string input)
        {
            Assert.That(TabNameRules.Validate(input, Taken), Is.EqualTo(TabNameRules.InvalidCharactersMessage));
            Assert.That(TabNameRules.Validate(input, null), Is.EqualTo(TabNameRules.InvalidCharactersMessage));
        }

        [TestCase("Docs.")]
        [TestCase("Docs ")]
        [TestCase("Vega...")]
        public void TrailingDotOrSpace_GivesTheTrailingMessage(string input)
        {
            Assert.That(TabNameRules.Validate(input, Taken), Is.EqualTo(TabNameRules.TrailingMessage));
        }

        [TestCase("Docs")]
        [TestCase("docs")]
        [TestCase("DOCS")]
        [TestCase("  Docs")]
        public void DuplicateIgnoringCase_NamesTheTab(string input)
        {
            Assert.That(TabNameRules.Validate(input, Taken), Is.EqualTo("This group already has a tab named \"Docs\"."));
        }

        [Test]
        public void ValidName_ReturnsNull()
        {
            Assert.That(TabNameRules.Validate("Vega", Taken), Is.Null);
            Assert.That(TabNameRules.Validate("Mira 2", Taken), Is.Null);
            Assert.That(TabNameRules.Validate(".hidden", Taken), Is.Null);
        }

        [Test]
        public void OwnNameOnRename_IsNotADuplicate()
        {
            // The caller leaves the tab being renamed out of the names already taken.
            var others = new[] { "Mira", "Vega" };

            Assert.That(TabNameRules.Validate("Orion", others), Is.Null);
            Assert.That(TabNameRules.Validate("orion", others), Is.Null);
            Assert.That(TabNameRules.Validate("Vega", others), Is.Not.Null);
        }

        [Test]
        public void GroupName_HasNoDuplicateRule()
        {
            Assert.That(TabNameRules.Validate("Docs", null), Is.Null);
            Assert.That(TabNameRules.Validate("Docs.", null), Is.EqualTo(TabNameRules.TrailingMessage));
        }

        [Test]
        public void NullEntriesInTheTakenList_AreSkipped()
        {
            Assert.That(TabNameRules.Validate("Vega", new string[] { null, "Mira" }), Is.Null);
        }
    }
}
