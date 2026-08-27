using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Arronix.Generators;

/// <summary>
/// The framework serialization types this generator reasons about, resolved from the compilation.
/// </summary>
/// <remarks>Compared by symbol identity: a name comparison answers the same for an author's own type.</remarks>
internal sealed class FrameworkSymbols
{
    private const string Namespace = "System.Text.Json.Serialization";

    private FrameworkSymbols(
        INamedTypeSymbol serializerContext,
        INamedTypeSymbol serializable,
        INamedTypeSymbol generationOptions,
        INamedTypeSymbol ignore,
        INamedTypeSymbol constructor,
        IReadOnlyList<INamedTypeSymbol> callbacks)
    {
        Callbacks = callbacks;
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

    /// <summary>The contracts whose presence makes the framework run code against a value.</summary>
    internal IReadOnlyList<INamedTypeSymbol> Callbacks { get; }

    /// <summary>Resolves the framework types, or nothing when this compilation has no serializer.</summary>
    /// <param name="compilation">The compilation being generated for.</param>
    /// <returns>The symbols, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Not <c>GetTypeByMetadataName</c>, which searches the compilation's own assembly first and so returns
    /// an impostor declared under the exact framework name. The assembly declaring <c>JsonSerializerContext</c>
    /// is required to declare the rest.
    /// </remarks>
    internal static FrameworkSymbols? Resolve(Compilation compilation)
    {
        if (CompilationSymbols.Referenced(compilation, Namespace + ".JsonSerializerContext")
            is not { } serializerContext)
        {
            return null;
        }

        var framework = serializerContext.ContainingAssembly;
        var serializable = CompilationSymbols.DeclaredBy(compilation, Namespace + ".JsonSerializableAttribute", framework);
        var generationOptions = CompilationSymbols.DeclaredBy(compilation, Namespace + ".JsonSourceGenerationOptionsAttribute", framework);
        var ignore = CompilationSymbols.DeclaredBy(compilation, Namespace + ".JsonIgnoreAttribute", framework);
        var constructor = CompilationSymbols.DeclaredBy(compilation, Namespace + ".JsonConstructorAttribute", framework);
        var callbacks = new List<INamedTypeSymbol>();

        foreach (var name in new[] { "IJsonOnSerializing", "IJsonOnSerialized", "IJsonOnDeserializing", "IJsonOnDeserialized" })
        {
            if (CompilationSymbols.DeclaredBy(compilation, Namespace + "." + name, framework) is { } callback)
            {
                callbacks.Add(callback);
            }
        }

        return callbacks.Count != 4
            || serializable is null || generationOptions is null || ignore is null || constructor is null
            ? null
            : new FrameworkSymbols(serializerContext, serializable, generationOptions, ignore, constructor, callbacks);
    }

    /// <summary>Determines whether an attribute is one of the framework's serialization attributes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns><see langword="true"/> when it comes from the framework's serialization namespace.</returns>
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
