using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property reporting how far along something is.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class ProgressAttribute : Attribute;
