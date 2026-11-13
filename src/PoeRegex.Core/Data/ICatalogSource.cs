namespace PoeRegex.Core.Data;

/// <summary>Abstraction over where a game catalog comes from.</summary>
public interface ICatalogSource
{
    Catalog Load();
}
