using System.Diagnostics.CodeAnalysis;

namespace PoeRegex.Web.Core;

/// <summary>Strict checks for snapshots from localStorage, share links, or saved recipes; all are untrusted.</summary>
public static class SnapshotValidator
{
    public const int MaxRecipeName = 100;
    private const int MaxPicks = 256;
    private const int MaxNames = 64;
    private const int MaxTestItems = 64;

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
        && IsValid(snap.Global, classless: true)
        && snap.TestItems is not null && snap.TestItems.Count <= MaxTestItems
        && snap.TestItems.All(IsValid);

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

    private static bool IsValid(TestItem? item) =>
        item is not null
        && item.Name is not null
        && item.Parsed is not null
        && item.Parsed.BaseLines is not null
        && item.Parsed.Mods is not null
        && item.Parsed.Mods.All(m => m is not null
            && IsText(m.Kind)
            && m.Tags is not null
            && m.Lines is not null
            && m.Lines.All(l => l is not null && l.Text is not null && l.Rolls is not null))
        && item.Mapped is not null
        && item.Mapped.All(m => m is not null
            && m.Text is not null
            && m.CanonicalText is not null
            && m.Rolls is not null
            && m.Candidates is not null)
        && item.Cols is >= 0 and <= GridPlacement.Cols
        && item.Rows is >= 0 and <= GridPlacement.Rows
        && item.Row is >= -1 and <= GridPlacement.Rows
        && item.Col is >= -1 and <= GridPlacement.Cols;
}
