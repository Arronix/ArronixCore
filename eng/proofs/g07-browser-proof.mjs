#!/usr/bin/env node

// G07 browser half — the checks a real browser has to make, driven rather than read by hand.
//
// The client this drives is the one an ordinary `dotnet publish` produces, served by the real API from one
// origin. Everything asserted here is read out of the rendered document: the page states its own load
// report and its own projection, so a harness reading them is reading exactly what a person on the page
// sees. There is no script-interop surface to read instead, deliberately — one that could disagree with
// the rendered page would be proving itself.
//
//   node eng/proofs/g07-browser-proof.mjs --address http://127.0.0.1:5223 \
//        --evidence artifacts/g07/evidence --fixture eng/proofs/fixtures/g07/movie.json
//
// Needs Playwright's Chromium. This repository's hermetic rail carries no browser toolchain, so this is
// run beside it rather than inside it, and its output is written to the evidence directory as JSON.

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';

const options = parse(process.argv.slice(2));
const address = options.address ?? 'http://127.0.0.1:5223';
const evidence = options.evidence ?? 'artifacts/g07/evidence';
const fixturePath = options.fixture ?? 'eng/proofs/fixtures/g07/movie.json';

let chromium;

try {
    ({ chromium } = await import('playwright'));
} catch {
    console.error(
        "error: playwright is not installed. Install it beside this repository (npm i -D playwright && "
        + "npx playwright install chromium) and re-run, or drive the identifiers by hand.");
    process.exit(2);
}

const fixture = readFileSync(fixturePath);
const fixtureHash = createHash('sha256').update(fixture).digest('hex');

const results = [];
const check = (description, actual, expected) => {
    const ok = JSON.stringify(actual) === JSON.stringify(expected);
    results.push({ description, ok, actual, expected });
    console.log(`${ok ? 'ok   ' : 'FAIL '} ${description}`);
    if (!ok) {
        console.log(`      expected: ${JSON.stringify(expected)}`);
        console.log(`      actual:   ${JSON.stringify(actual)}`);
    }
};

const browser = await chromium.launch();
const context = await browser.newContext();
const page = await context.newPage();

// Every byte the page fetched, so "loaded over serialized network payloads" is a measurement.
const requested = [];
page.on('request', request => requested.push(request.url()));

const consoleErrors = [];
page.on('pageerror', error => consoleErrors.push(String(error)));

try {
    await page.goto(`${address}/contracts`, { waitUntil: 'networkidle' });
    await page.waitForSelector('#contract-compatibility', { timeout: 60_000 });

    // --- G07.1: the installation, unchanged ---
    check('the installation is compatible', await text(page, '#contract-compatibility'), 'Compatible');
    check('a projection is permitted', await text(page, '#contract-can-project'), 'projection permitted');

    const load = JSON.parse(await page.locator('#contract-proof').textContent());
    const assemblies = load.packages.flatMap(entry => entry.assemblies);

    check(
        'every required assembly is resident',
        assemblies.every(entry => entry.outcome === 2 || entry.outcome === 3),
        true);
    check(
        'the movies contract was fetched over the network',
        requested.some(url => url.includes('/api/v1/client-contracts/movies/')),
        true);
    check(
        'the client build carries no media assembly of its own',
        requested.some(url => /_framework\/Arronix\.(Media|Format|Plugin)\./.test(url)),
        false);

    // --- G07.2: one serialized entity, read through the contract that was admitted ---
    check('the payload address is a path on this host', await value(page, '#contract-payload'), 'fixtures/g07/movie.json');

    await page.click('#project-contract-payload');
    await page.waitForFunction(
        () => document.querySelector('#projection-status')?.textContent?.trim() !== 'NotAttempted',
        null,
        { timeout: 60_000 });

    check('the payload projected', await text(page, '#projection-status'), 'Projected');
    check(
        'it was read into the contract\'s own entity type',
        await text(page, '#projected-entity-type'),
        'Arronix.Media.Movies.Movie');

    const payloadRequest = requested.find(url => url.endsWith('/fixtures/g07/movie.json'));
    check('the payload arrived as a serialized network payload', Boolean(payloadRequest), true);

    const served = await (await fetch(`${address}/fixtures/g07/movie.json`)).arrayBuffer();
    check(
        'the bytes the browser read are the bytes the contract wrote',
        createHash('sha256').update(Buffer.from(served)).digest('hex'),
        fixtureHash);

    const proof = JSON.parse(await page.locator('#projection-proof').textContent());
    check('the payload length the page reports is the fixture', proof.payloadLength, fixture.length);
    check('the projection carries every declared field', proof.fieldCount, proof.fields.length);

    // --- the five values the gate names, read from the document rather than from the proof ---
    for (const fieldId of ['artwork', 'ratings', 'lifecycle', 'status', 'collections']) {
        const field = page.locator(`[data-field-id="${fieldId}"]`);
        check(`the document carries one '${fieldId}' field`, await field.count(), 1);
        check(`'${fieldId}' is present`, await field.getAttribute('data-absent'), 'false');
    }

    check('status renders its declared choice', (await text(page, '[data-field-id="status"]')).includes('Released'), true);
    check('ratings carries both ratings', await page.locator('[data-field-id="ratings"]').getAttribute('data-item-count'), '2');
    check('collections carries its collection', await page.locator('[data-field-id="collections"]').getAttribute('data-item-count'), '1');

    // Artwork stays typed all the way to the document: role and both measurements, not a URL string.
    const poster = page.locator('[data-field-id="artwork"] img').first();
    check('the poster is rendered as an image', await poster.count(), 1);
    check('the poster keeps its role', await poster.getAttribute('data-artwork-role'), 'poster');
    check('the poster keeps its width', await poster.getAttribute('data-artwork-width'), '8');
    check('the poster keeps its height', await poster.getAttribute('data-artwork-height'), '12');
    check(
        'the poster is inline, so this proof fetches no image',
        (await poster.getAttribute('src')).startsWith('data:image/png;base64,'),
        true);

    check('nothing threw in the page', consoleErrors, []);

    // --- a payload this host does not serve fails visibly, with nothing projected ---
    await page.fill('#contract-payload', 'fixtures/g07/absent.json');
    await page.click('#project-contract-payload');
    await page.waitForFunction(
        () => document.querySelector('#projection-status')?.textContent?.trim() !== 'Projected',
        null,
        { timeout: 60_000 });

    check('a payload this host does not serve is unavailable', await text(page, '#projection-status'), 'Unavailable');
    check('nothing is projected from it', await page.locator('#projected-fields').count(), 0);
    check('and it says why', (await text(page, '#projection-failure')).length > 0, true);

    // --- an address off this origin is refused without being fetched ---
    const before = requested.length;
    await page.fill('#contract-payload', 'https://evil.test/movie.json');
    await page.click('#project-contract-payload');
    await page.waitForFunction(
        () => document.querySelector('#projection-status')?.textContent?.trim() === 'AddressUnsafe',
        null,
        { timeout: 60_000 });

    check('an address off this origin is refused', await text(page, '#projection-status'), 'AddressUnsafe');
    check(
        'and nothing was fetched for it',
        requested.slice(before).some(url => url.includes('evil.test')),
        false);
} finally {
    mkdirSync(evidence, { recursive: true });
    writeFileSync(
        join(evidence, 'browser-half.json'),
        JSON.stringify({ address, fixtureHash, results, requested, consoleErrors }, null, 2));
    await browser.close();
}

const failed = results.filter(entry => !entry.ok);
console.log(`\n${results.length - failed.length} of ${results.length} browser checks passed.`);
console.log(`Evidence: ${join(evidence, 'browser-half.json')}`);
process.exit(failed.length === 0 ? 0 : 1);

async function text(target, selector) {
    return (await target.locator(selector).textContent()).trim();
}

async function value(target, selector) {
    return (await target.locator(selector).inputValue()).trim();
}

function parse(argv) {
    const parsed = {};
    for (let index = 0; index < argv.length; index += 2) {
        parsed[argv[index].replace(/^--/, '')] = argv[index + 1];
    }
    return parsed;
}
