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

    [Fact]
    public void WriteReport_AcceptsBareFilenameAndNestedPath()
    {
        // The scheduled workflow passes --out data-audit.md, which has no
        // directory component; creating an empty directory used to throw.
        var bare = $"data-audit-{Guid.NewGuid():N}.md";
        try
        {
            DataAudit.WriteReport(bare, "bare");
            File.ReadAllText(bare).Should().Be("bare");
        }
        finally
        {
            if (File.Exists(bare)) File.Delete(bare);
        }

        var dir = Directory.CreateTempSubdirectory("poe-audit-");
        try
        {
            var nested = Path.Combine(dir.FullName, "nested", "report.md");
            DataAudit.WriteReport(nested, "nested");
            File.ReadAllText(nested).Should().Be("nested");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
