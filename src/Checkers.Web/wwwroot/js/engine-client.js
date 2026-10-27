// Page-side controller for the Checkers engine.
//
// - When the page is cross-origin isolated (SharedArrayBuffer available), the multithreaded
//   Checkers.Web.Engine runtime (/engine/_framework/) is booted on the Window. Its managed code runs off
//   the UI thread (deputy thread), and the Lazy SMP search threads use its pthread pool.
//   A threaded runtime cannot be created inside a Web Worker on .NET 10.
// - Otherwise, or if the threaded runtime fails to start, the app's own single-threaded runtime runs
//   the engine in a dedicated Web Worker (engine-worker.js).

const workerUrl = new URL('./engine-worker.js', import.meta.url);
const fallbackDotnetUrl = import.meta.resolve('../_framework/dotnet.js');
const threadedDotnetUrl = import.meta.resolve('../engine/_framework/dotnet.js');

// The MSBuild WasmPThreadPoolInitialSize property is not passed to dotnet.js, so it is set here.
// 16 pthreads cover 8 search threads plus the runtime's own threads (deputy, IO, timer, finalizer, ...).
const PTHREAD_POOL_INITIAL_SIZE = 16;
const THREADED_START_TIMEOUT_MS = 20000;

const canShareMemory = typeof SharedArrayBuffer !== 'undefined' && self.crossOriginIsolated;
const signalBuffer = canShareMemory ? new SharedArrayBuffer(4) : null;
const signalView = signalBuffer ? new Int32Array(signalBuffer) : null;

let threadedEngine = null;
let threadedInitPromise = null;
let threadedMode = false;
let progressTarget = null;

let worker = null;
let nextId = 1;
const pending = new Map();

function rejectPending(message) {
    for (const { reject } of pending.values()) {
        reject(new Error(message));
    }
    pending.clear();
}

function postToWorker(command, args, includeBuffer = false) {
    if (signalView) {
        Atomics.store(signalView, 0, 0);
    }
    const id = nextId++;
    const response = new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
    worker.postMessage(includeBuffer && signalBuffer ? { id, command, args, signalBuffer } : { id, command, args });
    return response;
}

function withTimeout(promise, ms, message) {
    return new Promise((resolve, reject) => {
        const id = setTimeout(() => reject(new Error(message)), ms);
        promise.then(
            result => { clearTimeout(id); resolve(result); },
            error => { clearTimeout(id); reject(error); });
    });
}

function startThreadedEngine() {
    threadedInitPromise ??= (async () => {
        const { dotnet } = await import(threadedDotnetUrl);
        const runtime = await dotnet
            .withConfig({ pthreadPoolInitialSize: PTHREAD_POOL_INITIAL_SIZE })
            .create();
        const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        const engine = exports.Checkers.Web.Engine.EngineWorker;
        await engine.InitAsync();
        threadedEngine = engine;
        return engine;
    })();
    return threadedInitPromise;
}

async function forwardProgress(engine) {
    const batch = await engine.DrainProgressAsync();
    if (!batch || !progressTarget) return;
    for (const line of batch.split('\n')) {
        if (line) {
            progressTarget.invokeMethodAsync('OnProgress', line);
        }
    }
}

function startWorker() {
    terminateWorker();
    const url = new URL(workerUrl);
    url.searchParams.set('dotnet', fallbackDotnetUrl);
    const current = new Worker(url, { type: 'module' });

    current.addEventListener('message', e => {
        if (current !== worker) return;
        const message = e.data;
        if (message.type === 'progress') {
            progressTarget?.invokeMethodAsync('OnProgress', message.json);
            return;
        }
        const request = pending.get(message.id);
        if (request) {
            pending.delete(message.id);
            if (message.error !== undefined) {
                request.reject(new Error(message.error));
            } else {
                request.resolve(message.result);
            }
        }
    });

    current.addEventListener('error', e => {
        if (current === worker) {
            rejectPending(`The engine could not run: ${e.message ?? 'unknown error'}`);
        }
    });

    worker = current;
    return postToWorker('init', [], true);
}

function terminateWorker() {
    worker?.terminate();
    worker = null;
    rejectPending('The engine was stopped.');
}

export async function start(target) {
    progressTarget = target;

    if (canShareMemory) {
        try {
            await withTimeout(startThreadedEngine(), THREADED_START_TIMEOUT_MS, 'The multithreaded engine did not start in time.');
            threadedMode = true;
            console.log(`[Checkers.Engine] Multithreaded engine started (${navigator.hardwareConcurrency} logical processors).`);
            return;
        } catch (error) {
            threadedInitPromise = null;
            threadedEngine = null;
            threadedMode = false;
            console.warn('[Checkers.Engine] Multithreaded engine failed to start; using the single-threaded worker.', error);
        }
    } else {
        console.log('[Checkers.Engine] Page is not cross-origin isolated; using the single-threaded worker.');
    }

    await startWorker();
}

export async function call(command, ...args) {
    if (threadedMode && threadedEngine) {
        const engine = threadedEngine;
        switch (command) {
            case 'chooseMove': {
                let draining = false;
                const timer = setInterval(async () => {
                    if (draining) return;
                    draining = true;
                    try {
                        await forwardProgress(engine);
                    } catch {
                        // Ignore a failed drain; the next tick retries.
                    } finally {
                        draining = false;
                    }
                }, 50);
                try {
                    const result = await engine.ChooseMoveAsync(...args);
                    await forwardProgress(engine);
                    return result;
                } finally {
                    clearInterval(timer);
                }
            }
            case 'clearTt':
                await engine.ClearTranspositionTableAsync();
                return null;
            default:
                throw new Error(`Unknown engine command: ${command}`);
        }
    }

    if (!worker) {
        throw new Error('The engine is not running.');
    }
    return postToWorker(command, args);
}

// Signals cooperative stop (1 = moveNow, 2 = cancel). Returns true if signaling is supported.
export async function signal(code) {
    if (threadedMode && threadedEngine) {
        try {
            await threadedEngine.SignalAsync(code);
            return true;
        } catch {
            return false;
        }
    }
    if (!signalView) return false;
    Atomics.store(signalView, 0, code);
    return true;
}

export function terminate() {
    if (threadedMode && threadedEngine) {
        // The Window-hosted runtime cannot be restarted; keep it (and its transposition table) and stop the search.
        threadedEngine.SignalAsync(2).catch(() => {});
        return;
    }
    terminateWorker();
}

export function isThreaded() {
    return threadedMode;
}

export function hardwareConcurrency() {
    return navigator.hardwareConcurrency || 1;
}
