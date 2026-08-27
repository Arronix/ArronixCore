#!/usr/bin/env node

// G07.3 browser half — install, update, remove, and a tab held open across all three.
//
// One BrowserContext and one origin for the whole matrix, across real restarts of the real API. Tabs opened
// in phases one and two are never navigated again: they change only through the ordinary in-page reload,
// because reloading the document is the escape hatch this gate exists to say is the only cure.
//
//   node eng/proofs/g07-lifecycle-proof.mjs --address http://127.0.0.1:5223 --stage artifacts/g07/stage \
//        --packages artifacts/g07/packages --evidence artifacts/g07/evidence \
//        --api src/Arronix.Api/bin/Release/net11.0/Arronix.Api.dll --content-root src/Arronix.Api
//
// The API is restarted over three installations composed from staged payloads:
//
//   1. video + movies v1     clean store: load, store, deserialize, project, render a typed Movie
//   2. video + movies v2     same CLR identity and schema, different bytes, module and content hash
//   3. video                 movies uninstalled
//
// Needs Playwright's Chromium, which the hermetic rail does not carry, so this runs beside it. Evidence is
// written to the evidence directory as JSON.

import { spawn } from 'node:child_process';
import { closeSync, cpSync, existsSync, mkdirSync, openSync, rmSync } from 'node:fs';
import { connect } from 'node:net';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import {
    closeWindow, openPlaywright, openWindow, parse, recorder, report, sink, text, watch, within,
    writeEvidence
} from './g07-browser-support.mjs';

const options = parse(process.argv.slice(2));
const address = options.address ?? 'http://127.0.0.1:5223';

// The repository is located from this file, and every path is resolved against it. Absolute matters twice
// over: the application resolves a relative path against its own content root, so a relative package root
// would reach the host as a path under src/Arronix.Api and load nothing; and a path resolved against the
// caller's working directory would name a different place depending on where the command was typed.
const repositoryRoot = resolve(fileURLToPath(import.meta.url), '..', '..', '..');
const proofRoot = join(repositoryRoot, 'artifacts', 'g07');
const under = (given, fallback) => resolve(repositoryRoot, given ?? fallback);

const stage = under(options.stage, 'artifacts/g07/stage');
const packages = under(options.packages, 'artifacts/g07/packages');
const evidence = under(options.evidence, 'artifacts/g07/evidence');
const state = under(options.state, 'artifacts/g07/state');
const clientRoot = under(options.client, 'artifacts/g07/client/wwwroot');

// The built application, not its project. `dotnet run` is a launcher whose child is what holds the port,
// so the pid this run tracked would be a parent whose death leaves the server behind.
const apiAssembly = under(options.api, 'src/Arronix.Api/bin/Release/net11.0/Arronix.Api.dll');
const contentRoot = under(options['content-root'], 'src/Arronix.Api');
const dotnet = options.dotnet ?? process.env.DOTNET_COMMAND ?? 'dotnet';

// A path on the host, not on this machine: the browser fetches it from the origin under proof.
const payloadAddress = options.payload ?? 'fixtures/g07/movie.json';

// This driver deletes and rebuilds the package root on every phase, and it takes that root as an argument.
// Every path it will write to or delete must resolve to its exact place under artifacts/g07, so a mistyped
// or hostile argument is refused here rather than acted on - over the resolved values used below, and
// before anything is removed.
if (!existsSync(join(repositoryRoot, 'Arronix.sln'))) {
    console.error(`error: '${repositoryRoot}' does not look like the repository this proof belongs to.`);
    process.exit(2);
}

for (const [name, given, wanted] of [
    ['--stage', stage, join(proofRoot, 'stage')],
    ['--packages', packages, join(proofRoot, 'packages')],
    ['--evidence', evidence, join(proofRoot, 'evidence')],
    ['--state', state, join(proofRoot, 'state')],
    ['--client', clientRoot, join(proofRoot, 'client', 'wwwroot')],
]) {
    if (given !== wanted) {
        console.error(
            `error: ${name} must be '${wanted}'. This proof composes an installation by deleting and `
            + `rebuilding its own package root, so it will not act on '${given}'.`);
        process.exit(2);
    }
}

const MOVIES = 'Arronix.Media.Movies';
const VIDEO = 'Arronix.Format.Video';
const CONTRACT_CACHE = 'arronix-client-contracts-v1';
const CONTRACT_PREFIX = '/arronix-contract/';

// The only errors a page may write, and only while the host is deliberately down. Every pattern requires a
// browser-issued net::ERR_ cause, which is the whole guarantee: a rendering failure, a refused promise or a
// Blazor exception cannot match one, and neither can a fetch failure reported without that cause.
const TRANSPORT_NOISE = [
    /^Failed to load resource: net::ERR_[A-Z_]+$/,
    /^WebSocket connection to '[^']+' failed: .*net::ERR_[A-Z_]+$/,
];

const { chromium } = await openPlaywright();
const { results, check } = recorder();
const observed = sink();
const phases = [];

mkdirSync(evidence, { recursive: true });

let server = null;

// How far this run has escalated against its server: 0 none, 1 SIGTERM sent, 2 SIGKILL sent. A retry after
// a failed stop resumes from here rather than spending the graceful budget again.
let escalation = 0;
let fatal = null;
let evidencePath = join(evidence, 'lifecycle-half.json');
// Nothing may already own this origin: a stranger on this port would answer for one of the three
// installations this matrix restarts over. Before anything is started, so there is nothing to unwind.
if (await listening()) {
    console.error(
        `error: something is already listening on ${address}. The lifecycle matrix restarts the real API at `
        + 'this exact origin, so it must own the port. Stop whatever is there, or pass --address with a free '
        + 'port.');
    process.exit(2);
}

// The synchronous safety net: a try/finally cannot run when this process is signalled away, and a detached
// child would outlive the run holding the port.
for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
    process.on(signal, () => {
        reap();
        process.exit(130);
    });
}

process.on('exit', reap);

// Created inside the try below, so a failure between the two still unwinds through one cleanup.
let browser = null;
let context = null;

try {
    browser = await chromium.launch();

    // One context for the whole matrix: the store, the origin and every tab survive each restart.
    context = await browser.newContext();

    // ---- phase 1: the installation a browser starts from ------------------------------------------
    compose(['arronix.format.video', 'movies-v1']);
    await startApi('v1');

    const v1 = await manifest();
    phases.push({ phase: 'v1', installationHash: v1.installationHash, assemblies: describe(v1) });

    check(
        'the host publishes both packages',
        v1.packages.map(entry => entry.id).sort(),
        ['arronix.format.video', 'movies']);

    const videoHash = published(v1, VIDEO).contentHash;
    const moviesV1 = published(v1, MOVIES);

    const first = await open('v1-tab');

    check('a clean page reads a compatible installation', await compatibility(first), 'Compatible');
    check('and may project it', await text(first, '#contract-can-project'), 'projection permitted');
    check('its first observation is numbered one', await observation(first), 1);
    check('the movies contract was loaded', await outcome(first, MOVIES), 'Loaded');
    check('over the network, from a clean store', await source(first, MOVIES), 'Network');
    check('and so was the video contract', await outcome(first, VIDEO), 'Loaded');

    check(
        'the browser holds exactly the content hashes this installation names',
        (await heldKeys(first)).sort(),
        [moviesV1.contentHash, videoHash].sort());
    check(
        'and the page counts what the browser is holding',
        await text(first, '#stored-key-count'),
        String((await heldKeys(first)).length));

    await project(first);

    check('the payload projected', await text(first, '#projection-status'), 'Projected');
    check(
        'through the contract\'s own entity type',
        await text(first, '#projected-entity-type'),
        'Arronix.Media.Movies.Movie');
    check('and a typed Movie is rendered', await presentFields(first), [
        'artwork', 'collections', 'lifecycle', 'ratings', 'status',
    ]);
    check(
        'with its declared status choice',
        (await text(first, '[data-field-id="status"]')).includes('Released'),
        true);

    // ---- phase 2: the same assembly, a different build --------------------------------------------
    openWindow(observed, 'restart onto movies v2');
    await stopApi();
    compose(['arronix.format.video', 'movies-v2']);
    await startApi('v2');
    await settle();

    const v2 = await manifest();
    const moviesV2 = published(v2, MOVIES);
    phases.push({ phase: 'v2', installationHash: v2.installationHash, assemblies: describe(v2) });

    check('the update keeps the assembly identity', moviesV2.identity, moviesV1.identity);
    check('and changes the content hash', moviesV2.contentHash !== moviesV1.contentHash, true);
    check('and the module version identifier', moviesV2.moduleVersionId !== moviesV1.moduleVersionId, true);
    check(
        'while declaring the same projection schema',
        moviesV2.declarations.map(entry => entry.projectionSchemaHash),
        moviesV1.declarations.map(entry => entry.projectionSchemaHash));
    check('so the installation hash moves', v2.installationHash !== v1.installationHash, true);
    check('and the video contract is untouched', published(v2, VIDEO).contentHash, videoHash);

    const beforeReload = observed.requested.length;
    await reload(first);

    check('the held tab was never navigated', navigations('v1-tab'), 1);
    check('it can never satisfy this installation', await compatibility(first), 'Terminal');
    check('and says so', await text(first, '#contract-can-project'), 'projection withheld');
    check('the resident name is the refusal', await outcome(first, MOVIES), 'NameAlreadyResident');
    check('naming the build it holds', (await failure(first)).length > 0, true);
    check(
        'nothing was fetched for the build it cannot use',
        (await asked('v1-tab', beforeReload)).some(url => url.includes(moviesV2.contentHash)),
        false);

    check(
        'the projection it was showing is withdrawn',
        await text(first, '#projection-status'),
        'NoAdmittedContract');
    check('and carries no values', await first.locator('#projected-fields').count(), 0);
    check(
        'nothing may be projected through this page again',
        await first.locator('#contract-payload-unavailable').count(),
        1);
    check(
        'and there is no control left to try',
        await first.locator('#project-contract-payload').count(),
        0);

    check(
        'the terminal tab sheds the bytes the host no longer names',
        (await heldKeys(first)).includes(moviesV1.contentHash),
        false);
    check('while keeping the ones it does', (await heldKeys(first)).sort(), [videoHash]);

    const second = await open('v2-tab');

    check('a fresh tab in the same browser reads the update', await compatibility(second), 'Compatible');
    check('and may project it', await text(second, '#contract-can-project'), 'projection permitted');
    check('loading the new movies build', await outcome(second, MOVIES), 'Loaded');
    check('over the network, because the stale bytes were evicted', await source(second, MOVIES), 'Network');
    check('and reusing the video bytes it still holds', await source(second, VIDEO), 'Store');
    check(
        'the browser now holds the update and nothing stale',
        (await heldKeys(second)).sort(),
        [moviesV2.contentHash, videoHash].sort());

    await project(second);

    check('the update projects', await text(second, '#projection-status'), 'Projected');
    check(
        'into the contract\'s own entity type',
        await text(second, '#projected-entity-type'),
        'Arronix.Media.Movies.Movie');
    check('and renders a typed Movie', await presentFields(second), [
        'artwork', 'collections', 'lifecycle', 'ratings', 'status',
    ]);

    check('the held tab is still terminal', await compatibility(first), 'Terminal');

    // ---- phase 3: the package is uninstalled ------------------------------------------------------
    openWindow(observed, 'restart with movies removed');
    await stopApi();
    compose(['arronix.format.video']);
    await startApi('removed');
    await settle();

    const removed = await manifest();
    phases.push({ phase: 'removed', installationHash: removed.installationHash, assemblies: describe(removed) });

    check('the host publishes video alone', removed.packages.map(entry => entry.id), ['arronix.format.video']);
    check('and withholds nothing', removed.refused.length, 0);

    await reload(second);

    check('the held tab was never navigated', navigations('v2-tab'), 1);
    check('what remains installed is still compatible', await compatibility(second), 'Compatible');
    check('and may still be projected', await text(second, '#contract-can-project'), 'projection permitted');
    check(
        'the withdrawn contract is no longer part of an installation',
        await second.locator(`.contract-assembly[data-assembly="${MOVIES}"]`).count(),
        0);
    check(
        'it is reported as held here and no longer installed',
        await second.locator(`.contract-orphan[data-assembly="${MOVIES}"]`).count(),
        1);
    check(
        'attributed to a package this host no longer offers',
        await second.locator(`.contract-orphan[data-assembly="${MOVIES}"]`).getAttribute('data-owner'),
        'Unpublished');
    check(
        'and it is not served to anything',
        await second.locator('#contract-payload-unavailable').count(),
        1);
    check(
        'so what it projected is no longer shown',
        await text(second, '#projection-status'),
        'NoAdmittedContract');
    check('with no values left', await second.locator('#projected-fields').count(), 0);
    check(
        'the browser holds the live video bytes and nothing else',
        (await heldKeys(second)).sort(),
        [videoHash]);

    const third = await open('clean-tab');

    check('a fresh tab reads the reduced installation', await compatibility(third), 'Compatible');
    check('and may project it', await text(third, '#contract-can-project'), 'projection permitted');
    check('it holds no orphan of its own', await third.locator('.contract-orphan').count(), 0);
    check('and no withdrawn assembly', await third.locator(`[data-assembly="${MOVIES}"]`).count(), 0);
    check('video is still resident', await outcome(third, VIDEO), 'Loaded');
    check('from the store, without a fetch', await source(third, VIDEO), 'Store');
    check('and the store is exactly the live installation', (await heldKeys(third)).sort(), [videoHash]);

    check('the tab that went terminal never recovered', await compatibility(first), 'Terminal');

} catch (failure) {
    // Phases raise rather than exit: a process.exit() after the first startApi walks past the cleanup below.
    console.error(`error: ${failure?.message ?? failure}`);
    fatal = failure;
} finally {
    // Each step independently, and all of them whatever the ones before did.
    //
    // Teardown stops the API under pages that are still open, so what they then say about a host that has
    // gone is attributed to the shutdown that caused it rather than left looking unaccounted for.
    openWindow(observed, 'teardown');

    // The API first: it holds a process and a port, and a browser close that never settles would otherwise
    // hold both for the life of this run. The two closes are bounded for the same reason.
    const cleanup = [
        ['stop the API', stopApi],
        ['close the browser context', () => within(30_000, 'closing the browser context', () => context?.close())],
        ['close the browser', () => within(30_000, 'closing the browser', () => browser?.close())],
    ];

    for (const [what, step] of cleanup) {
        try {
            await step();
        } catch (failure) {
            console.error(`error: could not ${what}: ${failure?.message ?? failure}`);
            fatal ??= failure;
        }
    }

    closeWindow(observed);

    // ---- what happened in the pages, across every phase and the teardown --------------------------
    // After cleanup on purpose. These read the whole sink, and the pages keep talking while the API is
    // being stopped under them - assertions made before that would be claiming coverage of seconds they
    // had not seen.
    checkWhatThePagesSaid();

    // Last, so it records the teardown as well as the run: an evidence file written before the pages were
    // closed ends mid-sentence about its own final seconds.
    try {
        evidencePath = writeEvidence(evidence, 'lifecycle-half.json', {
            address,
            phases,
            results,
            restartWindows: observed.windows,
            navigations: observed.navigations,
            errors: observed.errors,
        });
    } catch (failure) {
        console.error(`error: could not write the evidence: ${failure?.message ?? failure}`);
        fatal ??= failure;
    }
}

const status = report(results, evidencePath);

process.exit(fatal === null ? status : 1);

// --- the installation on disk ----------------------------------------------------------------------

/// Everything the pages wrote, judged over the complete run including its teardown.
function checkWhatThePagesSaid() {
    const unexpected = observed.errors.filter(entry => entry.window === null);
    const inWindow = observed.errors.filter(entry => entry.window !== null);

    check(
        'no page wrote an error outside a restart or the teardown',
        unexpected.map(entry => `${entry.page} ${entry.channel}: ${entry.text}`),
        []);
    check(
        'and every error during one names a host that was deliberately stopped',
        inWindow
            .filter(entry => !TRANSPORT_NOISE.some(pattern => pattern.test(entry.text)))
            .map(entry => `${entry.page} ${entry.channel}: ${entry.text}`),
        []);
    check(
        'every held tab was opened once and never navigated again',
        observed.navigations
            .filter(entry => entry.url !== 'about:blank')
            .map(entry => entry.page)
            .sort(),
        ['clean-tab', 'v1-tab', 'v2-tab']);
}

/// Rebuilds the package root from staged payloads. `movies-v1` and `movies-v2` both install as `movies`:
/// one package identifier, two builds.
function compose(members) {
    rmSync(packages, { recursive: true, force: true });

    for (const member of members) {
        const from = join(stage, member);

        if (!existsSync(from)) {
            throw new Error(`'${from}' was not staged. Run the proof script that stages it.`);
        }

        cpSync(from, join(packages, member.replace(/-v\d+$/, '')), { recursive: true });
    }
}

// --- the real API, restarted at one origin ----------------------------------------------------------

async function startApi(phase) {
    if (!existsSync(apiAssembly)) {
        throw new Error(`'${apiAssembly}' is missing. Build the solution in Release before this runs.`);
    }

    const log = openSync(join(evidence, `server-${phase}.log`), 'a');

    // Assigned the instant the child exists, inside the try: a throw between spawn returning and this
    // assignment - closing the log descriptor, say - would leave a live server that cleanup has no handle
    // on. The parent's copy of that descriptor leaks one per phase otherwise, spawn failure included.
    try {
        server = spawn(
            dotnet,
            [apiAssembly],
            {
                detached: true,
                stdio: ['ignore', log, log],
                env: {
                    ...process.env,
                    ASPNETCORE_URLS: address,
                    ASPNETCORE_ENVIRONMENT: 'Production',
                    // Stated, because this launches the assembly rather than the project: the application
                    // reads its own appsettings.json from the content root.
                    ASPNETCORE_CONTENTROOT: contentRoot,
                    Arronix__Plugins__RootFolder: packages,
                    Arronix__Plugins__StateFolder: state,
                    Arronix__Api__ClientRoot: clientRoot,
                },
            });
    } finally {
        closeSync(log);
    }

    // The pid is the server itself, since the assembly is launched directly.
    const child = server;

    // spawn reports its own failure asynchronously, on the child's 'error' event, and an 'error' nobody is
    // listening for is an uncaught exception - which walks straight past the cleanup this run depends on.
    // That, and an exit before the host ever answered, are captured here and raised through this await.
    let departure = null;
    let release = () => {};
    const departed = new Promise(done => {
        release = done;
    });

    const settle = failure => {
        departure ??= failure;
        release();
    };

    child.once('error', failure => settle(failure));
    child.once('exit', (code, signal) => settle(new Error(
        `the API exited before it answered for phase '${phase}' `
        + `(${signal ?? `exit code ${code}`}). See ${evidence}/server-${phase}.log.`)));

    for (let attempt = 0; attempt < 120; attempt++) {
        if (departure !== null) {
            throw departure;
        }

        if (await answering()) {
            // Something answering this origin is not proof that it is ours: a child that lost the bind
            // race dies while another process replies. Its own fate is the last word.
            if (departure !== null) {
                throw departure;
            }

            return;
        }

        // Either the second passes or the child gives up, whichever happens first.
        await Promise.race([departed, delay(1_000)]);
    }

    if (departure !== null) {
        throw departure;
    }

    throw new Error(`the API did not come up for phase '${phase}'. See ${evidence}/server-${phase}.log.`);
}

/// Stops the API and waits for the port to be free, so the next phase cannot answer from the last one.
async function stopApi() {
    if (server === null) {
        return;
    }

    const stopping = server;

    // Tracked rather than assumed: signalling one that already exited hides that it did.
    let live = stopping.pid !== undefined
        && stopping.exitCode === null
        && stopping.signalCode === null;
    const exited = live
        ? new Promise(settled => stopping.once('exit', () => {
            live = false;
            settled();
        }))
        : Promise.resolve();

    // The handle stays reachable through every failure below. A kill that throws, or a child that outlives
    // both budgets, must leave something for the guarded cleanup and the exit reap to escalate against;
    // dropping it here is how a detached server survives a run that reported the failure.
    if (live && escalation === 0) {
        kill(stopping, 'SIGTERM');
        escalation = 1;
        await Promise.race([exited, delay(15_000)]);
    }

    if (live) {
        kill(stopping, 'SIGKILL');
        escalation = 2;
        await Promise.race([exited, delay(5_000)]);
    }

    // A surviving owned process is a failure whatever the socket says: it may be mid-shutdown, about to
    // rebind, or holding state the next phase would inherit.
    if (live) {
        throw new Error(`the API process ${stopping.pid} did not exit.`);
    }

    // Only now is the handle spent.
    server = null;
    escalation = 0;

    // And the socket, because the next phase binds it.
    for (let attempt = 0; attempt < 60; attempt++) {
        if (!(await listening())) {
            return;
        }

        await delay(500);
    }

    throw new Error(`something is still listening on ${address} after the API was stopped.`);
}

/// Signals the exact process this run started - the server itself, since the assembly is launched directly.
/// ESRCH is an answer: it is already gone. Anything else would leave a server running behind a proof
/// reporting success.
function kill(child, signal) {
    // A spawn that failed never got one, and process.kill(undefined) raises a type error, not ESRCH.
    if (child?.pid === undefined) {
        return;
    }

    try {
        process.kill(child.pid, signal);
    } catch (failure) {
        if (failure?.code !== 'ESRCH') {
            throw failure;
        }
    }
}

/// Reaps the API without waiting, for a handler that cannot await. The one place a kill failure is
/// reported rather than raised: there is no caller left to raise it to.
///
/// Bounded and synchronous, and the handle is spent only once the signal is delivered or the process is
/// already gone - clearing it first is how a kill that throws leaves nothing for the next attempt. What
/// this cannot do is guarantee a reap: a process this run may not signal at all stays running, and the
/// most that is promised is a bounded number of attempts and a message naming the pid.
function reap() {
    const dying = server;

    if (dying === null || dying.pid === undefined) {
        return;
    }

    for (let attempt = 1; attempt <= 3; attempt++) {
        try {
            process.kill(dying.pid, 'SIGKILL');
            server = null;
            return;
        } catch (failure) {
            if (failure?.code === 'ESRCH') {
                server = null;
                return;
            }

            console.error(
                `error: attempt ${attempt} to kill the API process ${dying.pid} failed `
                + `(${failure?.code ?? failure?.message ?? failure}).`);
        }
    }

    console.error(
        `error: the API process ${dying.pid} could not be signalled and may still be running. `
        + 'Stop it before the next run, which will refuse to start while it holds the port.');
}

/// Whether anything at all holds the port, answering or not.
function listening() {
    const url = new URL(address);

    return new Promise(settled => {
        const socket = connect({ host: url.hostname, port: Number(url.port) });
        const finish = held => {
            socket.destroy();
            settled(held);
        };

        socket.once('connect', () => finish(true));
        socket.once('error', () => finish(false));
        socket.setTimeout(2_000, () => finish(false));
    });
}

async function answering() {
    try {
        const response = await fetch(`${address}/api`, { signal: AbortSignal.timeout(2000) });
        return response.ok;
    } catch {
        return false;
    }
}

async function manifest() {
    const response = await fetch(`${address}/api/v1/client-contracts`);

    if (!response.ok) {
        throw new Error(`the host answered ${response.status} for its contract manifest.`);
    }

    return await response.json();
}

function published(document, assemblyName) {
    const found = document.packages
        .flatMap(entry => entry.assemblies)
        .find(entry => entry.assemblyName === assemblyName);

    if (found === undefined) {
        throw new Error(`the host published no client-safe '${assemblyName}'.`);
    }

    return found;
}

/// What each phase published: identity, build and content address of every offered file.
function describe(document) {
    return document.packages.flatMap(entry => entry.assemblies.map(assembly => ({
        package: entry.id,
        assembly: assembly.assemblyName,
        identity: assembly.identity,
        contentHash: assembly.contentHash,
        moduleVersionId: assembly.moduleVersionId,
        length: assembly.length,
    })));
}

// --- the pages -------------------------------------------------------------------------------------

/// Opens a tab and waits for its first transaction to have committed.
async function open(name) {
    const page = await context.newPage();
    watch(page, name, observed);

    await page.goto(`${address}/contracts`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#contract-compatibility', { timeout: 120_000 });

    return page;
}

/// Presses the page's own reload and waits for the observation it produced. The only path used on a held
/// tab.
async function reload(page) {
    const before = await observation(page);

    await page.click('#reload-contracts');
    await page.waitForFunction(
        seen => Number(document.querySelector('#contract-observation')?.textContent) > seen,
        before,
        { timeout: 120_000 });
}

async function project(page) {
    await page.fill('#contract-payload', payloadAddress);
    await page.click('#project-contract-payload');
    await page.waitForFunction(
        () => document.querySelector('#projection-status')?.textContent?.trim() !== 'NotAttempted',
        null,
        { timeout: 120_000 });
}

function compatibility(page) {
    return text(page, '#contract-compatibility');
}

function failure(page) {
    return text(page, '#contract-failure');
}

async function observation(page) {
    return Number(await text(page, '#contract-observation'));
}

function outcome(page, assembly) {
    return page.locator(`.contract-assembly[data-assembly="${assembly}"]`).getAttribute('data-outcome');
}

function source(page, assembly) {
    return page.locator(`.contract-assembly[data-assembly="${assembly}"]`).getAttribute('data-source');
}

async function presentFields(page) {
    const present = await page.locator('.projected-field[data-absent="false"]').evaluateAll(
        fields => fields.map(field => field.dataset.fieldId));

    return present.filter(id => ['artwork', 'ratings', 'lifecycle', 'status', 'collections'].includes(id))
        .sort();
}

/// What this browser is holding, read from the browser rather than from the application. Phase one requires
/// it to agree with the count the page states.
async function heldKeys(page) {
    return await page.evaluate(async ([cacheName, prefix]) => {
        if (typeof caches === 'undefined') {
            return [];
        }

        const store = await caches.open(cacheName);
        const held = await store.keys();

        return held
            .map(request => new URL(request.url).pathname)
            .filter(path => path.startsWith(prefix))
            .map(path => decodeURIComponent(path.substring(prefix.length)));
    }, [CONTRACT_CACHE, CONTRACT_PREFIX]);
}

function asked(name, from) {
    return observed.requested.slice(from).filter(entry => entry.page === name).map(entry => entry.url);
}

/// How many times one tab's main frame went somewhere. A new tab's blank document is not one.
function navigations(name) {
    return observed.navigations
        .filter(entry => entry.page === name && entry.url !== 'about:blank')
        .length;
}

/// Closes a restart window once the host is back and the pages have gone quiet. The window is the restart
/// and nothing more, so every error a phase's assertions then see is a defect. Bounded by a quiet period
/// and by a cap.
async function settle() {
    const quiet = 4_000;
    const cap = 45_000;
    const started = Date.now();
    let seen = observed.errors.length;
    let changed = Date.now();

    while (Date.now() - started < cap) {
        await delay(500);

        if (observed.errors.length !== seen) {
            seen = observed.errors.length;
            changed = Date.now();
        } else if (Date.now() - changed >= quiet) {
            break;
        }
    }

    closeWindow(observed);
}
