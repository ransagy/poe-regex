namespace PoeRegex.Data.Build.Tests;

public class DataAuditTests
{
    [Fact]
    public void ParseIndex_NormalizesMainExportPrefixes()
    {
        const string html = """
            <a href=".data/mods.min.json">mods</a>
            <a href=".data/stat_translations/">translations</a>
            <a href=".data/stat_translations/stat_descriptions.min.json">stats</a>
            <a href=".data/Art/">art</a>
            <a href="https://github.com/example">external</a>
            """;

        var paths = DataAudit.ParseIndex(html, isMainExport: true);

        paths.Should().Contain(["mods.min.json", "stat_translations/", "stat_translations/stat_descriptions.min.json", "Art/"]);
        paths.Should().NotContain(p => p.StartsWith("http", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseIndex_NormalizesPobExportPrefixes()
    {
        const string html = """
            <a href="./ModRunes.min.json">runes</a>
            <a href="./Bases/">bases</a>
            <a href="../">parent</a>
            """;

        var paths = DataAudit.ParseIndex(html, isMainExport: false);

        paths.Should().Contain(["ModRunes.min.json", "Bases/"]);
        paths.Should().NotContain("../");
    }
}
