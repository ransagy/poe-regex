using PoeRegex.Web.Core;

namespace web.Services;

/// <summary>
/// The value-threshold scopes worth warming for the current workbench. Every
/// active slot is included, not just the focused one, so a non-focused slot's
/// min box appears after its own class warms.
/// </summary>
public static class ValuePrewarmTargets
{
    public static IReadOnlyList<(string Key, string? ItemClass)> For(
        WorkbenchState state, EngineService engine)
    {
        if (state.Mode != "advanced")
            return state.Global.Included.Any(engine.MayAcceptMin)
                ? [("simple|global", null)]
                : [];

        var targets = new List<(string Key, string? ItemClass)>();
        var seenClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in state.ActiveSlots())
        {
            if (!state.CriteriaFor(slot).Included.Any(engine.MayAcceptMin)) continue;
            var cls = state.SlotClassOrDefault(slot);
            if (cls.Length == 0 || !seenClasses.Add(cls)) continue;
            targets.Add(($"advanced|{cls}", cls));
        }
        return targets;
    }
}
