using System.Text.Json.Serialization;
using PoeRegex.Core;

namespace PoeRegex.Core.Data;

/// <summary>
/// Source-generated JSON contract for the engine's data types. Using
/// source generation keeps deserialization Native AOT-safe (no reflection).
/// </summary>
[JsonSerializable(typeof(Catalog))]
[JsonSerializable(typeof(ItemClassEntry))]
[JsonSerializable(typeof(ModEntry))]
[JsonSerializable(typeof(StatLineEntry))]
[JsonSerializable(typeof(GameRules))]
public sealed partial class CatalogJsonContext : JsonSerializerContext;
