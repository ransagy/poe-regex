using PoeRegex.Core;
using PoeRegex.Core.Data;

namespace PoeRegex.Web.Core;

/// <summary>
/// Previews a scope's compile budget with extra picks applied — used to gate
/// bulk adds ("add all matching stats") before they would exceed the game's
/// character limit. Never throws.
/// </summary>
public static class SlotBudget
{
    /// <summary>
    /// Compiles the criteria plus <paramref name="extra"/> (identical picks
    /// ignored) with allowOverflow, so callers can read Length/Limit/Exceeded
    /// without exceptions.
    /// </summary>
    public static CompileResult Preview(
        EngineService engine, SlotCriteria crit, string? itemClass, IEnumerable<WantedMod> extra)
    {
        var picks = new List<WantedMod>(crit.Included);
        foreach (var pick in extra)
            if (!picks.Any(m => m.SamePick(pick)))
                picks.Add(pick);
        return engine.CompilePicks(picks, crit.Excluded, crit.MatchAll, itemClass);
    }
}
