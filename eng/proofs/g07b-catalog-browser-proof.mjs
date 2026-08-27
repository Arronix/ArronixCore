#!/usr/bin/env node

// G07B browser half. It only drives the ordinary production Client and reads rendered accessibility state;
// it deliberately has no page-context fetch or Client interop seam. Generic catalog UI is supplied by a
// following slice, so this foundation records the exact selectors and interactions it awaits rather than
// pretending the Contracts diagnostic page is a catalog browser.

import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { openPlaywright, parse, recorder, report, sink, watch } from './g07-browser-support.mjs';

const options = parse(process.argv.slice(2));
const address = options.address ?? 'http://127.0.0.1:5225';
const evidence = options.evidence ?? 'artifacts/g07b-browser-proof/evidence';
const required = {
    workspace: { selector: '#catalog-workspace' },
    catalogSearch: { selector: '#catalog-search' },
    result: { selector: '[data-catalog-id="proof:42"]' },
    add: { selector: '[data-action="catalog-add"]' },
    monitor: { selector: '[data-action="catalog-monitor"]' },
    refresh: { selector: '[data-action="catalog-refresh"]' },
};

const { chromium } = await openPlaywright();
const { results, check } = recorder();
const observed = sink();
let browser; let context; let fatal = null;
mkdirSync(evidence, { recursive: true });

try {
    browser = await chromium.launch();
    context = await browser.newContext();
    const page = await context.newPage();
    watch(page, 'g07b-catalog', observed);
    await page.goto(`${address}/`, { waitUntil: 'networkidle' });

    // These are visible-document assertions only. A missing generic UI is reported as the known dependency,
    // not weakened into direct API calls or page-context requests.
    for (const [name, expected] of Object.entries(required)) {
        const locator = page.locator(expected.selector);
        check(`ordinary generic UI exposes ${name}`, await locator.count(), 1);
    }
    check('the page issued no non-loopback browser request', observed.requested
        .map(entry => new URL(entry.url).hostname)
        .filter(host => host !== '127.0.0.1' && host !== 'localhost'), []);
    check('the page reported no errors', observed.errors.map(entry => `${entry.channel}: ${entry.text}`), []);
} catch (error) {
    fatal = error;
    console.error(`G07B browser UI dependency: ${error?.message ?? error}`);
} finally {
    try { await context?.close(); await browser?.close(); } catch (error) { fatal ??= error; }
    writeFileSync(join(evidence, 'browser-half.json'), JSON.stringify({
        address, requiredAccessibleSelectors: required, results, requests: observed.requested, errors: observed.errors,
        blocker: 'Generic catalog browse/search/add/monitor/refresh accessible UI is not yet integrated.',
    }, null, 2));
}

process.exit(fatal === null ? report(results, join(evidence, 'browser-half.json')) : 1);
