#!/usr/bin/env python3
"""Read-only G07B SQLite evidence verifier; it never creates, updates, or deletes a database."""
import argparse
import json
import re
import sqlite3
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--database', required=True)
parser.add_argument('--definition', type=int, required=True)
parser.add_argument('--phase', required=True, choices=['searched', 'added', 'retried', 'monitored', 'refreshed', 'restarted'])
parser.add_argument('--contract-manifest')
parser.add_argument('--previous')
parser.add_argument('--output', required=True)
arguments = parser.parse_args()
database, output = Path(arguments.database), Path(arguments.output)
if not database.is_file():
    raise SystemExit(f'error: database does not exist: {database}')
if arguments.previous and not Path(arguments.previous).is_file():
    raise SystemExit(f'error: prior phase evidence does not exist: {arguments.previous}')

connection = sqlite3.connect(f'file:{database}?mode=ro', uri=True)
connection.row_factory = sqlite3.Row

def rows(statement, *parameters):
    return [dict(row) for row in connection.execute(statement, parameters)]

required_tables = {'catalog_identity', 'catalog_allocation', 'catalog_record', 'library_entry', 'library_entry_monitor', 'provider_definition', 'provider_definition_setting'}
present_tables = {row['name'] for row in rows("select name from sqlite_master where type = 'table'")}
if missing_tables := sorted(required_tables - present_tables):
    raise SystemExit('error: expected tables absent: ' + ', '.join(missing_tables))

provider = rows('select * from provider_definition where Id = ?', arguments.definition)
settings_rows = rows('select FieldId, Value from provider_definition_setting where DefinitionId = ?', arguments.definition)
settings = {row['FieldId']: row['Value'] for row in settings_rows}
identity = rows("select * from catalog_identity where Kind = 'movies' and Scheme = 'proof' and Value = '42'")
allocation = rows("select * from catalog_allocation where Kind = 'movies'")
records = rows("select * from catalog_record where Kind = 'movies' and CatalogScheme = 'proof' and CatalogValue = '42'")
library = rows("select * from library_entry where Kind = 'movies'")
monitor = rows('select * from library_entry_monitor')
report = {'phase': arguments.phase, 'provider': provider, 'settings': settings_rows, 'identity': identity, 'allocation': allocation, 'records': records, 'library': library, 'monitor': monitor}

if len(provider) != 1 or provider[0]['Family'] != 3 or settings.get('revision') not in {'1', '2'}:
    raise SystemExit('error: proof provider/revision is not durable')
if len(identity) != 1 or len(allocation) != 1 or allocation[0]['Issued'] != 1:
    raise SystemExit('error: search must mint exactly one proof identity/allocation')
if identity[0]['Level'] != 'item' or identity[0]['Identity'] != 1:
    raise SystemExit('error: proof:42 did not receive the expected movies:item identity 1')

if arguments.phase == 'searched':
    if records or library or monitor:
        raise SystemExit('error: search materialized a catalog or user facet')
else:
    if len(records) != 1 or len(library) != 1:
        raise SystemExit('error: expected exactly one durable catalog record and library entry')
    if library[0]['Level'] != 'item' or library[0]['Identity'] != identity[0]['Identity'] or not library[0]['AddedAt']:
        raise SystemExit('error: library entry is not the one durable proof item with an AddedAt value')
    record = records[0]
    expected_revision = 1 if arguments.phase in {'added', 'retried', 'monitored'} else 2
    expected_title = 'Proof Movie Revision One' if expected_revision == 1 else 'Proof Movie Revision Two'
    expected_state = 0 if expected_revision == 1 else 1
    if (record['Kind'] != 'movies' or record['Level'] != identity[0]['Level'] or record['Identity'] != identity[0]['Identity'] or record['CatalogScheme'] != 'proof' or record['CatalogValue'] != '42' or record['Revision'] != expected_revision or record['Title'] != expected_title or record['CatalogState'] != expected_state):
        raise SystemExit('error: durable record key, identity, revision, title, or catalog state is wrong')
    metadata_hash = record['ContractMetadataHash']
    if not isinstance(metadata_hash, str) or not re.fullmatch(r'[0-9A-Fa-f]{64}', metadata_hash):
        raise SystemExit('error: stored metadata hash is not a nonempty SHA-256 hex value')
    try:
        payload = json.loads(bytes(record['Payload']).decode('utf-8'))
    except (TypeError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise SystemExit(f'error: stored typed Movie payload is not valid generated JSON: {error}') from error
    required_payload_fields = {'externalIds', 'title', 'catalogState', 'lifecycle', 'artwork', 'ratings', 'collections', 'overview', 'genres', 'keywords'}
    if missing_payload_fields := sorted(required_payload_fields - set(payload)):
        raise SystemExit('error: generated Movie payload lacks typed fields: ' + ', '.join(missing_payload_fields))
    values = payload['externalIds'].get('values', []) if isinstance(payload['externalIds'], dict) else []
    if not any(value.get('scheme') == 'proof' and value.get('value') == '42' for value in values if isinstance(value, dict)):
        raise SystemExit('error: generated Movie payload does not carry proof:42')
    expected_payload_state = {'active', 0} if expected_state == 0 else {'withdrawn', 1}
    if payload['title'] != expected_title or payload['catalogState'] not in expected_payload_state:
        raise SystemExit('error: typed Movie title or catalog state disagrees with the durable record')
    if not payload['artwork'].get('images') or len(payload['ratings']) < 2 or len(payload['collections']) < 1:
        raise SystemExit('error: typed Movie payload omitted representative artwork, ratings, or collections')
    report['typedPayload'] = payload
    if arguments.contract_manifest:
        manifest = json.loads(Path(arguments.contract_manifest).read_text())
        movies = next(package for package in manifest['packages'] if package['id'] == 'movies')
        declared_hash = movies['assemblies'][0]['declarations'][0]['generatedMetadataHash']
        if metadata_hash != declared_hash:
            raise SystemExit('error: stored metadata hash differs from the captured Movies contract hash')
        report['generatedMetadataHash'] = declared_hash

if arguments.phase == 'monitored' and (len(monitor) != 1 or monitor[0]['Dimension'] != 'wanted' or monitor[0]['Choice'] != 'false'):
    raise SystemExit('error: visible Wanted monitor action did not persist exactly wanted=false')
if arguments.phase in {'added', 'retried'} and monitor:
    raise SystemExit('error: add/retry unexpectedly changed the monitor facet')
if arguments.previous:
    previous = json.loads(Path(arguments.previous).read_text())
    if arguments.phase == 'retried' and (library != previous['library'] or monitor != previous['monitor'] or records != previous['records']):
        raise SystemExit('error: idempotent API retry changed a durable catalog or user facet')
    if arguments.phase == 'monitored' and (library != previous['library'] or records != previous['records']):
        raise SystemExit('error: monitoring changed library presence/AddedAt or provider-owned catalog facts')
    if arguments.phase in {'refreshed', 'restarted'} and (library != previous['library'] or monitor != previous['monitor']):
        raise SystemExit('error: refresh/restart changed a user-owned library or monitor facet')
    if arguments.phase == 'restarted' and records != previous['records']:
        raise SystemExit('error: restart changed durable catalog facts')

output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, indent=2, default=str) + '\n')
