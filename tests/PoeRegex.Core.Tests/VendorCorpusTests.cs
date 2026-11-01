using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

public class VendorCorpusTests
{
    [Fact]
    public async Task AsyncLoad_MatchesTheSynchronousCorpus()
    {
        var asyncCorpus = await VendorCorpus.ForCurrentGameAsync();
        asyncCorpus.Should().NotBeNull();
        asyncCorpus!.Lines.Should().NotBeEmpty();

        // Once the async load completes, the synchronous entry point must
        // reuse the same instance instead of parsing the 14 MB resource again.
        VendorCorpus.ForCurrentGame.Should().BeSameAs(asyncCorpus);
    }

    [Fact]
    public async Task PrewarmGlobalLines_MatchesTheSynchronousBuild()
    {
        var syncCorpus = new EngineCorpus(new EmbeddedCatalogSource().Load());
        var expected = syncCorpus.RealLinesGlobal();

        var warmCorpus = new EngineCorpus(new EmbeddedCatalogSource().Load());
        await warmCorpus.PrewarmGlobalLinesAsync();

        warmCorpus.RealLinesGlobal().Should().Equal(expected);
    }

    [Fact]
    public async Task PrewarmGlobalLines_IsIdempotent()
    {
        var corpus = new EngineCorpus(new EmbeddedCatalogSource().Load());
        await corpus.PrewarmGlobalLinesAsync();
        var first = corpus.RealLinesGlobal();
        await corpus.PrewarmGlobalLinesAsync();
        corpus.RealLinesGlobal().Should().BeSameAs(first);
    }
}
