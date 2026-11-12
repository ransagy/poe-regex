using System.Globalization;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Web.Core;

/// <summary>
/// One affix-name group inside a family: every catalog mod in the current
/// scope that carries the same affix name (one-hand/two-hand and other scope
/// variants merge — the in-game search keys on the name, which they share).
/// </summary>
public sealed record ModTier(string Name, IReadOnlyList<ModEntry> Members, int RequiredLevel)
{
    /// <summary>Cached case-insensitive search text: name plus every rendered
    /// line/range form. Built once per grouping instead of filtering members
    /// on every keystroke.</summary>
    public string SearchText { get; init; } = "";

    /// <summary>Representative member: chip identity and name-set anchor.</summary>
    public ModEntry Anchor => Members[0];

    private HashSet<string>? _memberIds;

    /// <summary>Cached member ids for highlight and exclusion checks.</summary>
    public IReadOnlySet<string> MemberIds =>
        _memberIds ??= Members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>One mod's lines: the anchor. A tier can merge variants from
    /// several classes; showing every member here made the tier look like a
    /// single hybrid mod. Use <see cref="UnionLines"/> for the merged view.</summary>
    public IReadOnlyList<string> DisplayLines =>
        Members[0].StatLines.Select(ModSearch.DisplayLine).ToList();

    /// <summary>One line per stat shape for the merged family view.</summary>
    public IReadOnlyList<string> ShapeLines => Members
        .SelectMany(m => m.StatLines)
        .Select(ModFamilies.ShapeOf)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Roll alternatives for an All-gear tier: one concrete line when all
    /// members agree, otherwise a compact `+1 / +2 ...` line. True multi-line
    /// mods keep their real lines instead of being joined.
    /// </summary>
    public IReadOnlyList<string> UnionLines
    {
        get
        {
            if (Members.Count == 1 || Members.Any(m => m.StatLines.Count != 1))
                return DisplayLines;
            var lines = Members
                .Select(m => ModSearch.DisplayLine(m.StatLines[0]))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ModFamilies.FirstNumber)
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToList();
            if (lines.Count <= 1) return lines;
            var compact = ModFamilies.CompactAlternatives(lines);
            return [compact ?? string.Join(" / ", lines)];
        }
    }

    /// <summary>Concrete member variants for the tier tooltip.</summary>
    public IReadOnlyList<string> VariantLines => Members
        .Select(m => string.Join(" / ", m.StatLines.Select(ModSearch.DisplayLine)))
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>Stable tier merge key inside a family.</summary>
    public string MergeKey { get; init; } = "";

    /// <summary>Empty for normal explicit affixes; "desecrated" for Abyss
    /// desecrated mods, "implicit" for base implicits.</summary>
    public string Source { get; init; } = "";

    /// <summary>User-facing tier label. Normal tiers use their affix name;
    /// desecrated and implicit tiers use their numeric range because their
    /// names repeat or are absent.</summary>
    public string DisplayLabel { get; init; } = "";
}

/// <summary>
/// A mod family (raw RePoE `type`): all tiers of one stat in the current
/// scope. Picking the family compiles the plain stat-text fragment (matches
/// every tier); picking a tier compiles its affix name instead.
/// </summary>
public sealed record ModFamily(string Key, bool IsPrefix, IReadOnlyList<ModTier> Tiers)
{
    /// <summary>Empty for normal explicit affixes; "desecrated" for Abyss
    /// desecrated mods, "implicit" for base implicits, "corrupted" for
    /// corruption enchantments. The family key carries the source prefix, so
    /// a sourced family with the same stat text as a normal family remains a
    /// distinct row.</summary>
    public string Source { get; init; } = "";

    /// <summary>Generation kind: `prefix`, `suffix`, or `implicit`.</summary>
    public string Kind { get; init; } = "";

    /// <summary>True when this family was merged for the All-gear view.</summary>
    public bool IsUnion { get; init; }

    public bool IsDesecrated => Source == "desecrated";

    public bool IsImplicit => Source == "implicit";

    public bool IsCorrupted => Source == "corrupted";

    public bool IsAugment => Source == "augment";

    /// <summary>A Shaman bonded effect attached to a rune or idol.</summary>
    public bool IsBonded => Source == "bonded";

    /// <summary>Augment and bonded families share the socketable picker section.</summary>
    public bool IsAugmentLike => IsAugment || IsBonded;

    /// <summary>CSS class for the source tag; empty for normal families.</summary>
    public string SourceCssClass => Source switch
    {
        "desecrated" => "source-desecrated",
        "implicit" => "source-implicit",
        "corrupted" => "source-corrupted",
        "augment" => "source-augment",
        "bonded" => "source-bonded",
        _ => "",
    };

    private IReadOnlyList<ModEntry>? _members;

    public IReadOnlyList<ModEntry> Members => _members ??= Tiers.SelectMany(t => t.Members).ToList();

    private HashSet<string>? _memberIds;

    public IReadOnlySet<string> MemberIds =>
        _memberIds ??= Members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>Copy with a filtered tier list, without carrying stale caches.</summary>
    public ModFamily WithTiers(IReadOnlyList<ModTier> tiers) =>
        new(Key, IsPrefix, tiers)
        {
            Source = Source,
            Kind = Kind,
            IsUnion = IsUnion,
            DisplayLines = DisplayLines,
        };

    /// <summary>Distinct normalized faction names (Amanamu, Kurgal, Ulaman)
    /// for the desecrated mods in this family.</summary>
    public IReadOnlyList<string> Factions => Members
        .Select(m => FactionName(m.Name))
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();

    /// <summary>Source tag for a non-normal row: the faction for desecrated
    /// mods, "Implicit" for base implicits; null when normal or no tag exists.</summary>
    public string? SourceTag => Source switch
    {
        "desecrated" => Factions.Count == 0 ? null : string.Join(" / ", Factions),
        "implicit" => "Implicit",
        "corrupted" => "Corrupted",
        "augment" or "bonded" => ModFamilies.AugmentSourceTag(
            Members, includeTargets: !IsUnion),
        _ => null,
    };

    /// <summary>Normalize `of Amanamu` / `Amanamu's` to `Amanamu`.</summary>
    public static string FactionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var value = name.Trim();
        if (value.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            value = value[3..];
        if (value.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        return value.Trim();
    }

    /// <summary>Representative member for whole-family picks (base tier).</summary>
    public ModEntry Anchor => Tiers[0].Anchor;

    /// <summary>Family text lines with the numbers removed, e.g.
    /// "+ to maximum Life" — one entry per stat line (hybrid families have
    /// two); names the stat without implying one tier's range (tier rows
    /// carry the real ranges).</summary>
    public required IReadOnlyList<string> DisplayLines { get; init; }

    /// <summary>Single-line form for chips: lines joined with `/`.</summary>
    public string DisplayText => string.Join(" / ", DisplayLines);

    /// <summary>
    /// The family pick: the anchor plus every tier variant. Compiling all
    /// variant mod ids ORs their fragments, so a family whose tiers differ in
    /// wording (singular/plural, an explicit "2") still matches every tier.
    /// </summary>
    public WantedMod Pick() => new(
        Anchor.Id,
        null,
        Tiers.SelectMany(t => t.Members)
            .Where(m => m.StatLines.Count > 0)
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList());

    /// <summary>This tier's affix name and every better tier's name, by
    /// required level (the honest "tier or better" — never game tier numbers).</summary>
    public IReadOnlyList<string> NamesFrom(ModTier tier) =>
        Source is "desecrated" or "implicit" or "corrupted" or "augment"
            ? []
            : Tiers
                .Where(t => t.Name.Length > 0 && t.RequiredLevel >= tier.RequiredLevel)
                .OrderBy(t => t.RequiredLevel)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
}

/// <summary>
/// Groups a scope's mods into families/tiers for the pickers. Pure and
/// browser-safe; ordering is deterministic (prefixes first, then display text).
/// </summary>
public static class ModFamilies
{
    /// <summary>
    /// Family identity for grouping and lookups. The source is part of the
    /// key so a desecrated or implicit family never merges with a normal
    /// family carrying the same stat text or raw RePoE type. Implicit keys
    /// use the rendered patterns so duplicate implicit records across bases
    /// still form one family.
    /// </summary>
    public static string KeyOf(ModEntry mod)
    {
        if (mod.Source is "implicit" or "corrupted")
        {
            var patterns = mod.StatLines
                .Select(line => line.Pattern)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pattern => pattern, StringComparer.Ordinal);
            return mod.Source + ":" + string.Join("\u0001", patterns);
        }
        var key = mod.Family.Length > 0 ? mod.Family : "\u0001" + mod.Id;
        return mod.Source switch
        {
            "desecrated" => "desecrated:" + key,
            "augment" => "augment:" + key,
            "bonded" => "bonded:" + key,
            _ => key,
        };
    }

    /// <summary>
    /// Families offered before the user types, loaded from
    /// <c>data/common-picks.json</c>. This compatibility view exposes the
    /// explicit family-key entries; visible-text entries are exposed through
    /// <see cref="CommonPicks.MatchTexts"/>.
    /// </summary>
    public static IReadOnlySet<string> CommonKeys => CommonPicks.FamilyKeys;

    /// <summary>
    /// True when the family is in the pre-typing shortlist. A null
    /// <paramref name="classId"/> is the All-gear/global view; class-scoped
    /// entries apply only to their class views.
    /// </summary>
    public static bool IsCommon(ModFamily family, string? classId = null) =>
        CommonPicks.IsCommon(family, classId);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IReadOnlyList<ModFamily>> GroupCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IReadOnlyList<ModFamily>> UnionGroupCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, UnionBuilder> UnionBuildCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<ModFamily>, IReadOnlyDictionary<string, ModFamily>> FamilyIndexCache = new();

    /// <summary>Mod id to family lookup for chip labels and highlighting.</summary>
    public static IReadOnlyDictionary<string, ModFamily> IndexOf(
        IReadOnlyList<ModFamily> families) =>
        FamilyIndexCache.GetValue(families, fs =>
        {
            var index = new Dictionary<string, ModFamily>(StringComparer.Ordinal);
            foreach (var family in fs)
            foreach (var member in family.Members)
                index[member.Id] = family;
            return index;
        });

    /// <summary>
    /// Shared grouping cache keyed by the stable mod-list instance from
    /// <see cref="EngineService"/>. Mode and slot switches reuse the same
    /// grouping instead of rebuilding every tier's search text.
    /// </summary>
    public static IReadOnlyList<ModFamily> CachedGroup(object mods, bool union = false)
    {
        // Build (and cache) the raw families even when the caller only needs
        // the All-gear union; query import and per-slot views reuse them.
        var raw = GroupCache.GetValue(
            mods, key => BuildFamilies((IEnumerable<ModEntry>)key));
        if (!union) return raw;
        return UnionGroupCache.GetValue(mods, _ =>
        {
            // Finish a warm-up already in progress instead of starting the
            // same union from scratch (first search can arrive mid-warm).
            var builder = UnionBuildCache.GetValue(mods, _ => new UnionBuilder(raw));
            builder.Step(int.MaxValue);
            UnionBuildCache.Remove(mods);
            return builder.Result;
        });
    }

    public static IReadOnlyList<ModFamily> Group(IEnumerable<ModEntry> mods, bool union = false)
    {
        var families = BuildFamilies(mods);
        return union ? UnionFamilies(families) : families;
    }

    private static List<ModFamily> BuildFamilies(IEnumerable<ModEntry> mods)
    {
        var families = new List<ModFamily>();
        foreach (var famGroup in mods
                     .GroupBy(m => (Key: KeyOf(m), Gen: m.GenerationType.ToLowerInvariant()))
                     .OrderBy(g => KindOrder(g.Key.Gen))
                     .ThenBy(g => string.Join(" · ", FamilyLines(g.ToList())), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key.Key, StringComparer.Ordinal))
        {
            var members = famGroup.ToList();
            var tiers = members
                .GroupBy(TierKey)
                .Select(g =>
                {
                    var tierMembers = g
                        .OrderBy(m => m.RequiredLevel)
                        .ThenBy(m => m.Id, StringComparer.Ordinal)
                        .ToList();
                    var anchor = tierMembers[0];
                    return new ModTier(anchor.Name ?? "", tierMembers, g.Min(m => m.RequiredLevel))
                    {
                        Source = anchor.Source,
                        DisplayLabel = DisplayLabelFor(tierMembers),
                        MergeKey = TierKey(anchor),
                        SearchText = SearchTextOf(tierMembers),
                    };
                })
                .OrderBy(t => t.RequiredLevel)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Anchor.Id, StringComparer.Ordinal)
                .ToList();
            families.Add(new ModFamily(famGroup.Key.Key, famGroup.Key.Gen == "prefix", tiers)
            {
                Source = members[0].Source,
                Kind = famGroup.Key.Gen,
                DisplayLines = FamilyLines(members),
            });
        }
        return families;
    }

    private static int KindOrder(string kind) => kind switch
    {
        "prefix" => 0,
        "suffix" => 1,
        "implicit" => 2,
        "corrupted" => 3,
        "augment" => 4,
        _ => 5,
    };

    private static List<ModFamily> UnionFamilies(IReadOnlyList<ModFamily> families)
    {
        var builder = new UnionBuilder(families);
        builder.Step(int.MaxValue);
        return builder.Result;
    }

    /// <summary>
    /// Builds the All-gear union in slices. The full list is only needed once
    /// the user searches or shows every mod, so a large catalog can warm it in
    /// small steps instead of blocking the UI thread with one long task.
    /// </summary>
    public static async Task<IReadOnlyList<ModFamily>> WarmUnionAsync(
        object mods, Func<bool>? cancelled = null)
    {
        if (UnionGroupCache.TryGetValue(mods, out var cached)) return cached;
        var raw = CachedGroup(mods);
        var builder = UnionBuildCache.GetValue(mods, _ => new UnionBuilder(raw));
        // ~32 groups is a few milliseconds even on the WASM runtime; the
        // delay returns to the browser event loop so a click during the
        // warm-up waits for one slice, not the whole build. A scope change
        // cancels the remaining slices so they cannot compete with the new
        // view; the builder stays cached for a later search to finish.
        while (!builder.Done)
        {
            if (cancelled?.Invoke() == true) return [];
            builder.Step(32);
            if (!builder.Done) await Task.Delay(16);
        }
        if (UnionGroupCache.TryGetValue(mods, out cached)) return cached;
        UnionGroupCache.Add(mods, builder.Result);
        UnionBuildCache.Remove(mods);
        return builder.Result;
    }

    /// <summary>
    /// One union pass, resumable so the warm-up path can spread it over
    /// several frames. Grouping and the non-augment signature set are built once.
    /// </summary>
    private sealed class UnionBuilder
    {
        // The All-gear union combines every class, so a rune that was kept for
        // one class can be redundant with a normal, implicit, corrupted or
        // desecrated family from another class. A rune is safe to omit only
        // when one non-augment family covers every one of its patterns; if the
        // patterns are split across rows, keep it so its combined query remains
        // selectable. Per-slot views still show the rune where it is the only
        // source. The pattern signature is the exact set key, so this is a
        // set lookup rather than a scan-and-compare over every normal family.
        private readonly HashSet<string> _nonAugmentSignatures;
        private readonly List<(string Key, List<ModFamily> Families)> _groups;
        private int _index;

        public UnionBuilder(IReadOnlyList<ModFamily> families)
        {
            _nonAugmentSignatures = families
                .Where(f => !f.IsAugmentLike)
                .Select(SignatureOf)
                .Where(signature => signature.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            _groups = families
                .GroupBy(UnionKeyOf)
                .Select(g => (Key: g.Key, Families: g.ToList()))
                .OrderBy(g => KindOrder(g.Families[0].Kind))
                .ThenBy(g => g.Families[0].DisplayText, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();
        }

        public List<ModFamily> Result { get; } = [];

        public bool Done => _index >= _groups.Count;

        public void Step(int count)
        {
            var end = Math.Min(_groups.Count, _index + count);
            for (; _index < end; _index++)
            {
                var (key, group) = _groups[_index];
                var union = Build(key, group);
                if (union is null) continue;
                Result.Add(union);
            }
        }

        private ModFamily? Build(string key, List<ModFamily> group)
        {
            var members = group.SelectMany(f => f.Members).ToList();
            var tiers = group
                .SelectMany(f => f.Tiers)
                .GroupBy(t => t.MergeKey, StringComparer.Ordinal)
                .Select(g =>
                {
                    var tierMembers = g
                        .SelectMany(t => t.Members)
                        .OrderBy(m => m.RequiredLevel)
                        .ThenBy(m => m.Id, StringComparer.Ordinal)
                        .ToList();
                    var anchor = tierMembers[0];
                    return new ModTier(anchor.Name ?? "", tierMembers, tierMembers.Min(m => m.RequiredLevel))
                    {
                        Source = anchor.Source,
                        DisplayLabel = DisplayLabelFor(tierMembers),
                        MergeKey = TierKey(anchor),
                        // Raw tiers already carry their search text; joining
                        // them avoids rebuilding every line for the union.
                        SearchText = string.Join("\n", g
                            .Select(t => t.SearchText)
                            .Distinct(StringComparer.Ordinal)),
                    };
                })
                .OrderBy(t => t.RequiredLevel)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Anchor.Id, StringComparer.Ordinal)
                .ToList();
            var union = new ModFamily(key, group[0].IsPrefix, tiers)
            {
                Source = group[0].Source,
                Kind = group[0].Kind,
                DisplayLines = FamilyLines(members),
                IsUnion = true,
            };
            if (union.IsAugmentLike &&
                _nonAugmentSignatures.Contains(SignatureOf(union)))
                return null;
            return union;
        }
    }

    /// <summary>Exact set key for a family's stat patterns.</summary>
    private static string SignatureOf(ModFamily family) =>
        SignatureOf(family.Members.SelectMany(m => m.StatLines));

    private static string SignatureOf(IEnumerable<StatLineEntry> lines) =>
        string.Join('\u0001', lines
            .Select(sl => sl.Pattern)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal));

    private static string UnionKeyOf(ModFamily family)
    {
        var signature = SignatureOf(family);
        // Augments are effect-first: the All-gear union merges the same effect
        // across targets/types, while normal and bonded variants stay apart.
        if (family.IsAugmentLike)
            return "union:augment:" + family.Source + ":" + signature;
        return "union:" + family.Source + ":" + family.Kind + ":" + signature;
    }

    /// <summary>
    /// Display tag for augment/bonded rows. Per-slot rows keep their single
    /// target; the All-gear union omits targets because the same effect is
    /// merged across them and the list is only noise in that view.
    /// </summary>
    public static string? AugmentSourceTag(
        IReadOnlyList<ModEntry> mods, bool includeTargets = true)
    {
        var augmentMods = mods
            .Where(m => m.Source is "augment" or "bonded")
            .ToList();
        if (augmentMods.Count == 0) return null;

        var parts = new List<string>();
        if (augmentMods.Any(m => m.Source == "bonded")) parts.Add("Bonded");
        var types = augmentMods
            .Select(m => m.AugmentType)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var names = augmentMods
            .Select(AugmentFamilyName)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var type = types.Count == 1 ? types[0] : null;
        if (names.Count == 1 && type is not null)
        {
            var name = names[0];
            if (!name.Contains(type, StringComparison.OrdinalIgnoreCase))
                parts.Add(type);
            parts.Add(name);
        }
        else if (type is not null)
        {
            parts.Add(type);
        }
        else if (types.Count > 0)
        {
            parts.Add(string.Join(" / ", types));
        }

        if (includeTargets)
        {
            var targets = augmentMods
                .Select(m => m.AugmentTarget)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targets.Count > 0) parts.Add(string.Join(", ", targets));
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string AugmentFamilyName(ModEntry mod)
    {
        var parts = mod.Family.Split(':', 3);
        return parts.Length >= 2 && parts[0] is "rune" or "soulcore" or "abyssaleye"
            ? parts[1].Trim()
            : (mod.Name ?? "").Trim();
    }

    internal static string? CompactAlternatives(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2) return null;
        var prefix = CommonPrefix(lines);
        var suffix = CommonSuffix(lines);
        if (prefix.Length == 0 && suffix.Length == 0) return null;
        if (lines.Any(line => prefix.Length + suffix.Length >= line.Length)) return null;
        var middles = lines
            .Select(line => line[prefix.Length..(line.Length - suffix.Length)])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (middles.Count < 2 || middles.Any(m => m.Length == 0)) return null;
        // Keep the prefix on every short alternative (`+1 / +2`). A numeric
        // prefix that ends at the number/range boundary (`10–`, `1.`) also
        // belongs to every middle; without it `10–15 / 18%` loses the base
        // number. Long textual prefixes (`Break Armour equal to 2–4 / 4–7`)
        // are still emitted once.
        var repeatPrefix =
            prefix.Length <= 2 ||
            (prefix.Any(char.IsDigit) &&
             middles.All(m => m.Any(char.IsDigit)) &&
             (char.IsDigit(prefix[^1]) || prefix[^1] is '–' or '-' or '.' or ':'));
        var body = repeatPrefix
            ? string.Join(" / ", middles.Select(m => prefix + m))
            : prefix + string.Join(" / ", middles);
        return body + suffix;
    }

    private static string CommonPrefix(IReadOnlyList<string> lines)
    {
        var first = lines[0];
        var length = first.Length;
        foreach (var line in lines.Skip(1))
        {
            length = Math.Min(length, line.Length);
            var i = 0;
            while (i < length && first[i] == line[i]) i++;
            length = i;
        }
        // Never end a common prefix in the middle of a digit run. A shared
        // leading digit would otherwise turn `10–19 / 15–29` into
        // `10–1 / 15–29`.
        while (length > 0 && char.IsDigit(first[length - 1]) &&
               lines.All(line => line.Length > length && char.IsDigit(line[length])))
        {
            length--;
        }
        return first[..length];
    }

    private static string CommonSuffix(IReadOnlyList<string> lines)
    {
        var first = lines[0];
        var length = 0;
        var minLength = lines.Min(line => line.Length);
        while (length < minLength &&
               lines.All(line => line[^(length + 1)] == first[^(length + 1)]))
        {
            length++;
        }
        // Likewise, never start a common suffix in the middle of a digit
        // run. The shared trailing `9` in `10–19 / 15–29` belongs to the
        // ranges, not to the stat text.
        while (length > 0 && char.IsDigit(first[^(length)]) &&
               lines.All(line => line.Length > length && char.IsDigit(line[^(length + 1)])))
        {
            length--;
        }
        return first[^length..];
    }

    internal static double FirstNumber(string text)
    {
        var match = NumberRe.Match(text);
        return match.Success &&
               double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string TierKey(ModEntry mod) =>
        mod.Source is "desecrated" or "implicit" or "corrupted"
            ? string.Join("\u0001", mod.StatLines
                .Select(sl => sl.Pattern)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal))
            : mod.Name ?? "";

    /// <summary>
    /// Tier label: the affix name for normal tiers, or every distinct range
    /// label for a desecrated or implicit tier merged across ranges.
    /// </summary>
    private static string DisplayLabelFor(IReadOnlyList<ModEntry> members)
    {
        var anchor = members[0];
        if (anchor.Source is not ("desecrated" or "implicit" or "corrupted")) return anchor.Name ?? "";
        return string.Join(" / ", members
            .Select(RangeLabel)
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(FirstNumber)
            .ThenBy(label => label, StringComparer.Ordinal));
    }

    private static string RangeLabel(ModEntry mod)
    {
        var parts = mod.StatLines
            .Select(sl =>
            {
                var range = RangeRe.Match(sl.RangeLine);
                if (range.Success)
                {
                    var percent = range.Groups[3].Success || sl.RangeLine.Contains('%')
                        ? "%"
                        : "";
                    return $"{range.Groups[1].Value}–{range.Groups[2].Value}{percent}";
                }
                if (sl.MinValue == 0 && sl.MaxValue == 0) return "";
                var min = sl.MinValue.ToString("0.##", CultureInfo.InvariantCulture);
                var max = sl.MaxValue.ToString("0.##", CultureInfo.InvariantCulture);
                var pct = sl.RangeLine.Contains('%') ? "%" : "";
                return min == max ? $"{min}{pct}" : $"{min}–{max}{pct}";
            })
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return string.Join(" / ", parts);
    }

    private static string SearchTextOf(IReadOnlyList<ModEntry> members)
    {
        var text = new System.Text.StringBuilder();
        foreach (var member in members)
        {
            if (!string.IsNullOrEmpty(member.Name)) text.Append(member.Name).Append('\n');
            foreach (var line in member.StatLines)
                text.Append(line.Line).Append('\n')
                    .Append(line.RangeLine).Append('\n')
                    .Append(AdvancedStatLine.For(line)).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Family-level text lines: the anchor member's stat shape with
    /// the numbers dropped (`+ to maximum Life`, `% increased Physical
    /// Damage`), one entry per stat line. Tier rows carry the real ranges.</summary>
    public static IReadOnlyList<string> FamilyLines(IReadOnlyList<ModEntry> members)
    {
        var anchor = members.OrderBy(m => m.RequiredLevel).ThenBy(m => m.Id, StringComparer.Ordinal).First();
        return anchor.StatLines.Select(ShapeOf).ToList();
    }

    /// <summary>Stat text with every digit placeholder dropped and spaces
    /// collapsed; comparison/searching still run on the real rendered lines.</summary>
    internal static string ShapeOf(StatLineEntry line) =>
        WhitespaceRe.Replace(line.Pattern.Replace(SuffixIndex.DigitPlaceholder, ' '), " ").Trim();

    private static readonly Regex WhitespaceRe = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex RangeRe = new(
        @"(\d+(?:\.\d+)?)\s*[–—-]\s*(\d+(?:\.\d+)?)(\s*%)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberRe = new(
        @"[+-]?\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
