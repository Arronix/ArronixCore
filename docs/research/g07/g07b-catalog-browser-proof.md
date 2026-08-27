# G07B browser-proof foundation

G07B owns a separate browser-proof identity. It builds a proof-only `ICataloger<Movie>` package against
packed `Arronix.Abstractions` and `Arronix.Media.Movies`, never against the Movies entry assembly, Host,
Client, or source projects. The cataloger owns `proof:42`; provider definition `revision` 1 and 2 produce
different deterministic catalog-owned Movie values with the same identity. It has no static or call-count
state and its changed-since response is deterministically empty.

`g07b-catalog-browser-proof.sh` isolates its package root, state, client publication, SQLite file and
evidence under `artifacts/g07b-browser-proof`; it starts only a loopback API process it owns and performs
bounded verified-PID termination. `g07b-read-store.py` is read-only and asserts the search distinction:
search may mint the one identity and allocation, but may not materialize a catalog record, library entry,
or monitor row. Later phases assert add/retry, monitoring, refresh and restart without drift.

The integration dependency is intentionally explicit. Provider discovery currently filters `kind` to the
same plugin that owns the media kind and `ProviderCatalogEntry` does not publish a catalog scheme. An
independently contributed cataloger therefore cannot be discovered/configured from the ordinary provider
surface. The harness captures both provider responses and exits with that dependency instead of hard-coding
a provider identifier or scheme. The planned projection supplies nullable `pairedMediaKind` and
`catalogScheme`; Client offers only active configured schemes. Once that contract is corrected, the same
harness configures revision 1 and 2 through ordinary POST/PUT routes and drives the ordinary Client only
through visible `catalog-*` and `data-action` interactions. First add is visible; retry idempotence stays
separately labelled API evidence, rather than requiring a redundant second Add button. The browser driver
does not turn a page-context fetch or the contracts diagnostics into substitute catalog evidence.

G07.3's 137 checks and G07A's 37 checks remain separate accepted proof identities. This foundation does not
close G07B or claim its combined browser acceptance.
