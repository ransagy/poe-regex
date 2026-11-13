using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Data;

/// <summary>
/// One game's catalog plus the precomputed table and foreign-line corpora used
/// by the engine. PoE2 uses the embedded artifacts; PoE1 supplies its own
/// loaded data.
/// </summary>
public sealed record GameData(
    Catalog Catalog,
    FragmentTable? Fragments = null,
    VendorCorpusFile? Vendor = null,
    IReadOnlyList<SafetyItem>? Safety = null);
