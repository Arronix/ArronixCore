using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arronix.Generators;

/// <summary>Reports media declarations which cannot receive their generated Host-binding projection.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class MediaTypeAuthoringDiagnosticsGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MediaTypeMustBePartial = new(
        "ARX1003",
        "Media type declarations must be partial",
        "Media type '{0}' must be declared partial so Arronix can generate its compiled shape",
        "Arronix.Authoring",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The Arronix SDK generates the Host binding projection. Declare the media type partial; do not implement that projection yourself.");

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var invalidDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } declaration
                    && !declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)),
                static (syntax, _) => FindInvalidDeclaration(syntax))
            .Where(static declaration => declaration is not null)
            .Select(static (declaration, _) => declaration!);

        context.RegisterSourceOutput(invalidDeclarations, static (production, declaration) =>
            production.ReportDiagnostic(Diagnostic.Create(
                MediaTypeMustBePartial,
                declaration.Location,
                declaration.Name)));
    }

    private static InvalidDeclaration? FindInvalidDeclaration(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol
            || PlatformSymbols.Resolve(context.SemanticModel.Compilation) is not { } platform
            || platform.ClosedBase(symbol, PlatformSymbol.MediaType) is null)
        {
            return null;
        }

        return new InvalidDeclaration(declaration.Identifier.GetLocation(), symbol.Name);
    }

    private sealed class InvalidDeclaration
    {
        internal InvalidDeclaration(Location location, string name)
        {
            Location = location;
            Name = name;
        }

        internal Location Location { get; }

        internal string Name { get; }
    }
}
