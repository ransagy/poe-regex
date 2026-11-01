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

    /// <summary>
    /// One segment of the ruler. Normally a segment is one quoted block; in
    /// simple any-of mode the single group is split into one segment per pick
    /// (or one shared segment when prefix factoring merged them).
    /// </summary>
    public sealed record RulerSegment(
        int Index, int BlockIndex, string Role, int Cost, string Label, IReadOnlyList<WantedMod> Picks);

    public string Query { get; private set; } = "";
    public int Length { get; private set; }
    public int Limit { get; private set; } = 250;
    public bool Over => Length > Limit;
    public MultiPasteResult? Paste { get; private set; }
    public IReadOnlyList<Block> Blocks => _blocks;
    public IReadOnlyList<RulerSegment> RulerSegments => _ruler;
    public Block? Anchor { get; private set; }

    /// <summary>Raised when the hover highlight moves, so every view showing blocks can repaint.</summary>
    public event Action? HotChanged;

    private List<Block> _blocks = [];
    private List<RulerSegment> _ruler = [];
    private List<PasteGroup> _groups = [];
    private List<string> _excludeOrder = [];
    private readonly Dictionary<string, List<int>> _scopeBlocks = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Scope, WantedMod Pick), int> _pickBlock = [];
    private readonly Dictionary<int, List<int>> _segmentsByBlock = [];
    private readonly Dictionary<WantedMod, List<int>> _segmentsByPick = [];
    private readonly HashSet<int> _hot = [];
    private readonly HashSet<int> _hotSegments = [];
    private readonly HashSet<WantedMod> _hotPicks = [];

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
        _hotSegments.Clear();
        _hotPicks.Clear();
        BuildBlocks();
        MapBlocks();
        BuildRulerSegments();
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

    /// <summary>
    /// Builds the ruler segments. Advanced and all-of keep one segment per
    /// quoted block; simple any-of splits its single block into per-pick
    /// segments aligned to the compiled alternatives. Prefix factoring can
    /// merge alternatives from several picks, so those picks share a segment.
    /// </summary>
    private void BuildRulerSegments()
    {
        _ruler = [];
        _segmentsByBlock.Clear();
        _segmentsByPick.Clear();
        if (_blocks.Count == 0) return;

        var split = state.Mode == "simple" && !state.Global.MatchAll
            ? BuildAnyOfSplit()
            : null;

        foreach (var block in _blocks)
        {
            if (split is not null && block.Index == split.BlockIndex)
            {
                var total = split.Weights.Sum();
                var remaining = block.Cost;
                for (var i = 0; i < split.Weights.Count; i++)
                {
                    var left = split.Weights.Count - i - 1;
                    var cost = i == split.Weights.Count - 1
                        ? remaining
                        : Math.Max(1, (int)Math.Round(
                            (double)block.Cost * split.Weights[i] / total,
                            MidpointRounding.AwayFromZero));
                    cost = Math.Min(cost, Math.Max(1, remaining - left));
                    remaining -= cost;
                    AddSegment(new RulerSegment(
                        _ruler.Count, block.Index, block.Role, cost,
                        split.Labels[i], split.Picks[i]));
                }
                continue;
            }
            AddSegment(new RulerSegment(
                _ruler.Count, block.Index, block.Role, block.Cost, block.Label, []));
        }
    }

    private void AddSegment(RulerSegment segment)
    {
        _ruler.Add(segment);
        if (!_segmentsByBlock.TryGetValue(segment.BlockIndex, out var blockSegs))
            _segmentsByBlock[segment.BlockIndex] = blockSegs = [];
        blockSegs.Add(segment.Index);
        foreach (var pick in segment.Picks)
        {
            if (!_segmentsByPick.TryGetValue(pick, out var pickSegs))
                _segmentsByPick[pick] = pickSegs = [];
            pickSegs.Add(segment.Index);
        }
    }

    private sealed record AnyOfSplit(
        int BlockIndex,
        IReadOnlyList<int> Weights,
        IReadOnlyList<string> Labels,
        IReadOnlyList<IReadOnlyList<WantedMod>> Picks);

    /// <summary>
    /// Splits the single simple any-of block using the same fragments and
    /// factoring the compiler used. Returns null when the block cannot be
    /// reproduced exactly (the caller then keeps the one-segment ruler).
    /// </summary>
    private AnyOfSplit? BuildAnyOfSplit()
    {
        var inc = _blocks.Where(b => b.Role == "inc").ToList();
        if (inc.Count != 1) return null;
        var block = inc[0];
        var picks = state.Global.Included.Where(p => PickFragment(p, null) is not null).ToList();
        if (picks.Count < 2) return null;

        var inputs = new List<string>();
        var inputPick = new List<int>();
        for (var i = 0; i < picks.Count; i++)
            foreach (var fragment in engine.RequirementFragments(picks[i], null))
            {
                inputs.Add(fragment);
                inputPick.Add(i);
            }
        var factored = CompactionEngine.FactorWithSources(inputs);
        if (factored.Count < 2) return null;
        var inner = string.Join("|", factored.Select(f => f.Text));
        var blockInner = block.Text.Length >= 2 ? block.Text[1..^1] : block.Text;
        if (!string.Equals(inner, blockInner, StringComparison.Ordinal)) return null;

        // Union picks that share a factored alternative (or an identical
        // fragment), then weight each group by its emitted text.
        var parent = Enumerable.Range(0, picks.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);
        foreach (var alt in factored)
        {
            var first = inputPick[alt.SourceIndices[0]];
            foreach (var source in alt.SourceIndices.Skip(1))
                Union(first, inputPick[source]);
        }

        var weights = new Dictionary<int, int>();
        var order = new List<int>();
        foreach (var alt in factored)
        {
            var root = Find(inputPick[alt.SourceIndices[0]]);
            if (!weights.ContainsKey(root)) order.Add(root);
            weights[root] = weights.GetValueOrDefault(root) + alt.Text.Length + 1;
        }
        if (order.Count < 2 || block.Cost < order.Count) return null;

        var groups = order.Select(root => (root, picks: picks
                .Where((_, i) => Find(i) == root)
                .ToList()))
            .ToList();
        return new AnyOfSplit(
            block.Index,
            groups.Select(g => weights[g.root]).ToList(),
            groups.Select(g => string.Join(" + ", g.picks.Select(PickLabel))).ToList(),
            groups.Select(g => (IReadOnlyList<WantedMod>)g.picks).ToList());
    }

    private string PickLabel(WantedMod pick)
    {
        if (pick.IsNamePick) return $"\"{pick.Names![0]}\"";
        var mods = engine.PlayableMods();
        return FamilyText(ModFamilies.CachedGroup(mods), mods, pick.ModId);
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
    public string? PickFragment(WantedMod pick, string? cls) => engine.PickFragment(pick, cls);

    public string? ExcludeFragment(string exc) => engine.ExcludeFragment(exc);

    public bool IsHot(int index) => _hot.Contains(index);

    public bool IsSegmentHot(int index) => _hotSegments.Contains(index);

    /// <summary>True when this pick owns a hot ruler segment (simple any-of).</summary>
    public bool IsPickHot(WantedMod pick) => _hotPicks.Contains(pick);

    public void Hot(int index) => Hot([index]);

    public void Hot(IEnumerable<int> indices)
    {
        var blocks = indices.ToHashSet();
        var segments = blocks
            .SelectMany(b => _segmentsByBlock.GetValueOrDefault(b) ?? [])
            .ToHashSet();
        var picks = segments
            .Where(s => s < _ruler.Count)
            .SelectMany(s => _ruler[s].Picks)
            .ToHashSet();
        ApplyHot(blocks, segments, picks);
    }

    /// <summary>Hover one ruler segment: its block, segment and picks light up.</summary>
    public void HotSegment(int index)
    {
        if (index < 0 || index >= _ruler.Count) return;
        var segment = _ruler[index];
        ApplyHot([segment.BlockIndex], [index], segment.Picks.ToHashSet());
    }

    /// <summary>Hover a Your-picks row: light every segment that pick owns.</summary>
    public bool HotPick(WantedMod pick)
    {
        if (!_segmentsByPick.TryGetValue(pick, out var segments) || segments.Count == 0) return false;
        var blocks = segments.Where(s => s < _ruler.Count)
            .Select(s => _ruler[s].BlockIndex).ToHashSet();
        ApplyHot(blocks, segments.ToHashSet(), [pick]);
        return true;
    }

    private void ApplyHot(HashSet<int> blocks, HashSet<int> segments, HashSet<WantedMod> picks)
    {
        if (blocks.SetEquals(_hot) && segments.SetEquals(_hotSegments) && picks.SetEquals(_hotPicks))
            return;
        _hot.Clear();
        _hot.UnionWith(blocks);
        _hotSegments.Clear();
        _hotSegments.UnionWith(segments);
        _hotPicks.Clear();
        _hotPicks.UnionWith(picks);
        HotChanged?.Invoke();
    }

    public void Cool() => ApplyHot([], [], []);

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
