using System.Runtime.CompilerServices;
using Arronix.Host.Engines.Items;
using Arronix.Host.Engines.Matching;
using Arronix.Host.Engines.Naming;
using Arronix.Host.Engines.Search;
using Arronix.Host.Media;
using Arronix.Host.Languages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;


namespace Arronix.Host.Composition;

/// <summary>
/// Fills the <see cref="DefinitionEngineCatalog"/>: which host engine executes which section of a
/// declaration, for this build.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is the only place the host says what it can execute, and this is the only place the catalog
/// is filled. Everything registered here is a factory over one <see cref="ValidatedDefinition"/>, because an
/// engine serves one media kind and is compiled from that kind's declaration at construction; there is no
/// per-kind branching left inside any of them.
/// </para>
/// <para>
/// Every applicable slot is filled, including the two the binder treats as optional. Optional means an absent
/// factory is not a <i>defect</i> — a definition whose quality section is the ladder-derived default and
/// whose naming section is the bare folder spine is fully served without a seam instance. It does not mean
/// the engines should be withheld: a definition that declares a real ladder or real templates has declared
/// behavior, and leaving the slot empty would admit the kind and then silently not execute the half of it
/// that was written down. Filling both keeps one rule — what a definition declares, the host runs.
/// </para>
/// <para>
/// The item source is shared across the slots rather than rebuilt per engine. The matcher, the planner and
/// the namer all read the same items as the registry's own <see cref="RegisteredMediaKind.Items"/>, and two
/// instances answering for one kind would be two catalogs the moment the storage milestone gives them rows
/// to hold.
/// </para>
/// </remarks>
internal static class EngineRegistration
{
    private static readonly ConditionalWeakTable<ValidatedDefinition, HostItemSource> Sources = [];

    /// <summary>
    /// Registers the engine catalog this build can execute definitions with.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddDefinitionEngines(this IServiceCollection services)
    {
        services.TryAddSingleton(provider =>
        {
            var languages = provider.GetRequiredService<LanguageTextService>();
            var strategies = MatchStrategyRegistry.CreateDefault(
                provider.GetRequiredService<TimeProvider>(),
                languages);

            return new DefinitionEngineCatalog
            {
                ItemStore = ItemsOf,
                Matcher = definition => new DeclarativeMatcher(
                    definition.Kind,
                    definition.Model.Matching,
                    strategies,
                    new ItemSourceEntryReader(ItemsOf(definition), definition.Shape)),
                QueryPlanner = definition => new DeclarativeQueryPlanner(
                    definition.Kind,
                    definition.Model.Querying,
                    definition.Shape.Declaration.SearchKinds,
                    new ItemSourceQueryReader(ItemsOf(definition)),
                    languages),
                // Typed release selection supersedes the legacy rung-shaped quality seam. Imperative
                // legacy media plugins may still register IQualityModel during their migration, but a
                // typed media declaration never manufactures one from its display shape.
                Quality = null,
                Naming = definition => new DeclarativeRenamePolicy(
                    definition.Kind,
                    definition.Shape.Declaration,
                    definition.Model.Naming,
                    new ItemSourceNamingResolver(ItemsOf(definition), definition.Shape),
                    languages: languages),
            };
        });

        return services;
    }

    /// <summary>
    /// Returns the one item source serving a definition, building it on first ask.
    /// </summary>
    /// <param name="definition">The kind's validated definition.</param>
    /// <returns>The item source.</returns>
    private static HostItemSource ItemsOf(ValidatedDefinition definition)
        => Sources.GetValue(definition, static resolved => new HostItemSource(resolved));
}
