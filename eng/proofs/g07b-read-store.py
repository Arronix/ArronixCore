#!/usr/bin/env python3
"""Read-only G07B SQLite evidence verifier; it never creates, updates, or deletes a database."""
import argparse, json, sqlite3
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--database', required=True)
p.add_argument('--definition', type=int, required=True)
p.add_argument('--phase', required=True, choices=['searched', 'added', 'retried', 'monitored', 'refreshed', 'restarted'])
p.add_argument('--contract-manifest')
p.add_argument('--output', required=True)
a = p.parse_args()
db, output = Path(a.database), Path(a.output)
if not db.is_file(): raise SystemExit(f'error: database does not exist: {db}')
con = sqlite3.connect(f'file:{db}?mode=ro', uri=True)
con.row_factory = sqlite3.Row
def rows(sql, *args): return [dict(r) for r in con.execute(sql, args)]
required = ['catalog_identity','catalog_allocation','catalog_record','library_entry','library_entry_monitor','provider_definition','provider_definition_setting']
present = {r['name'] for r in rows("select name from sqlite_master where type = 'table'")}
missing = sorted(set(required) - present)
if missing: raise SystemExit('error: expected tables absent: ' + ', '.join(missing))
provider = rows('select * from provider_definition where Id = ?', a.definition)
settings = rows('select FieldId, Value from provider_definition_setting where DefinitionId = ?', a.definition)
identity = rows("select * from catalog_identity where Kind = 'movies' and Scheme = 'proof' and Value = '42'")
allocation = rows("select * from catalog_allocation where Kind = 'movies'")
records = rows("select * from catalog_record where Kind = 'movies' and CatalogScheme = 'proof' and CatalogValue = '42'")
library = rows("select * from library_entry where Kind = 'movies'")
monitor = rows('select * from library_entry_monitor')
report = {'phase': a.phase, 'provider': provider, 'settings': settings, 'identity': identity, 'allocation': allocation, 'records': records, 'library': library, 'monitor': monitor}
if len(provider) != 1 or dict(settings).get('revision') not in {'1','2'}: raise SystemExit('error: proof provider/revision is not durable')
if len(identity) != 1 or len(allocation) != 1: raise SystemExit('error: search must mint exactly one proof identity/allocation')
if a.phase == 'searched' and (records or library or monitor): raise SystemExit('error: search materialized a catalog or user facet')
if a.phase != 'searched' and (len(records) != 1 or len(library) != 1): raise SystemExit('error: expected one durable catalog record and library entry')
if records:
    record = records[0]
    payload = json.loads(bytes(record['Payload']).decode('utf-8'))
    if record['Title'] != payload.get('title') or payload.get('externalIds', {}).get('values', [{}])[0].get('scheme') != 'proof':
        raise SystemExit('error: record indexes or typed JSON do not describe proof:42')
    report['typedPayload'] = payload
    if a.contract_manifest:
        manifest = json.loads(Path(a.contract_manifest).read_text())
        movies = next(package for package in manifest['packages'] if package['id'] == 'movies')
        declared = movies['assemblies'][0]['declarations'][0]['generatedMetadataHash']
        if record['ContractMetadataHash'] != declared:
            raise SystemExit('error: stored metadata hash differs from the captured Movies contract hash')
        report['generatedMetadataHash'] = declared
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, indent=2, default=str) + '\n')
