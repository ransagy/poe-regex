namespace PoeRegex.Core.Data;

/// <summary>Abstraction over where the PoE2 catalog comes from.</summary>
public interface ICatalogSource
{
    Catalog Load();
}
