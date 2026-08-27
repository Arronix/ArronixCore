using Microsoft.CodeAnalysis;

namespace Arronix.Generators;

/// <summary>
/// The framework serialization types this generator reasons about, resolved from the compilation.
/// </summary>
/// <remarks>
/// Held as symbols and compared by identity. A name comparison answers the same for a type an author
/// declared with the same name in their own namespace, and what this generator decides is what a browser
/// is handed.
/// </remarks>
internal sealed class FrameworkSymbols
{
    private const string Namespace = "System.Text.Json.Serialization";

    private FrameworkSymbols(
        INamedTypeSymbol serializerContext,
        INamedTypeSymbol serializable,
        INamedTypeSymbol generationOptions,
        INamedTypeSymbol ignore,
        INamedTypeSymbol constructor)
    {
        SerializerContext = serializerContext;
        Serializable = serializable;
        GenerationOptions = generationOptions;
        Ignore = ignore;
        Constructor = constructor;
    }

    internal INamedTypeSymbol SerializerContext { get; }

    internal INamedTypeSymbol Serializable { get; }

    internal INamedTypeSymbol GenerationOptions { get; }

    internal INamedTypeSymbol Ignore { get; }

    internal INamedTypeSymbol Constructor { get; }

    /// <summary>Resolves the framework types, or nothing when this compilation has no serializer.</summary>
    /// <param name="compilation">The compilation being generated for.</param>
    /// <returns>The symbols, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Deliberately not <c>GetTypeByMetadataName</c>. That searches the compilation's own assembly first, so
    /// a package declaring a type with the exact framework metadata name would be handed back instead of the
    /// framework's — and this generator would then read that package's own attribute as the instruction that
    /// keeps a member off the wire, while the real serializer wrote it anyway.
    /// </para>
    /// <para>
    /// So every candidate is enumerated, the compilation's own assembly is excluded, and the assembly that
    /// declares <c>JsonSerializerContext</c> is required to declare all the rest. Two referenced assemblies
    /// declaring it is refused rather than guessed at.
    /// </para>
    /// </remarks>
    internal static FrameworkSymbols? Resolve(Compilation compilation)
    {
        if (Referenced(compilation, Namespace + ".JsonSerializerContext") is not { } serializerContext)
        {
            return null;
        }

        var framework = serializerContext.ContainingAssembly;
        var serializable = DeclaredBy(compilation, Namespace + ".JsonSerializableAttribute", framework);
        var generationOptions = DeclaredBy(compilation, Namespace + ".JsonSourceGenerationOptionsAttribute", framework);
        var ignore = DeclaredBy(compilation, Namespace + ".JsonIgnoreAttribute", framework);
        var constructor = DeclaredBy(compilation, Namespace + ".JsonConstructorAttribute", framework);

        return serializable is null || generationOptions is null || ignore is null || constructor is null
            ? null
            : new FrameworkSymbols(serializerContext, serializable, generationOptions, ignore, constructor);
    }

    /// <summary>Finds the one referenced declaration of a metadata name.</summary>
    private static INamedTypeSymbol? Referenced(Compilation compilation, string metadataName)
    {
        INamedTypeSymbol? found = null;

        foreach (var candidate in compilation.GetTypesByMetadataName(metadataName))
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, compilation.Assembly))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = candidate;
        }

        return found;
    }

    /// <summary>Finds the declaration of a metadata name in one exact assembly.</summary>
    private static INamedTypeSymbol? DeclaredBy(
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

    /// <summary>Determines whether an attribute is one of the framework's serialization attributes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns><see langword="true"/> when it comes from the framework's serialization namespace.</returns>
    /// <remarks>
    /// Compared by the namespace symbol of a type resolved from the compilation, so an author's own
    /// <c>System.Text.Json.Serialization</c> in some other assembly is a different namespace and a
    /// different answer.
    /// </remarks>
    internal bool IsSerializationAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } declared
        && SymbolEqualityComparer.Default.Equals(declared.ContainingNamespace, Ignore.ContainingNamespace)
        && SymbolEqualityComparer.Default.Equals(declared.ContainingAssembly, Ignore.ContainingAssembly);

    /// <summary>Determines whether an attribute is exactly one framework attribute.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="expected">The framework attribute type.</param>
    /// <returns><see langword="true"/> when they are the same type.</returns>
    internal static bool Is(AttributeData attribute, INamedTypeSymbol expected) =>
        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);
}
