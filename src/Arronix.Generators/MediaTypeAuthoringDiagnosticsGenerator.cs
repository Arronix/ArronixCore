using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arronix.Generators;

/// <summary>Reports media declarations which cannot receive their generated projections.</summary>
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

    private static readonly DiagnosticDescriptor PlatformSymbolsIncomplete = new(
        "ARX1004",
        "The referenced Arronix contract is incomplete",
        "'{0}' cannot be generated: {1}",
        "Arronix.Authoring",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reference exactly one Arronix.Abstractions, of the version this SDK supplies. The generators produce nothing unless every platform type resolves from it.");

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var reports = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntax, _) => Examine(syntax))
            .Where(static report => report is not null)
            .Select(static (report, _) => report!);

        context.RegisterSourceOutput(reports, static (production, report) =>
            production.ReportDiagnostic(report.Defect is null
                ? Diagnostic.Create(MediaTypeMustBePartial, report.Location, report.Name)
                : Diagnostic.Create(PlatformSymbolsIncomplete, report.Location, report.Name, report.Defect)));
    }

    /// <summary>Decides whether one declaration has something to report, and what.</summary>
    /// <remarks>
    /// The incomplete reading is asked first: while it holds, deciding that a declaration is a media type
    /// would compare its base against a symbol that did not resolve.
    /// </remarks>
    private static Report? Examine(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        var platform = PlatformSymbols.Read(context.SemanticModel.Compilation);

        if (platform.Defect is { } defect)
        {
            return platform.Authors(symbol)
                ? new Report(declaration.Identifier.GetLocation(), symbol.ToDisplayString(), defect)
                : null;
        }

        var partial = declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));

        return !partial
            && platform.Symbols is { } symbols
            && symbols.ClosedBase(symbol, PlatformSymbol.MediaType) is not null
                ? new Report(declaration.Identifier.GetLocation(), symbol.Name, null)
                : null;
    }

    private sealed class Report
    {
        internal Report(Location location, string name, string? defect)
        {
            Location = location;
            Name = name;
            Defect = defect;
        }

        internal Location Location { get; }

        internal string Name { get; }

        /// <summary>Gets why the platform types did not resolve, or null for the missing modifier.</summary>
        internal string? Defect { get; }
    }
}
