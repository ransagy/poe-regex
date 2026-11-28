using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class QueryEquivalenceTests
{
    [Fact]
    public void SharedFactoredGroups_NormalizeToTheExpandedUnion()
    {
        QueryEquivalence.Equivalent(
                "\"Adds (A X|B X|C X)|tail\"",
                "\"Adds A X|Adds B X|Adds C X|tail\"",
                QuerySyntax.Poe2)
            .Should().BeTrue();
    }

    [Fact]
    public void NestedFactoredGroups_NormalizeToTheExpandedUnion()
    {
        QueryEquivalence.Equivalent(
                "\"Adds ((1|2).*Ph|(3|4).*ld)\"",
                "\"Adds 1.*Ph|Adds 2.*Ph|Adds 3.*ld|Adds 4.*ld\"",
                QuerySyntax.Poe2)
            .Should().BeTrue();
    }

    [Fact]
    public void QuantifiedGroup_IsNotExpanded()
    {
        // (A|B)+ is not A+|B+; the normalizer must leave the group alone.
        QueryEquivalence.Equivalent("\"(A|B)+\"", "\"A+|B+\"", QuerySyntax.Poe2)
            .Should().BeFalse();
    }

    [Fact]
    public void EscapedGroup_IsNotExpanded()
    {
        QueryEquivalence.Equivalent("\"\\(A|B\\)\"", "\"\\(A\\)|\\(B\\)\"", QuerySyntax.Poe2)
            .Should().BeFalse();
    }
}
