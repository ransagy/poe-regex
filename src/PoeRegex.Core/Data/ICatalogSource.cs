namespace PoeRegex.Core.Data;

/// <summary>
/// Abstraction over where a catalog comes from: a file path in the CLI,
/// an embedded resource in a future WASM build, etc.
/// </summary>
public interface ICatalogSource
{
    Catalog Load(GameTarget game);
}
