using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class ItemMatcherShapeTests
{
    [Fact]
    public void QuotedBlockWithSpaces_IsOneLineLocalTerm()
    {
        ItemMatcher.Match("\"maximum Life\"", ["+10 to maximum Life"])
            .Match.Should().BeTrue();
        ItemMatcher.Match("\"maximum Life\"", ["10% maximum", "Life"])
            .Match.Should().BeFalse("both words must be on the same line inside one block");
    }

    [Fact]
    public void UnquotedSpaces_SplitIntoAndedTermsAcrossLines()
    {
        ItemMatcher.Match("maximum Life", ["10% maximum", "Life"])
            .Match.Should().BeTrue("unquoted terms may hit different lines");
    }
}
