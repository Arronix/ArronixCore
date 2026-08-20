# Copilot Instructions for Arronix

Read `CONTEXT.md`, `INTERFACE.md`, and `ARCHITECTURE.md` before changing the repository. `CONTEXT.md` is current operational truth, `INTERFACE.md` is the external semantic boundary, and `HISTORY.md` records why major boundaries changed.

## Hard rules

- Arronix is a clean-sheet implementation informed by *arr behavior, not a port of an *arr codebase.
- Universal contracts and Host remain media- and vendor-neutral.
- C# media-owned types are the schema. Do not replace a known shape with field dictionaries or string bags.
- A media kind retains `TItem`, `TTarget`, and `TRelease` through `IMediaType<TItem,TTarget,TRelease>`.
- Catalogers and Curators are paired with the media-owned item type they return.
- Format capabilities own representation semantics and vocabularies. Video does not belong in Abstractions or Host.
- Provider modules register implementation types; Host activates admitted providers through DI.
- Do not add a second non-generic public media-type model, a universal `Evidence<T>` wrapper, a globally ordered evidence source, or executable objects to wire descriptors.
- Use Television, not only Movies, to pressure-test changes to typed media and release coverage.
- Do not add vendor-specific identifiers, routes, endpoints, or implementations to a media definition.
- Client references Abstractions only. Host references no format or media implementation.
- Media extensions may reference Abstractions and independently owned format capabilities, never Common, Plugins, Host, Api, or Client.
- No plugin supplies UI markup. Descriptors support discovery and generic presentation; they are not a substitute for typed domain assemblies.
- Warnings are errors. Run the complete solution build and tests and report skips.
- Use American English in source and documentation.

## Contract lifecycle

The current contract version is `0.8.0`; first-party manifests declare `>=0.8 <0.9`. Below 1.0, breaking public contract changes require a new minor identity and a synchronized `docs/contracts/stability.md` entry. Do not ship changed contracts under an older assembly version.

When a change alters public interfaces, domain ownership, dependency direction, lifecycle, or side effects, update `INTERFACE.md`, `HISTORY.md`, and `CONTEXT.md` atomically. Remove stale text rather than leaving competing descriptions.

## Current migration constraint

Movies is typed. Television, Music, and Books retain legacy imperative seams during conversion. `IQualityModel`, ladder DTOs, and the untyped parsed-quality string exist only as migration scaffolding. New features use typed releases, format-owned representations, `ReleasePolicy<TRelease>`, and `TargetMatch<TTarget>`; do not extend the legacy quality architecture.

The next pressure point is a generic typed release interpreter and the shared definition-assembly lifecycle required by separate provider plugins and the Blazor client. Do not conceal either gap with string projections or duplicate type loads.
