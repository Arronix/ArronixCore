#!/usr/bin/env node

// G07B's separate browser-proof identity. It drives rendered ordinary-Client controls only: no
// page-context fetch, Client interop seam, or test-only route substitutes for visible catalog evidence.

import { mkdirSync } from 'node:fs';
import { closeWindow, openPlaywright, parse, recorder, report, sink, watch, within, writeEvidence } from './g07-browser-support.mjs';

const options = parse(process.argv.slice(2));
const address = options.address ?? 'http://127.0.0.1:5225';
const evidence = options.evidence ?? 'artifacts/g07b-browser-proof/evidence';
const phase = options.phase ?? 'search';
const suppliedItemRef = options['item-ref'];
const phaseNames = new Set(['search', 'add', 'monitor', 'refresh', 'restart']);
if (!phaseNames.has(phase)) throw new Error(`Unknown G07B browser phase '${phase}'.`);

const expected = {
    catalogId: 'proof:42',
    initialTitle: 'Proof Movie Revision One',
    refreshedTitle: 'Proof Movie Revision Two',
    added: 'Added to library.',
    inLibrary: 'In library.',
    refreshed: 'Catalog facts refreshed.',
    notWanted: 'Not wanted',
};
const catalog = {
    route: '/kinds/movies/catalog',
    form: '[data-testid="catalog-search-form"]',
    scheme: '[data-testid="catalog-scheme"]',
    query: '[data-testid="catalog-query"]',
    id: '[data-testid="catalog-id"]',
    results: '[data-testid="catalog-results"]',
    result: '[data-testid="catalog-result"]',
    search: '[data-action="search"]',
    add: '[data-action="add"]',
    refresh: '[data-action="refresh"]',
    openItem: '[data-action="open-item"]',
    catalogId: 'data-catalog-id',
    itemRef: 'data-item-ref',
    wantedRole: 'checkbox',
    wantedName: 'Wanted',
};

const { chromium } = await openPlaywright();
const { results, check } = recorder();
const observed = sink();
let browser;
let context;
let closing = false;
let signal = null;
let itemRef = suppliedItemRef ?? null;
mkdirSync(evidence, { recursive: true });

const closeBrowser = async () => {
    if (closing) return;
    closing = true;
    await within(5000, 'G07B browser context close', () => context?.close());
    await within(5000, 'G07B browser close', () => browser?.close());
};

const signalExit = received => {
    signal ??= received;
    void closeBrowser().finally(() => process.exit(128));
};
process.once('SIGINT', () => signalExit('SIGINT'));
process.once('SIGTERM', () => signalExit('SIGTERM'));
process.once('SIGHUP', () => signalExit('SIGHUP'));

async function status(row, text) {
    const value = row.locator('[role="status"]').filter({ hasText: text });
    await value.waitFor({ state: 'visible', timeout: 10000 });
    check(`catalog row visibly reports ${text}`, (await value.textContent())?.trim(), text);
}

async function search(page) {
    await page.locator(catalog.form).waitFor({ state: 'visible', timeout: 10000 });
    await page.locator(catalog.scheme).selectOption('proof');
    await page.locator(catalog.query).fill('Proof Movie');
    await page.locator(catalog.id).fill(expected.catalogId);
    await page.locator(catalog.search).click();
    await page.locator(catalog.results).waitFor({ state: 'visible', timeout: 10000 });
    const row = page.locator(`${catalog.result}[${catalog.catalogId}="${expected.catalogId}"]`);
    await row.waitFor({ state: 'visible', timeout: 10000 });
    check('catalog result declares proof:42', await row.getAttribute(catalog.catalogId), expected.catalogId);
    const title = row.getByRole('heading', { level: 3, name: expected.initialTitle, exact: true });
    await title.waitFor({ state: 'visible', timeout: 10000 });
    check('catalog result visibly renders revision-one title', (await title.textContent())?.trim(), expected.initialTitle);
    const artwork = row.locator('img');
    await artwork.waitFor({ state: 'visible', timeout: 10000 });
    check('catalog result visibly renders inline typed artwork', (await artwork.getAttribute('src'))?.startsWith('data:image/'), true);
    return row;
}

function postTo(path) {
    return response => {
        const request = response.request();
        return request.method() === 'POST' && new URL(response.url()).pathname === path;
    };
}

async function completeVisibleAdd(page, row) {
    const add = row.getByRole('button', { name: 'Add', exact: true });
    await add.waitFor({ state: 'visible', timeout: 10000 });
    const response = await Promise.all([
        page.waitForResponse(postTo('/api/v1/kinds/movies/catalog/items'), { timeout: 10000 }),
        add.click(),
    ]).then(([received]) => received);
    check('first visible Add used the created response path', response.status(), 201);
    await status(row, expected.added);
    await row.getByRole('button', { name: 'Open', exact: true }).waitFor({ state: 'visible', timeout: 10000 });
    await row.getByRole('button', { name: 'Refresh', exact: true }).waitFor({ state: 'visible', timeout: 10000 });
    itemRef = await row.getAttribute(catalog.itemRef);
    check('first visible Add produced one complete durable movie reference', /^movie:[1-9][0-9]*$/.test(itemRef ?? ''), true);
}

async function completeVisibleRefresh(page, row) {
    check('searched row retains the first visible Add reference', await row.getAttribute(catalog.itemRef), suppliedItemRef);
    await status(row, expected.inLibrary);
    const refresh = row.getByRole('button', { name: 'Refresh', exact: true });
    await refresh.waitFor({ state: 'visible', timeout: 10000 });
    const response = await Promise.all([
        page.waitForResponse(response => postTo(`/api/v1/kinds/movies/catalog/items/${encodeURIComponent(suppliedItemRef)}/refresh`)(response), { timeout: 10000 }),
        refresh.click(),
    ]).then(([received]) => received);
    check('visible Refresh returned HTTP 200', response.status(), 200);
    await status(row, expected.refreshed);
    const title = row.getByRole('heading', { level: 3, name: expected.refreshedTitle, exact: true });
    await title.waitFor({ state: 'visible', timeout: 10000 });
    check('catalog row visibly renders revision-two title after Refresh', (await title.textContent())?.trim(), expected.refreshedTitle);
}

async function openItemPage(page, reference) {
    if (!/^movie:[1-9][0-9]*$/.test(reference ?? '')) throw new Error('Item detail phase requires a complete durable movie reference.');
    await page.goto(`${address}/kinds/movies/items/${encodeURIComponent(reference)}`, { waitUntil: 'networkidle' });
}

try {
    browser = await chromium.launch();
    context = await browser.newContext();
    const page = await context.newPage();
    watch(page, `g07b-catalog-${phase}`, observed);

    if (phase === 'monitor' || phase === 'restart') {
        await openItemPage(page, suppliedItemRef);
        itemRef = suppliedItemRef;
    } else {
        await page.goto(`${address}${catalog.route}`, { waitUntil: 'networkidle' });
        for (const [name, selector] of Object.entries({ form: catalog.form, scheme: catalog.scheme, query: catalog.query, id: catalog.id, results: catalog.results })) {
            check(`ordinary catalog UI exposes ${name}`, await page.locator(selector).count(), 1);
        }
        const row = await search(page);
        if (phase === 'add') await completeVisibleAdd(page, row);
        if (phase === 'refresh') await completeVisibleRefresh(page, row);
    }

    if (phase === 'monitor') {
        const wanted = page.getByRole(catalog.wantedRole, { name: catalog.wantedName });
        await wanted.waitFor({ state: 'visible', timeout: 10000 });
        check('default Wanted monitor checkbox is visibly checked', await wanted.isChecked(), true);
        await wanted.uncheck();
        const state = page.getByText(expected.notWanted, { exact: true });
        await state.waitFor({ state: 'visible', timeout: 10000 });
        check('ordinary item page visibly reports Not wanted after monitor change', (await state.textContent())?.trim(), expected.notWanted);
    }
    if (phase === 'restart') {
        const title = page.getByRole('heading', { level: 1, name: expected.refreshedTitle, exact: true });
        await title.waitFor({ state: 'visible', timeout: 10000 });
        check('ordinary item page visibly persists revision-two title after restart', (await title.textContent())?.trim(), expected.refreshedTitle);
        const state = page.getByText(expected.notWanted, { exact: true });
        await state.waitFor({ state: 'visible', timeout: 10000 });
        check('ordinary item page visibly persists Not wanted after restart', (await state.textContent())?.trim(), expected.notWanted);
    }
    check('the page issued no non-loopback browser request', observed.requested.map(entry => new URL(entry.url).hostname).filter(host => host !== '127.0.0.1' && host !== 'localhost'), []);
    check('the page reported no errors', observed.errors.map(entry => `${entry.channel}: ${entry.text}`), []);
} catch (error) {
    console.error(`G07B browser UI dependency: ${error?.message ?? error}`);
    results.push({ description: `visible Client ${phase} phase completed`, ok: false, actual: String(error?.message ?? error), expected: 'visible generic catalog UI' });
} finally {
    try { await closeBrowser(); } catch (error) {
        results.push({ description: 'bounded browser cleanup completed', ok: false, actual: String(error), expected: 'closed' });
    }
    closeWindow(observed);
    const phaseSucceeded = signal === null && results.every(result => result.ok);
    const payload = { address, phase, catalog, expected, itemRef, results, requests: observed.requested, errors: observed.errors, signal, phaseSucceeded };
    if (!phaseSucceeded) payload.blocker = 'Visible generic catalog UI is not yet available or did not satisfy this proof phase.';
    writeEvidence(evidence, `browser-${phase}.json`, payload);
}

process.exit(report(results, `${evidence}/browser-${phase}.json`));
