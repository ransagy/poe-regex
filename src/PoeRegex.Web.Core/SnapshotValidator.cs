using System.Diagnostics.CodeAnalysis;

namespace PoeRegex.Web.Core;

/// <summary>Strict checks for picks that arrive from share links or saved recipes; both are untrusted.</summary>
public static class SnapshotValidator
{
    public const int MaxRecipeName = 100;
    private const int MaxPicks = 256;
    private const int MaxNames = 64;

    public static bool IsValid([NotNullWhen(true)] SavedRecipe? recipe) =>
        recipe is not null
        && IsText(recipe.Name) && recipe.Name.Length <= MaxRecipeName
        && IsValid(recipe.Snapshot);

    public static bool IsValid([NotNullWhen(true)] WorkbenchSnapshot? snap) =>
        snap is not null
        && snap.Version is >= 1 and <= WorkbenchSnapshot.CurrentVersion
        && IsText(snap.Mode)
        && IsOptionalText(snap.SelectedSlot) && IsOptionalText(snap.MultiFocus)
        && snap.Slots is not null && snap.Slots.Count <= GearSlot.All.Count
        && snap.Slots.All(kv => GearSlot.ById(kv.Key) is not null && IsValid(kv.Value, classless: false))
        && IsValid(snap.Global, classless: true);

    private static bool IsValid(SlotCriteria? crit, bool classless) =>
        crit is not null
        && (classless ? crit.ClassId is not null : IsText(crit.ClassId))
        && crit.Included is { Count: <= MaxPicks } && crit.Included.All(IsValid)
        && crit.Excluded is { Count: <= MaxPicks } && crit.Excluded.All(IsText);

    private static bool IsValid(WantedMod? pick) =>
        pick is not null
        && IsText(pick.ModId)
        && IsTextList(pick.Names) && IsTextList(pick.VariantModIds)
        && (pick.MinValue is not { } min || (!pick.IsNamePick && WantedMod.IsValidMinValue(min)));

    private static bool IsTextList(IReadOnlyList<string>? list) =>
        list is null || (list.Count <= MaxNames && list.All(IsText));

    private static bool IsText([NotNullWhen(true)] string? s) => !string.IsNullOrWhiteSpace(s);

    private static bool IsOptionalText(string? s) => s is null || IsText(s);
}
