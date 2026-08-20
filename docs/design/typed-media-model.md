# Typed media model

> **Superseded on 2026-08-20.** The former fluent-builder and capability-interface proposal is retained
> in Git history only. It is not current architecture.

The implemented model is described by [the typed-media north star](typed-media-north-star.md), with the
current operational state in [`CONTEXT.md`](../../CONTEXT.md) and the public boundary in
[`INTERFACE.md`](../../INTERFACE.md).

The binding rules are:

- one ordinary `MediaType<TItem,TTarget,TRelease,TParser>` definition object;
- typed virtual/abstract override values rather than `Configure(builder)`;
- one static `IReleaseParser<TRelease>` implementation bound as generic arity rather than a parse DSL;
- no `IUses...` capability badges or reflection over implemented interfaces;
- `MediaItem<TReleaseTimeline,TReleaseStage>` or its exact-item three-arity form, with nominal media-owned
  closures or subclasses only when they provide stable domain identity or add real facts;
- format-owned representations and vocabulary;
- typed cataloger and curator pairs over the exact item class;
- catalog-owned external identifier marker recognition;
- one-way Host compilation into kind-blind discovery and wire projections.
