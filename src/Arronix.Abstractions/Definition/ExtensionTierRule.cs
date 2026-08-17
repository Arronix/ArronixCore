using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One container-extension fallback of a rung-resolution table.
/// </summary>
/// <param name="Extension">The file extension, leading dot included; <c>"*"</c> is the default row.</param>
/// <param name="TierId">The tier the extension implies, by name.</param>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct ExtensionTierRule(string Extension, string TierId);
