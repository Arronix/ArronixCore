using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Arronix.Generators;

/// <summary>
/// How a symbol a generator decides on is found in a compilation, and compared once it is.
/// </summary>
/// <remarks>
/// A namespace and a name are not an identity, and <c>GetTypeByMetadataName</c> hands the compilation's own
/// declaration back first. Candidates are enumerated, the compilation's own assembly is never one, and the
/// answer must be unambiguous.
/// </remarks>
internal static class CompilationSymbols
{
    /// <summary>Finds the one referenced declaration of a metadata name, if there is exactly one.</summary>
    internal static INamedTypeSymbol? Referenced(Compilation compilation, string metadataName)
    {
        var candidates = ReferencedCandidates(compilation, metadataName);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Finds every referenced declaration of a metadata name, in reference order.</summary>
    /// <remarks>None and several are different answers to a caller that has to explain itself.</remarks>
    internal static IReadOnlyList<INamedTypeSymbol> ReferencedCandidates(
        Compilation compilation,
        string metadataName)
    {
        var found = new List<INamedTypeSymbol>();

        foreach (var candidate in compilation.GetTypesByMetadataName(metadataName))
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, compilation.Assembly))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>Finds the declaration of a metadata name in one exact assembly.</summary>
    internal static INamedTypeSymbol? DeclaredBy(
        Compilation compilation,
        string metadataName,
        IAssemblySymbol assembly)
    {
        foreach (var candidate in compilation.GetTypesByMetadataName(metadataName))
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, assembly))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The one base walk: each step compares definitions by identity, never by spelling.</summary>
    internal static INamedTypeSymbol? ClosedBase(INamedTypeSymbol type, INamedTypeSymbol definition)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, definition))
            {
                return current;
            }
        }

        return null;
    }
}
