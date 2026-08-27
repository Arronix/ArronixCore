#!/usr/bin/env node

// G07 / G07A browser half — the checks a real browser has to make, driven rather than read by hand.
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
// G07A drives the same mechanics — the same panel, the same generic ContractPayloadLoader — against a
// different installation and a different contract, so it is a second mode rather than a second script:
//
//   node eng/proofs/g07-browser-proof.mjs --mode g07a --address http://127.0.0.1:5224 \
//        --evidence artifacts/g07a/evidence --fixture eng/proofs/fixtures/g07a/shortfilm.json \
//        --package northmark.shorts --entity-type Northmark.Shorts.ShortFilm
//
// Needs Playwright's Chromium. This repository's hermetic rail carries no browser toolchain, so this is
// run beside it rather than inside it, and its output is written to the evidence directory as JSON.

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';

const options = parse(process.argv.slice(2));
const mode = options.mode ?? 'g07';
const address = options.address ?? 'http://127.0.0.1:5223';
const evidence = options.evidence ?? 'artifacts/g07/evidence';
const fixturePath = options.fixture ?? 'eng/proofs/fixtures/g07/movie.json';
const packageId = options.package ?? 'movies';
const entityType = options['entity-type'] ?? 'Arronix.Media.Movies.Movie';

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

// Both channels. An unhandled exception reaches `pageerror`; a `console.error` the application wrote
// itself does not, and "nothing threw in the page" would quietly miss it.
const consoleErrors = [];
page.on('pageerror', error => consoleErrors.push(`pageerror: ${error}`));
page.on('console', message => {
    if (message.type() === 'error') {
        consoleErrors.push(`console.error: ${message.text()}`);
    }
});

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
        `the ${packageId} contract was fetched over the network`,
        requested.some(url => url.includes(`/api/v1/client-contracts/${packageId}/`)),
        true);
    check(
        'the client build carries no media assembly of its own',
        requested.some(url => /_framework\/(Arronix\.(Media|Format|Plugin)|Northmark)\./.test(url)),
        false);

    // --- G07.2: one serialized entity, read through the contract that was admitted ---
    // The client carries no payload address of its own: it renders whichever contract a host admitted, and
    // a default would be one installation's fixture compiled into every deployment. The proof supplies it.
    check('the client carries no payload address of its own', await value(page, '#contract-payload'), '');

    const fixtureAddress = `fixtures/${fixturePath.split('/').slice(-2).join('/')}`;
    await page.fill('#contract-payload', fixtureAddress);

    // The response the browser actually handed the contract, captured around the click. Fetching the file
    // again from Node would hash a second request, which says nothing about what this page read.
    const payloadResponse = page.waitForResponse(
        response => response.url().endsWith(`/${fixtureAddress}`),
        { timeout: 60_000 });

    await page.click('#project-contract-payload');
    await page.waitForFunction(
        () => document.querySelector('#projection-status')?.textContent?.trim() !== 'NotAttempted',
        null,
        { timeout: 60_000 });

    check('the payload projected', await text(page, '#projection-status'), 'Projected');
    check('it was read into the contract\'s own entity type', await text(page, '#projected-entity-type'), entityType);

    const payloadRequest = requested.find(url => url.endsWith(`/${fixtureAddress}`));
    check('the payload arrived as a serialized network payload', Boolean(payloadRequest), true);

    const served = await payloadResponse;
    check('and the host served it', served.status(), 200);
    check(
        'the bytes the browser read are the bytes the contract wrote',
        createHash('sha256').update(await served.body()).digest('hex'),
        fixtureHash);

    const proof = JSON.parse(await page.locator('#projection-proof').textContent());
    check('the payload length the page reports is the fixture', proof.payloadLength, fixture.length);
    check('the projection carries every declared field', proof.fieldCount, proof.fields.length);

    if (mode === 'g07a') {
        // --- the external item's own fields, in the unchanged generic panel ---
        for (const fieldId of ['artwork', 'premiere', 'lifecycle', 'status']) {
            const field = page.locator(`[data-field-id="${fieldId}"]`);
            check(`the document carries one '${fieldId}' field`, await field.count(), 1);
            check(`'${fieldId}' is present`, await field.getAttribute('data-absent'), 'false');
        }

        // The premiere/date value the gate names: the lifecycle composite's own 'premiered' part, drawn
        // under its own component id rather than flattened into the parent field's text.
        const premieredPart = page.locator('[data-field-id="lifecycle"] [data-component-id="premiered"]');
        check('the lifecycle composite carries its own premiered part', await premieredPart.count(), 1);
        check(
            'the premiered date is rendered, not absent',
            (await text(page, '[data-field-id="lifecycle"] [data-component-id="premiered"]')).length > 0,
            true);

        check(
            'status renders the stage the premiere date puts this film in',
            (await text(page, '[data-field-id="status"]')).toLowerCase().includes('festival'),
            true);

        // The festival premiere itself: a composite of the external item's own two fields, named by the
        // external domain's own component ids — this client has never heard of "festival" or "edition".
        const premiereLabels = await page.locator('[data-field-id="premiere"] [data-component-id]').evaluateAll(
            parts => parts.map(part => part.dataset.componentId));
        check(
            'the premiere composite\'s own parts are drawn under their own components',
            ['festival', 'edition'].filter(id => !premiereLabels.includes(id)),
            []);

        const premiereText = await text(page, '[data-field-id="premiere"]');
        check('the premiere carries the festival this fixture named', premiereText.includes('Sundance'), true);
        // An integer field is grouped for display (locale-dependent thousands separators), so this compares
        // digits only rather than pinning one culture's punctuation.
        check(
            'and the edition year it named',
            premiereText.replace(/\D/g, '').includes('2024'),
            true);

        // Artwork stays typed all the way to the document, exactly as it does for the first-party contract:
        // role and both measurements, not a URL string, decoded from bytes this proof fetches nothing for.
        const poster = page.locator('[data-field-id="artwork"] img').first();
        check('the poster is rendered as an image', await poster.count(), 1);
        check('the poster keeps its role', await poster.getAttribute('data-artwork-role'), 'poster');
        check(
            'the poster is inline, so this proof fetches no image',
            (await poster.getAttribute('src')).startsWith('data:image/png;base64,'),
            true);

        await poster.scrollIntoViewIfNeeded();
        const decoded = await poster.evaluate(
            (image, budget) => image.complete && image.naturalWidth > 0
                ? true
                : new Promise(resolve => {
                    const settle = () => resolve(image.complete && image.naturalWidth > 0);
                    image.addEventListener('load', settle, { once: true });
                    image.addEventListener('error', () => resolve(false), { once: true });
                    setTimeout(() => resolve(false), budget);
                }),
            15_000);

        check('the poster decoded as a real image', decoded, true);
    } else {
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

        // The attributes only say what the document claims. This says the browser decoded the bytes the
        // contract carried, which is the whole point of holding an inline image to its own signature.
        //
        // The image is lazily loaded, so offscreen it stays incomplete indefinitely: it is scrolled to
        // first, and the wait is bounded so a poster that never decodes fails this proof rather than
        // stalling it.
        await poster.scrollIntoViewIfNeeded();
        const decoded = await poster.evaluate(
            (image, budget) => image.complete && image.naturalWidth > 0
                ? true
                : new Promise(resolve => {
                    const settle = () => resolve(image.complete && image.naturalWidth > 0);
                    image.addEventListener('load', settle, { once: true });
                    image.addEventListener('error', () => resolve(false), { once: true });
                    setTimeout(() => resolve(false), budget);
                }),
            15_000);

        check('the poster decoded as a real image', decoded, true);
        check(
            'and it decoded at the size it states',
            await poster.evaluate(image => [image.naturalWidth, image.naturalHeight]),
            [8, 12]);

        // A composite is a tuple of values, and its parts say which is which — from the components the
        // contract declared, so a kind this client has never heard of is labeled by what it says it is.
        const labels = await page.locator('[data-field-id="ratings"] [data-component-id]').evaluateAll(
            parts => parts.map(part => part.dataset.componentId));
        check(
            'a composite\'s parts are drawn under their own components',
            ['source', 'value', 'scale', 'voice', 'sampleSize'].filter(id => !labels.includes(id)),
            []);

        // Nested too: a rating's scale is a composite inside a composite, and its parts are its own.
        check(
            'and so are the parts of a composite inside one',
            ['minimum', 'maximum'].filter(id => !labels.includes(id)),
            []);

        const ratings = await text(page, '[data-field-id="ratings"]');
        check(
            'each part carries its component\'s declared name, separated from its value',
            ratings.includes('Voice: Audience') && ratings.includes('Source: tmdb'),
            true);
        check('and no label runs into the value beside it', /[A-Za-z]:[^\s]/.test(ratings), false);
    }

    check('nothing threw in the page', consoleErrors, []);

    // --- a payload this host does not serve fails visibly, with nothing projected ---
    await page.fill('#contract-payload', fixtureAddress.replace(/[^/]+$/, 'absent.json'));
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
