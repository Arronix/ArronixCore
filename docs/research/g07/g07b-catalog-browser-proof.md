# G07B catalog browser proof

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

The integration dependency is explicit and satisfied. Provider discovery publishes the admitted cataloger's
nullable singular `pairedMediaKind` and `catalogScheme`; kind filtering compares that semantic pairing rather
than extension ownership, so an independently contributed cataloger remains discoverable. The harness derives
the provider and scheme from ordinary discovery, requires exactly one selected entry with
`pairedMediaKind=movies` and `catalogScheme=proof`, and refuses rather than hard-coding either value. Client
offers only configured, enabled, active schemes. The harness configures revision 1 and 2 through ordinary
POST/PUT routes and drives `/kinds/movies/catalog`
through the agreed `catalog-*` test ids and `search`, `add`, `refresh`, and `open-item` actions. First add
is visible and waits for the row's `Added to library.` status plus its Open and Refresh controls before
recording its full durable reference; search and add prove the revision-one title, while the refresh phase's
fresh catalog query proves revision two before the durable refresh. Each query also proves inline typed
artwork is visibly rendered. Retry idempotence stays separately labelled API evidence: it must be HTTP 200
and return exactly that reference, rather than requiring a redundant second Add button. Visible refresh
returns to the catalog row, records `Catalog facts refreshed.` and revision-two title; restart records the
ordinary item page's revision-two title and `Not wanted` state. CatalogState=Withdrawn remains a mandatory
store/API assertion, not a claimed UI surface. The visible native `Wanted` checkbox is unchecked to produce
`wanted=false`; that facet must survive the provider refresh and server restart. The browser driver does not
turn a page-context fetch or the contracts diagnostics into substitute catalog evidence.

Independent review accepted G07B at `e61fec49766e108d2410b37a1338cfe8cbb3f695`. The full .NET 11 rail
reported 3,940 total / 3,638 enabled and passed / 0 failed / 302 skipped. The retained
`g07b-proof-summary.json` reported 46 browser checks and required provider-discovery, browser/store and
API-retry evidence to pass. The same detached acceptance run reproduced G07A's 37 checks and G07.3's 30
server + 43 G07.2 browser + 64 lifecycle = 137 checks separately. G07B's 46, G07A's 37 and G07.3's 137 are
never combined into one proof count.
