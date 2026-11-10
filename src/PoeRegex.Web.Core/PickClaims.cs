namespace PoeRegex.Web.Core;

/// <summary>
/// Claim-covered selection helpers. The unit of identity is the actual
/// compiled query for a scope, not the mod/family row: two mods or tiers that
/// compile to the same positive phrase share one claim. The UI keeps every
/// row visible; one row owns the claim and the others are covered until the
/// user switches the claim.
/// </summary>
public static class PickClaims
{
    public static string? IncludeKey(EngineService engine, WantedMod pick, string? cls) =>
        engine.PickClaimKey(pick, cls);

    public static string? ExcludeKey(EngineService engine, string exclusion, string? cls) =>
        engine.ExcludeClaimKey(exclusion, cls);

    public static WantedMod? FindInclude(
        SlotCriteria criteria, EngineService engine, string? cls, string key) =>
        criteria.Included.FirstOrDefault(pick =>
            string.Equals(engine.PickClaimKey(pick, cls), key, StringComparison.Ordinal));

    public static IReadOnlyList<WantedMod> FindIncludes(
        SlotCriteria criteria, EngineService engine, string? cls, string key) =>
        criteria.Included
            .Where(pick => string.Equals(engine.PickClaimKey(pick, cls), key, StringComparison.Ordinal))
            .ToList();

    public static IReadOnlyList<string> FindExcludes(
        SlotCriteria criteria, EngineService engine, string? cls, string key) =>
        criteria.Excluded
            .Where(exc => string.Equals(engine.ExcludeClaimKey(exc, cls), key, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Deduplicate legacy/imported state to one include and one exclusion per
    /// claim. If a claim appears on both sides, the include wins; the UI never
    /// creates that state, but old snapshots and imports can.
    /// </summary>
    public static bool Normalize(SlotCriteria criteria, EngineService engine, string? cls)
    {
        var includedBefore = criteria.Included.Count;
        var excludedBefore = criteria.Excluded.Count;
        var includeKeys = new HashSet<string>(StringComparer.Ordinal);
        criteria.Included.RemoveAll(pick =>
        {
            var key = engine.PickClaimKey(pick, cls);
            return key is not null && !includeKeys.Add(key);
        });

        var excludeKeys = new HashSet<string>(StringComparer.Ordinal);
        criteria.Excluded.RemoveAll(exc =>
        {
            var key = engine.ExcludeClaimKey(exc, cls);
            if (key is null) return false;
            if (includeKeys.Contains(key)) return true;
            return !excludeKeys.Add(key);
        });
        return criteria.Included.Count != includedBefore || criteria.Excluded.Count != excludedBefore;
    }
}
