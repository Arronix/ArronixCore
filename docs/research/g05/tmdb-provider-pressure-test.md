# G05 — independent TMDb cataloger and curator

**Status:** complete.

G05 proves that a real vendor package can bind to an independently installed media domain, return its exact
typed item, and participate in Host materialization without moving vendor concepts into Movies or the
platform. It is the first production provider package, not a claim of complete TMDb or Movies coverage.

## Package boundary

`Arronix.Provider.Tmdb` ships independently as package `tmdb` and declares one dependency on `movies`
`>=0.1 <0.2`. Its compiled Arronix references are exactly:

- `Arronix.Abstractions`;
- `Arronix.Media.Movies`.

It does not reference `Arronix.Plugin.Movies`, Host, Plugins, Client, Video, or Host DI. The package owns its
settings, transport, DTOs, identifier grammar, embedded marker spelling, cataloger, curator, and TMDb-to-Movie
mapping. Platform, format, and media projects contain no TMDb vocabulary.

The manifest requests `metadata` and `curation`. Network access is implied by those capabilities and is not
restated as a third declaration.

## Typed provider contract

`TmdbMovieCataloger : ICataloger<Movie>` returns exact `Movie` values:

- search maps the facts present in TMDb summary rows;
- direct fetch maps the full details response;
- every result carries exactly one `tmdb` external identifier;
- a fetched result may also carry TMDb's IMDb cross-reference;
- no provider result contains or invents a `MediaItemId`.

`TmdbMovieCurator : ICurator<Movie>` returns `CuratedReference` values in the `tmdb` catalog scheme. It does
not return Movies and does not use its optional curator-entry identity as catalog identity.

The installed path is therefore:

```text
TMDb curator
  -> CuratedReference(tmdb:603)
  -> TMDb cataloger
  -> Movie
  -> Host-assigned MediaItemId
```

The cataloger is authoritative for the item and its TMDb identity. The curator only points to it. Host alone
assigns and reconciles durable local identity, as established in
`docs/research/g04/media-item-identity.md`.

## Identifier recognition

The provider owns scheme `tmdb` and recognizes `{tmdb-<id>}` locally. A canonical id is a positive unsigned
decimal `Int32` with no leading zero or surrounding text. Marker recognition and direct lookup use the same
grammar and never make a network request.

The installed Movies parser consumes the resulting `ExternalIdReading`; it contains no TMDb regular expression
or identifier declaration of its own.

## Transport and mapping

All requests use the capability-scoped `IHttpGateway`. The Read Access Token is sent only as an
`Authorization: Bearer` header. Endpoint overrides must be absolute HTTP(S) URLs with a host and no user info,
query, or fragment. Release region is a normalized two-letter country code. Artwork paths cannot replace the
configured scheme, authority, port, or base path, and item homepages are retained only for HTTP(S).

The mapper preserves absence rather than filling it with guesses:

- regional lifecycle dates come only from the configured country's release-date rows;
- the top-level release date supplies `Year`, not a regional cinema date;
- certification remains absent because TMDb is not the issuing authority;
- original language remains absent until a language resolver can produce the complete domain value.

Search and discovery currently read the first page. The popular list has no curator-entry identifiers because
TMDb supplies catalog items, not a separate list-entry identity.

## Installed-package proof

`PackagedTmdbProviderTests` publishes and installs Video, Movies, and TMDb as separate package directories and
sends them through the real discovery, dependency, shared-contract, admission, activation, and stop path. It
proves that:

- the cataloger and curator activate against the exact `Movie` type published by the installed Movies package;
- the provider load context does not contain a private copy of `Arronix.Media.Movies`;
- a real provider call over a fake platform gateway maps TMDb JSON into that installed `Movie` type;
- repeated catalog fetches resolve to the same Host reference;
- a curated reference resolves through the cataloger to that same reference;
- `{tmdb-603}` reaches the installed Movie parser as `parse.externalId.tmdb`;
- the scoped gateway applies the Bearer token, plugin user agent, and provider/host rate-limit partition;
- missing or incompatible Movies quarantines only TMDb and publishes no provider registrations.

The fake gateway replaces the external network only. The package, DTO parsing, mapper, cataloger, curator,
plugin context, loader, dispatcher, identity assignment, and parser path are production implementations.

## Architecture and focused evidence

Architecture tests assert the source dependency graph, compiled assembly references, and absence of TMDb
vocabulary outside the provider package. Focused proof on .NET 11 Preview 7 currently reports:

- `Arronix.Provider.Tmdb.Tests`: 132 passed, 0 failed;
- packaged TMDb integration: 3 passed, 0 failed;
- full architecture suite: 358 passed, 1 registered skip, 0 failed;
- full solution rail: 2,749 passed, 302 registered skips, 0 failed, and 0 inconclusive from 3,051
  cases across 12 test projects.

The full rail also passed locked restore, the warnings-as-errors build, compiler-input binding, required-test
sentinels, and the compatibility-ledger ratchet.

## Deliberate limits

G05 does not add durable catalog persistence, live credentialed service verification, pagination beyond the
first search/discovery page, certification-authority resolution, or language-code resolution. Persistence is
G07B; broader provider completeness and credentialed operational coverage are G30. None of those limits puts
vendor identity, fields, or transport back into Movies or Host.
