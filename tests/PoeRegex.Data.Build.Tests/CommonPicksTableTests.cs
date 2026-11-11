using PoeRegex.Web.Core;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// The contributor table in docs/COMMON-PICKS.md is generated from the data
/// file; CI runs <c>common-picks --check</c> so it cannot silently drift.
/// </summary>
public class CommonPicksTableTests
{
    [Fact]
    public void CommittedTable_MatchesGeneratedData()
    {
        var document = File.ReadAllText(
            Path.Combine(RepoRoot(), "docs", "COMMON-PICKS.md"));
        var generated = CommonPicksTable.Render(new EngineService());

        CommonPicksTable.ExtractBlock(document).Should().Be(generated,
            "run `common-picks --table` after editing data/common-picks.json");
    }

    [Fact]
    public void Render_ListsEveryEntryAndItsScope()
    {
        var table = CommonPicksTable.Render(new EngineService());

        foreach (var entry in CommonPicks.Entries)
            table.Should().Contain(entry.Label.Trim());
        table.Should().Contain("All gear");
        table.Should().Contain("Helmet");
        table.Should().Contain("Gloves");
        table.Should().Contain("`IncreasedLife`");
        table.Should().Contain("match: \"Adds to Physical Damage to Attacks\"");
    }

    [Fact]
    public void ReplaceBlock_OnlyTouchesTheMarkedSection()
    {
        const string document =
            "before\n<!-- common-picks:start -->\nold\n<!-- common-picks:end -->\nafter\n";

        var updated = CommonPicksTable.ReplaceBlock(
            document,
            "<!-- common-picks:start -->\nnew\n<!-- common-picks:end -->");

        updated.Should().Be(
            "before\n<!-- common-picks:start -->\nnew\n<!-- common-picks:end -->\nafter\n");
        CommonPicksTable.ExtractBlock(updated).Should().Be(
            "<!-- common-picks:start -->\nnew\n<!-- common-picks:end -->");
    }

    [Fact]
    public void ReplaceBlock_PreservesCrLfDocuments()
    {
        const string document =
            "before\r\n<!-- common-picks:start -->\r\nold\r\n<!-- common-picks:end -->\r\nafter\r\n";

        var updated = CommonPicksTable.ReplaceBlock(
            document,
            "<!-- common-picks:start -->\nnew\n<!-- common-picks:end -->");

        updated.Should().Be(
            "before\r\n<!-- common-picks:start -->\r\nnew\r\n<!-- common-picks:end -->\r\nafter\r\n");
        CommonPicksTable.ExtractBlock(updated).Should().Be(
            "<!-- common-picks:start -->\nnew\n<!-- common-picks:end -->");
    }

    [Fact]
    public void ExtractBlock_ReturnsNullWhenMarkersAreMissing()
    {
        CommonPicksTable.ExtractBlock("no generated table here").Should().BeNull();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "data", "catalogs", "poe2.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }
}
