using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds the shipped min-capability table. The probe is the same
/// <see cref="MinCapability.Probe"/> the browser falls back to, so a table hit
/// and a live probe agree by construction.
/// </summary>
public static class MinCapabilityBuilder
{
    public static MinCapableTable Build(
        Catalog catalog, EngineCorpus corpus, PoeRegexCompiler compiler)
    {
        var global = new HashSet<string>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(HashSet<string> set, WantedMod pick, string? itemClass)
        {
            var key = FragmentTable.MinCapableKey(pick.ModId, pick.VariantModIds);
            if (set.Contains(key)) return;
            if (MinCapability.Probe(compiler, corpus.FindMod, pick, itemClass))
                set.Add(key);
        }

        var playable = ModSearch.MergeMods(
            corpus.InGameClasses.Select(c => catalog.Mods.Where(m => m.Classes.Contains(c.Id))));
        var globalFamilies = ModFamilies.Group(playable);
        foreach (var family in globalFamilies)
            Add(global, family.Pick(), null);
        foreach (var mod in playable)
            Add(global, new WantedMod(mod.Id), null);

        foreach (var cls in corpus.InGameClasses)
        {
            var mods = catalog.Mods.Where(m => m.Classes.Contains(cls.Id)).ToList();
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var family in ModFamilies.Group(mods))
                Add(set, family.Pick(), cls.Id);
            foreach (var mod in mods)
                Add(set, new WantedMod(mod.Id), cls.Id);
            // Global family picks can reach a class scope through import or
            // reconstruction; probe those shapes too.
            foreach (var family in globalFamilies)
                Add(set, family.Pick(), cls.Id);
            byClass[cls.Id] = set;
        }

        return new MinCapableTable { Global = global, ByClass = byClass };
    }
}
