// What both halves of the G07 browser proof do the same way.
//
// One harness, not one per sub-gate: the payload proof (G07.2) and the lifecycle matrix (G07.3) drive the
// same published client and must agree on what a passing check and a page error are. Nothing here knows
// about a media kind, an installation, or a phase.

import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/// Reads `--name value` pairs, which is the whole of the calling convention.
export function parse(argv) {
    const parsed = {};
    for (let index = 0; index < argv.length; index += 2) {
        parsed[argv[index].replace(/^--/, '')] = argv[index + 1];
    }
    return parsed;
}

/// Playwright, or a refusal that says how to get it. Its Chromium is not in this repository.
export async function openPlaywright() {
    try {
        return await import('playwright');
    } catch {
        console.error(
            "error: playwright is not installed. Install it beside this repository (npm i -D playwright && "
            + "npx playwright install chromium) and re-run, or drive the identifiers by hand.");
        process.exit(2);
    }
}

/// One run's checks: every comparison is recorded, whether it passed or not.
export function recorder() {
    const results = [];

    const check = (description, actual, expected) => {
        const ok = JSON.stringify(actual) === JSON.stringify(expected);
        results.push({ description, ok, actual, expected });
        console.log(`${ok ? 'ok   ' : 'FAIL '} ${description}`);
        if (!ok) {
            console.log(`      expected: ${JSON.stringify(expected)}`);
            console.log(`      actual:   ${JSON.stringify(actual)}`);
        }
        return ok;
    };

    return { results, check };
}

/// Both error channels of one page, plus every address it asked for and every navigation it made.
///
/// An unhandled exception reaches `pageerror`; a `console.error` the application wrote does not, and
/// "nothing threw" would miss it. Navigations are watched because a claim about a tab held across a restart
/// is worthless if the tab was quietly reloaded.
export function watch(page, name, sink) {
    page.on('request', request => sink.requested.push({ page: name, url: request.url() }));
    page.on('pageerror', error => sink.errors.push(entry(name, 'pageerror', String(error), sink)));
    page.on('console', message => {
        if (message.type() === 'error') {
            sink.errors.push(entry(name, 'console.error', message.text(), sink));
        }
    });
    page.on('framenavigated', frame => {
        if (frame === page.mainFrame()) {
            sink.navigations.push({ page: name, url: frame.url(), at: Date.now() - sink.started });
        }
    });
}

function entry(name, channel, text, sink) {
    return { page: name, channel, text, at: Date.now() - sink.started, window: sink.window };
}

/// The sink `watch` fills, and the restart windows an error is allowed to fall inside.
export function sink() {
    return { started: Date.now(), window: null, windows: [], requested: [], errors: [], navigations: [] };
}

export function openWindow(sink, reason) {
    sink.window = reason;
    sink.windows.push({ reason, from: Date.now() - sink.started, to: null });
}

export function closeWindow(sink) {
    const open = sink.windows[sink.windows.length - 1];
    if (open) {
        open.to = Date.now() - sink.started;
    }
    sink.window = null;
}

export async function text(target, selector) {
    return (await target.locator(selector).textContent()).trim();
}

export async function value(target, selector) {
    return (await target.locator(selector).inputValue()).trim();
}

/// Runs one cleanup step under a deadline, so a promise that never settles cannot hold a run open.
export async function within(milliseconds, what, step) {
    let timer;
    const expiry = new Promise((_, refuse) => {
        timer = setTimeout(
            () => refuse(new Error(`${what} did not finish within ${milliseconds}ms.`)),
            milliseconds);
    });

    try {
        await Promise.race([Promise.resolve(step()), expiry]);
    } finally {
        clearTimeout(timer);
    }
}

export function writeEvidence(directory, fileName, payload) {
    mkdirSync(directory, { recursive: true });
    const path = join(directory, fileName);
    writeFileSync(path, JSON.stringify(payload, null, 2));
    return path;
}

export function report(results, evidencePath) {
    const failed = results.filter(entry => !entry.ok);
    console.log(`\n${results.length - failed.length} of ${results.length} browser checks passed.`);
    console.log(`Evidence: ${evidencePath}`);
    return failed.length === 0 ? 0 : 1;
}
