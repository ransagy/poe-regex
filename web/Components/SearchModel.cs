using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;
using PoeRegex.Web.Core;

namespace web.Components;

/// <summary>The compiled string split into its quoted blocks, which picks own which block, and the shared hover highlight.</summary>
public sealed class SearchModel(EngineService engine, WorkbenchState state)
{
    public sealed record Block(int Index, string Role, string Text, int Cost, string Label);

    public string Query { get; private set; } = "";
    public int Length { get; private set; }
    public int Limit { get; private set; } = 250;
    public bool Over => Length > Limit;
    public MultiPasteResult? Paste { get; private set; }
    public IReadOnlyList<Block> Blocks => _blocks;
    public Block? Anchor { get; private set; }

    /// <summary>Raised when the hover highlight moves, so every view showing blocks can repaint.</summary>
    public event Action? HotChanged;

    private List<Block> _blocks = [];
    private List<PasteGroup> _groups = [];
    private List<string> _excludeOrder = [];
    private readonly Dictionary<string, List<int>> _scopeBlocks = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Scope, WantedMod Pick), int> _pickBlock = [];
    private readonly HashSet<int> _hot = [];
    private readonly Dictionary<(WantedMod Pick, string? Class), string?> _pickFragments = [];
    private readonly Dictionary<string, string?> _excludeFragments = new(StringComparer.Ordinal);

    public void Update(CompileResult? simple, MultiPasteResult? paste)
    {
        Paste = state.Mode == "advanced" ? paste : null;
        if (Paste is not null)
        {
            (Query, Length, Limit) = (Paste.Query, Paste.Length, Paste.Limit);
            _groups = Paste.Groups.ToList();
        }
        else if (state.Mode == "simple" && simple is not null)
        {
            (Query, Length, Limit) = (simple.Query, simple.Length, simple.Limit);
            _groups = [];
        }
        else
        {
            (Query, Length, Limit) = ("", 0, 250);
            _groups = [];
        }
        _excludeOrder = state.Mode == "advanced"
            ? state.ActiveSlots().SelectMany(s => state.CriteriaFor(s).Excluded)
                .Concat(state.Global.Excluded).Distinct(StringComparer.Ordinal).ToList()
            : state.Global.Excluded.ToList();
        _hot.Clear();
        BuildBlocks();
        MapBlocks();
    }

    /// <summary>Leaves the string unsegmented when re-joining its blocks would not reproduce it exactly.</summary>
    private void BuildBlocks()
    {
        _blocks = [];
        Anchor = null;
        if (Query.Length == 0) return;
        var tokens = Explainer.Tokenize(Query);
        var texts = tokens.Select(t => (t.Excluded ? "!" : "") + "\"" + t.Phrase + "\"").ToList();
        if (!string.Equals(string.Join(" ", texts), Query, StringComparison.Ordinal)) return;

        var advanced = state.Mode == "advanced";
        var excIndex = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var cost = texts[i].Length + (i > 0 ? 1 : 0);
            string role, label;
            if (tokens[i].Excluded)
            {
                role = "exc";
                var exc = excIndex < _excludeOrder.Count ? _excludeOrder[excIndex] : tokens[i].Phrase;
                excIndex++;
                label = $"None of: {ExcludeLabel(exc, engine.PlayableMods())}";
            }
            else if (advanced && i == 0)
            {
                role = "anchor";
                label = "Item classes";
            }
            else if (advanced && i - 1 < _groups.Count)
            {
                role = "inc";
                label = string.Join(" + ", state.ActiveSlots()
                    .Where(s => _groups[i - 1].TargetClasses.Contains(state.SlotClassOrDefault(s), StringComparer.Ordinal))
                    .Select(SlotLabel).Distinct());
            }
            else
            {
                role = "inc";
                label = state.Global.MatchAll ? "One of your picks" : "Any of your picks";
            }
            _blocks.Add(new Block(i, role, texts[i], cost, label));
        }
        Anchor = advanced ? _blocks.FirstOrDefault(b => b.Role == "anchor") : null;
    }

    private void MapBlocks()
    {
        _scopeBlocks.Clear();
        _pickBlock.Clear();
        if (_blocks.Count == 0) return;
        if (state.Mode == "advanced")
        {
            foreach (var slot in state.ActiveSlots())
            {
                var cls = state.SlotClassOrDefault(slot);
                var indices = _groups
                    .Select((g, i) => (g, i))
                    .Where(x => x.g.TargetClasses.Contains(cls, StringComparer.Ordinal))
                    .Select(x => x.i + 1)
                    .Where(i => i < _blocks.Count && _blocks[i].Role == "inc")
                    .ToList();
                _scopeBlocks[slot] = indices;
                MapPicks(slot, state.CriteriaFor(slot), cls, indices);
            }
        }
        else
        {
            var indices = _blocks.Where(b => b.Role == "inc").Select(b => b.Index).ToList();
            _scopeBlocks["global"] = indices;
            MapPicks(null, state.Global, null, indices);
        }
    }

    private void MapPicks(string? slot, SlotCriteria crit, string? cls, List<int> indices)
    {
        if (!crit.MatchAll) return;
        var resolved = crit.Included.Where(p => PickFragment(p, cls) is not null).ToList();
        if (resolved.Count != indices.Count) return;
        for (var i = 0; i < resolved.Count; i++)
            _pickBlock[(slot ?? "global", resolved[i])] = indices[i];
    }

    public List<int> ScopeBlocks(string? slot) =>
        _scopeBlocks.TryGetValue(slot ?? "global", out var list) ? list : [];

    /// <summary>The block a pick owns when every pick is required; -1 when the pick shares one block.</summary>
    public int PickBlock(string? slot, WantedMod pick) =>
        _pickBlock.TryGetValue((slot ?? "global", pick), out var i) ? i : -1;

    public int Cost(IEnumerable<int> indices) =>
        indices.Sum(i => i < _blocks.Count ? _blocks[i].Cost : 0);

    public Block? ExcludeBlock(string exc)
    {
        var i = _excludeOrder.IndexOf(exc);
        if (i < 0) return null;
        var excBlocks = _blocks.Where(b => b.Role == "exc").ToList();
        return i < excBlocks.Count ? excBlocks[i] : null;
    }

    public bool MergedToAny(string cls) =>
        Paste?.MergedToAnyClasses.Contains(cls, StringComparer.Ordinal) == true;

    /// <summary>The fragment one pick compiles to on its own (cached).</summary>
    public string? PickFragment(WantedMod pick, string? cls)
    {
        if (_pickFragments.TryGetValue((pick, cls), out var cached)) return cached;
        var result = engine.Compile(pick.Requirements(), [], itemClass: cls, allowOverflow: true);
        var frag = Explainer.Tokenize(result.Query).Where(t => !t.Excluded).Select(t => t.Phrase)
            .Skip(cls is null ? 0 : 1).FirstOrDefault();
        _pickFragments[(pick, cls)] = frag;
        return frag;
    }

    public string? ExcludeFragment(string exc)
    {
        if (_excludeFragments.TryGetValue(exc, out var cached)) return cached;
        var result = engine.Compile([], [new ModifierRequirement(exc)], allowOverflow: true);
        var frag = Explainer.Tokenize(result.Query).Where(t => t.Excluded).Select(t => t.Phrase).FirstOrDefault();
        _excludeFragments[exc] = frag;
        return frag;
    }

    public bool IsHot(int index) => _hot.Contains(index);

    public void Hot(int index) => Hot([index]);

    public void Hot(IEnumerable<int> indices)
    {
        var next = indices.ToHashSet();
        if (next.SetEquals(_hot)) return;
        _hot.Clear();
        _hot.UnionWith(next);
        HotChanged?.Invoke();
    }

    public void Cool() => Hot([]);

    public static string SlotLabel(string slot) => GearSlot.ById(slot)?.Label ?? slot;

    public static string FamilyText(
        IReadOnlyList<ModFamily> families, IReadOnlyList<ModEntry> mods, string modId,
        EngineService? engine = null)
    {
        var mod = mods.FirstOrDefault(m => m.Id == modId);
        if (mod is null && engine?.FindMod(modId) is { } globalMod)
        {
            // A family anchor may live on another class (the melee skill-level
            // family has Gloves and weapon tiers); still show the family text.
            var globalFamily = ModFamilies.CachedGroup(engine!.PlayableMods())
                .FirstOrDefault(f => f.Key == ModFamilies.KeyOf(globalMod));
            if (globalFamily is not null) return globalFamily.DisplayText;
            return globalMod.StatLines.Count > 0
                ? ModSearch.DisplayLine(globalMod.StatLines[0])
                : globalMod.Name is { Length: > 0 } ? globalMod.Name : modId;
        }
        if (mod is null) return modId;
        var fam = families.FirstOrDefault(f => f.Key == ModFamilies.KeyOf(mod));
        return fam?.DisplayText
               ?? (mod.StatLines.Count > 0 ? ModSearch.DisplayLine(mod.StatLines[0]) : mod.Id);
    }

    /// <summary>A family exclude names its stat text; a tier exclude is the quoted affix name.</summary>
    public static string ExcludeLabel(string exc, IReadOnlyList<ModEntry> mods)
    {
        if (!mods.Any(m => m.Id == exc)) return $"\"{exc}\"";
        return FamilyText(ModFamilies.CachedGroup(mods), mods, exc);
    }

    /// <summary>
    /// The strip compilation for a workbench state, shared by the page and the
    /// import preview so both render the same blocks.
    /// </summary>
    public static (CompileResult? Simple, MultiPasteResult? Paste) CompileForStrip(
        EngineService engine, WorkbenchState state)
    {
        if (state.Mode != "advanced")
            return (engine.CompilePicks(
                state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null), null);

        var active = state.ActiveSlots();
        if (active.Count == 0) return (null, null);
        var paste = engine.Combine(
            active.Select(s =>
            {
                var crit = state.CriteriaFor(s);
                return ($"{s} ({crit.ClassId})", crit.ClassId,
                    (IEnumerable<WantedMod>)crit.Included, crit.MatchAll);
            }),
            active.SelectMany(s => state.CriteriaFor(s).Excluded)
                .Concat(state.Global.Excluded).Distinct(StringComparer.Ordinal),
            allowOverflow: true);
        return (null, paste);
    }
}
