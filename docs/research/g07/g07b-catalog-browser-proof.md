# G07B browser-proof foundation

G07B owns a separate browser-proof identity. It builds a proof-only `ICataloger<Movie>` package against
packed `Arronix.Abstractions` and `Arronix.Media.Movies`, never against the Movies entry assembly, Host,
Client, or source projects. The cataloger owns `proof:42`; provider definition `revision` 1 and 2 produce
different deterministic catalog-owned Movie values with the same identity: title, lifecycle and catalog
state change from Active to addressable Withdrawn, while the payload remains rich enough to exercise
artwork, ratings and collections. It has no static or call-count state and its changed-since response is
deterministically empty.

`g07b-catalog-browser-proof.sh` isolates its package root, state, client publication, SQLite file and
evidence under `artifacts/g07b-browser-proof`; provider build output/intermediates also stay there, so it
never cleans a source-tree `bin` or `obj`. It starts only a loopback API process it owns, uses the API
project content root, and performs bounded verified-PID termination with port-release verification.
`g07b-read-store.py` is read-only and asserts the search distinction: search may mint the primary Movie
identity and its referenced typed collection identity (with the corresponding allocation), but may not
materialize a catalog record, library entry, or monitor row. Later snapshots are taken only after their
actual phase and compare both identities/allocation, AddedAt, and user-monitor stability across retry,
refresh and restart.

The integration dependency is intentionally explicit. Provider discovery currently filters `kind` to the
same plugin that owns the media kind and `ProviderCatalogEntry` does not publish a catalog scheme. An
independently contributed cataloger therefore cannot be discovered/configured from the ordinary provider
surface. The harness captures both provider responses and exits with that dependency instead of hard-coding
a provider identifier or scheme. On the corrected projection it requires exactly one selected entry with
`pairedMediaKind=movies` and `catalogScheme=proof`, and records `dependency satisfied` rather than retaining
a blocker. The planned projection supplies nullable `pairedMediaKind` and
`catalogScheme`; Client offers only active configured schemes. Once that contract is corrected, the same
harness configures revision 1 and 2 through ordinary POST/PUT routes and drives `/kinds/movies/catalog`
through the agreed `catalog-*` test ids and `search`, `add`, `refresh`, and `open-item` actions. First add
is visible and waits for the row's `Added to library.` status plus its Open and Refresh controls before
recording its full durable reference; every search also proves the revision-one title and inline typed
artwork are visibly rendered. Retry idempotence stays separately labelled API evidence: it must be HTTP 200
and return exactly that reference, rather than requiring a redundant second Add button. Visible refresh
returns to the catalog row, records `Catalog facts refreshed.` and revision-two title; restart records the
ordinary item page's revision-two title and `Not wanted` state. CatalogState=Withdrawn remains a mandatory
store/API assertion, not a claimed UI surface. The visible native `Wanted` checkbox is unchecked to produce
`wanted=false`; that facet must survive the provider refresh and server restart. The browser driver does not
turn a page-context fetch or the contracts diagnostics into substitute catalog evidence.

G07.3's 137 checks and G07A's 37 checks remain separate accepted proof identities. This foundation does not
close G07B or claim its combined browser acceptance. A future successful full G07B run emits its own
`g07b-proof-summary.json`: it derives the exact browser-check count from all visible phase reports and
requires provider-discovery, browser/store, and API-retry evidence to pass; it never combines that count
with G07.3 or G07A.
