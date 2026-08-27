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

async function search(page) {
    await page.locator(catalog.form).waitFor({ state: 'visible', timeout: 10000 });
    await page.locator(catalog.scheme).selectOption('proof');
    await page.locator(catalog.query).fill('Proof Movie');
    await page.locator(catalog.id).fill('proof:42');
    await page.locator(catalog.search).click();
    await page.locator(catalog.results).waitFor({ state: 'visible', timeout: 10000 });
    const result = page.locator(`${catalog.result}[${catalog.catalogId}="proof:42"]`);
    await result.waitFor({ state: 'visible', timeout: 10000 });
    check('catalog result declares proof:42', await result.getAttribute(catalog.catalogId), 'proof:42');
    return result;
}

async function openItem(page, result) {
    const action = result.locator(catalog.openItem);
    await action.waitFor({ state: 'visible', timeout: 10000 });
    await action.click();
    const itemRef = await result.getAttribute(catalog.itemRef);
    check('opened catalog result declares a durable item reference', Boolean(itemRef), true);
    return itemRef;
}

try {
    browser = await chromium.launch();
    context = await browser.newContext();
    const page = await context.newPage();
    watch(page, `g07b-catalog-${phase}`, observed);
    if (suppliedItemRef && ['monitor', 'refresh', 'restart'].includes(phase)) {
        await page.goto(`${address}/kinds/movies/items/${encodeURIComponent(suppliedItemRef)}`, { waitUntil: 'networkidle' });
    } else {
        await page.goto(`${address}${catalog.route}`, { waitUntil: 'networkidle' });
        for (const [name, selector] of Object.entries({ form: catalog.form, scheme: catalog.scheme, query: catalog.query, id: catalog.id, results: catalog.results })) {
            check(`ordinary catalog UI exposes ${name}`, await page.locator(selector).count(), 1);
        }
        const result = await search(page);
        if (phase === 'add') {
        const add = result.locator(catalog.add);
        await add.waitFor({ state: 'visible', timeout: 10000 });
        await add.click();
            itemRef = await result.getAttribute(catalog.itemRef);
            check('first visible Add produced a durable item reference', Boolean(itemRef), true);
        }
        if (phase === 'monitor' || phase === 'refresh' || phase === 'restart') itemRef = await openItem(page, result);
    }
    if (phase === 'monitor') {
        const wanted = page.getByRole(catalog.wantedRole, { name: catalog.wantedName });
        await wanted.waitFor({ state: 'visible', timeout: 10000 });
        check('default Wanted monitor checkbox is visibly checked', await wanted.isChecked(), true);
        await wanted.uncheck();
        await page.getByText('Not wanted', { exact: true }).waitFor({ state: 'visible', timeout: 10000 });
    }
    if (phase === 'refresh') {
        const refresh = page.locator(catalog.refresh);
        await refresh.waitFor({ state: 'visible', timeout: 10000 });
        await refresh.click();
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
    writeEvidence(evidence, `browser-${phase}.json`, { address, phase, catalog, itemRef, results, requests: observed.requested, errors: observed.errors, blocker: 'Generic catalog browse/search/add/monitor/refresh UI is supplied by the concurrent Client slice.', signal });
}

process.exit(report(results, `${evidence}/browser-${phase}.json`));
