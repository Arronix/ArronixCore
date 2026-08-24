# Copilot Instructions for Arronix

Read `CONTEXT.md`, `INTERFACE.md`, and `ARCHITECTURE.md` before changing the repository. `CONTEXT.md` is current operational truth, `INTERFACE.md` is the external semantic boundary, and `HISTORY.md` records why major boundaries changed.

## Hard rules

- Arronix is a clean-sheet implementation informed by *arr behavior, not a port of an *arr codebase.
- Universal contracts and Host remain media- and vendor-neutral.
- C# media-owned types are the schema. Do not replace a known shape with field dictionaries or string bags.
- A media kind retains `TItem`, `TTarget`, `TRelease`, and its parser through `MediaType<TItem,TTarget,TRelease,TParser>`.
- Catalogers and Curators are paired with the media-owned item type they return.
- Format capabilities own representation semantics and vocabularies. Video does not belong in Abstractions or Host.
- Provider modules register implementation types; Host activates admitted providers only through an exact
  public `(IPluginContext)` or parameterless constructor, never through Host DI.
- Do not add a second non-generic public media-type model, a universal `Evidence<T>` wrapper, a globally ordered evidence source, or executable objects to wire descriptors.
- Use Television, not only Movies, to pressure-test changes to typed media and release coverage.
- Do not add vendor-specific identifiers, routes, endpoints, or implementations to a media definition.
- Client references Abstractions only. Host references no format or media implementation.
- Media extensions may reference Abstractions, independently owned format capabilities, and their own media domain assembly, never Common, Plugins, Host, Api, Client, or another kind's media domain.
- A package ships zero or more shared contract assemblies and zero or one isolated executable entry assembly. A shared contract assembly (`Arronix.Media.Movies`, `Arronix.Format.Video`) holds typed owner semantics and pure deterministic behavior only; the executable half carries a name that says so (`Arronix.Plugin.Movies`, `Arronix.Format.Video.Contributions`). A media extension references a format's domain assembly and never its executable half: no `IPluginModule`, parser or registration execution, provider implementation, I/O, mutable process state, or module initializer.
- No plugin supplies UI markup. Descriptors support discovery and generic presentation; they are not a substitute for typed domain assemblies.
- Warnings are errors. Use the SDK pinned by `global.json`, run `eng/ci/run-tests.sh`, and report every result count and ratchet failure.
- Use American English in source and documentation.

## Contract lifecycle

The plugin-consumable `Arronix.Abstractions` contract version is `0.8.0`; first-party manifests declare
`>=0.8 <0.9`. Below 1.0, breaking SDK contract changes require a new minor identity and a synchronized
`docs/contracts/stability.md` entry. Public cross-assembly Host infrastructure is not thereby an extension
authoring contract. Do not ship changed SDK contracts under an older assembly version.

When a change alters public interfaces, domain ownership, dependency direction, lifecycle, or side effects, update `INTERFACE.md`, `HISTORY.md`, and `CONTEXT.md` atomically. Remove stale text rather than leaving competing descriptions.

## Current migration constraint

Movies is typed. Television, Music, and Books retain legacy imperative seams during conversion. `IQualityModel`, ladder DTOs, and the untyped parsed-quality string exist only as migration scaffolding. New features use typed releases, format-owned representations, `ReleasePolicy<TRelease>`, and `TargetMatch<TTarget>`; do not extend the legacy quality architecture.

The movies and video packages are split into a shared contract assembly plus an isolated executable
assembly, and video ships as the installed package `arronix.format.video`. A manifest declares its published
contract assemblies and its direct package dependencies; one resolver produces one resolved graph; the
installation admits each shared contract once into one Host-owned collectible context; and a package binds
only to contracts published by itself or by a package in its exact transitive dependency closure. See
`docs/research/g04/integrated-package-runtime.md`.

The real packaged Movies loader path is closed and package dependency, version rules and one exact CLR type
identity are closed with it. The active gate is G04, and it is open. Its pairing half is closed: a provider
names its item type once, in the contract it closes; family, item type and identity have one authority each;
and admission refuses an incoherent or unpairable provider contract before package code runs. What remains
is durable media item identity and catalog materialization, an owner decision recorded unchosen in
`docs/research/g04/media-item-identity-decision.md`. Do not move ahead to parser redesign or Client loading
until the gate passes, and do not report G04 complete on the strength of the pairing work.
