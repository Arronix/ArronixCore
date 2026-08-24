using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Media.Typed.Compilation;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// The entry point of the typed registration path: an item type and the type that declares it in, one
/// runtime model out.
/// </summary>
/// <remarks>
/// <para>
/// The whole derivation is here in four steps and nothing else calls into it. Read the entity, compile the
/// definition's typed values, derive the structure and intent surface, and carry the remaining data. What
/// comes out is what every engine, the binder, the intent registry and the client already
/// consume, so nothing downstream of this point knows a typed kind from a declared one.
/// </para>
/// <para>
/// Derivation producing a structurally invalid model is a <i>host</i> defect, not a plugin one, and it must
/// still be refused by the same gate a declared kind goes through rather than trusted because the host
/// wrote it.
/// </para>
/// </remarks>
public static class MediaTypeModelFactory
{
    /// <summary>
    /// Builds one media kind's runtime model from its typed declaration.
    /// </summary>
    /// <typeparam name="TItem">The kind's item type.</typeparam>
    /// <typeparam name="TTarget">The kind's acquisition-target type.</typeparam>
    /// <typeparam name="TRelease">The kind's interpreted-release type.</typeparam>
    /// <typeparam name="TParser">The kind's statically dispatched release parser.</typeparam>
    /// <typeparam name="TType">The type declaring the kind.</typeparam>
    /// <returns>The runtime model.</returns>
    /// <exception cref="ArgumentException">The item type is not a well-formed entity.</exception>
    public static IMediaTypeRuntime Build<TItem, TTarget, TRelease, TParser, TType>()
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
        where TType : MediaType<TItem, TTarget, TRelease, TParser>, new()
        => Build<TItem, TTarget, TRelease, TParser>(new TType());

    /// <summary>Builds one media kind from an already captured definition instance.</summary>
    internal static IMediaTypeRuntime Build<TItem, TTarget, TRelease, TParser>(
        MediaType<TItem, TTarget, TRelease, TParser> definition)
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
    {
        ArgumentNullException.ThrowIfNull(definition);
        var declaration = new TypedDeclaration();
        MediaDefinitionCompiler.Apply<TItem, TTarget, TRelease, TParser>(declaration, definition);

        return Build<TItem, TTarget, TRelease, TParser>(definition.Kind, declaration);
    }

    /// <summary>
    /// Builds one media kind's runtime model from an already-compiled internal declaration.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <typeparam name="TTarget">The acquisition-target type.</typeparam>
    /// <typeparam name="TRelease">The interpreted-release type.</typeparam>
    /// <typeparam name="TParser">The statically dispatched release parser.</typeparam>
    /// <param name="kind">The media kind identifier.</param>
    /// <param name="declaration">The one-way internal projection of the typed definition.</param>
    /// <returns>The runtime model.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The item type is not a well-formed entity.</exception>
    internal static IMediaTypeRuntime Build<TItem, TTarget, TRelease, TParser>(
        MediaKindId kind,
        TypedDeclaration declaration)
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var compiledShapes = declaration.CompiledShapes
            ?? throw new ArgumentException("The typed definition supplied no compile-time field projection.", nameof(declaration));
        var reading = ItemTypeReader.Read(compiledShapes.Item);
        var shape = ShapeDerivation.Derive(kind, reading, declaration, compiledShapes);
        var intent = IntentDerivation.Derive(kind, reading, shape, declaration, compiledShapes);

        return new TypedMediaType<TItem, TTarget, TRelease, TParser>(
            kind,
            [.. declaration.Groups.Select(static draft => draft.GroupType)],
            shape,
            intent,
            CarryModel(declaration, shape),
            declaration.ReleasePolicy as ReleasePolicy<TRelease>,
            new ItemProjector(
                kind,
                shape.Levels[0].Id,
                reading,
                declaration.Groups.ToDictionary(
                    static draft => draft.GroupType,
                    static draft => draft.AxisId)));
    }

    /// <summary>
    /// Carries the sections a later iteration types, with every reference into the item already turned into
    /// a field path during compilation.
    /// </summary>
    private static MediaKindModel CarryModel(TypedDeclaration declaration, MediaShape shape) =>
        new()
        {
            Parsing = null,

            Matching = new MatchDeclaration
            {
                Entry = new EntryResolution
                {
                    // Empty, and that is the fix rather than an omission: which catalog to trust first when
                    // several answer is provider-reliability knowledge, so it is host configuration over the
                    // installed catalogers rather than a list inside a media kind.
                    IdentifierOrder = [],
                    ScopeReplacesSearch = declaration.ScopeReplacesSearch,
                    Layers = declaration.MatchLayers,
                    Agreements = declaration.Agreements,
                    Ambiguity = declaration.Ambiguity
                },

                // Derived rather than declared. A kind with exactly one coordinate space has exactly one
                // way to get from a reading to a unit, and the file binding already said one unit per
                // entry — so the rule that would be written out is the only rule that could be written,
                // and writing it out would be restating the structure. A kind with several spaces has a
                // real choice to make and declares it; deriving one for that case would be guessing, so
                // nothing is derived and the gate refuses until it says.
                Units = DeriveUnits(shape),

                // Empty on purpose. How far to trust a match arrived at by identifier, by title-and-year
                // or by title alone is the same question for every media kind, so it is host policy and
                // the matcher supplies it. A per-kind table here was a table every kind copied.
                Confidence = []
            },

            Querying = new QueryDeclaration
            {
                Tiers = [.. declaration.Tiers.Select(DeriveTier)],
                Aliases = declaration.Aliases,
                Grammar = CoordinateGrammar.None,

                // Host search policy rather than a fact about any media kind: how many results an origin is
                // worth is the same question for every kind, and answering it per kind made behaviour differ
                // between kinds for no reason anybody could name.
                Limits = [],
                Substitutions = []
            },

            Naming = new NamingDeclaration
            {
                DefaultTemplates = declaration.Templates,
                Selection = declaration.TemplateSelection,
                MultiUnitStyles = [],
                FolderSpine = declaration.FolderSpine,
                Fallbacks = declaration.TokenFallbacks
            },

            Notifications = new NotificationDeclaration
            {
                HeadlineTemplate = declaration.HeadlineTemplate,
                HeadlineMaxLength = declaration.HeadlineMaxLength,
                BodyFieldId = declaration.BodyFieldId,
                BodyMaxLength = declaration.BodyMaxLength,

                // Four members used to be set to empty here and are now gone from the contract entirely:
                // the deep link, which is the host's own routing scheme and never a media kind's business;
                // the outbound catalog addresses, which belong to whoever owns the identifier; and the
                // occasion phrases and artwork role order, which are the same for every kind and so are
                // host-owned and localizable once.
                Fields = declaration.SummaryFields,
                GroupSummaries = declaration.GroupSummaries
            },

            TemplateRules = declaration.TemplateRules,
        };

    /// <summary>
    /// Derives the unit-resolution table from the structure, when the structure admits one reading.
    /// </summary>
    /// <param name="shape">The derived structure.</param>
    /// <returns>The single implied rule, or nothing when the structure does not imply one.</returns>
    private static IReadOnlyList<UnitResolutionRule> DeriveUnits(MediaShape shape) =>
        shape.CoordinateSpaces.Count == 1
            ?
            [
                new UnitResolutionRule
                {
                    ReleaseKind = null,
                    Spaces = [new SpaceAttempt { SpaceId = shape.CoordinateSpaces[0].SpaceId }],
                    Expansion = SpanExpansion.None,
                },
            ]
            : [];

    private static QueryTierTemplate DeriveTier(QueryTierDraft draft) =>
        new()
        {
            TierId = draft.TierId,
            SearchKindId = draft.SearchKindId,
            Order = draft.Order,
            Origins = draft.Origins,
            Arguments = draft.Arguments,
            FreeTextTemplate = draft.FreeTextTemplate,

            // A required identifier role becomes a required field path rather than a scheme name, so a tier
            // that needs an identifier works with whichever cataloger is installed rather than only with the
            // one the kind was written against.
            RequiredFields =
            [
                .. draft.RequiredFields,
                .. draft.RequiredRoles.Select(static role => $"identity.{DerivedNames.Identifier(role.ToString())}")
            ],
            FanOutPerAlias = draft.FanOutPerAlias,
            CarryAliases = draft.CarryAliases
        };

    /// <summary>
    /// One media kind's runtime model, as the host holds it.
    /// </summary>
    private sealed class TypedMediaType<TItem, TTarget, TRelease, TParser>(
        MediaKindId kind,
        IReadOnlyList<Type> groupTypes,
        MediaShape shape,
        PluginIntentSurface intent,
        MediaKindModel model,
        ReleasePolicy<TRelease>? releasePolicy,
        ItemProjector projector) : IMediaTypeRuntime<TItem, TTarget, TRelease>
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
    {
        /// <inheritdoc />
        public MediaKindId Kind { get; } = kind;

        /// <inheritdoc />
        public Type ItemType { get; } = typeof(TItem);

        /// <inheritdoc />
        public Type TargetType { get; } = typeof(TTarget);

        /// <inheritdoc />
        public Type ReleaseType { get; } = typeof(TRelease);

        /// <inheritdoc />
        public Type ParserType { get; } = typeof(TParser);

        /// <inheritdoc />
        public IReadOnlyList<Type> GroupTypes { get; } = groupTypes;

        /// <inheritdoc />
        public MediaShape Shape { get; } = shape;

        /// <inheritdoc />
        public PluginIntentSurface Intent { get; } = intent;

        /// <inheritdoc />
        public MediaKindModel Model { get; } = model;

        /// <inheritdoc />
        public bool HasReleasePolicy => ReleasePolicy is not null;

        /// <inheritdoc />
        public IRelease? Parse(ReleaseParseContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return TParser.Parse(context).Release;
        }

        /// <inheritdoc />
        public ReleasePolicy<TRelease>? ReleasePolicy { get; } = releasePolicy;

        /// <inheritdoc />
        public ItemView Project(MediaItemRef reference, object item, CatalogIdentity identity)
            => projector.Project(reference, item, identity);

        /// <inheritdoc />
        public FieldValue Read(object item, string fieldId, CatalogIdentity identity)
            => projector.Read(item, fieldId, identity);
    }
}
