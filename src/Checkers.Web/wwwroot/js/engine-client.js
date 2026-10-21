// Page-side controller for the Checkers engine Web Worker.
const workerUrl = new URL('./engine-worker.js', import.meta.url);
const dotnetUrl = import.meta.resolve('../_framework/dotnet.js');

let worker = null;
let nextId = 1;
const pending = new Map();
const canShareMemory = typeof SharedArrayBuffer !== 'undefined' && self.crossOriginIsolated;
const signalBuffer = canShareMemory ? new SharedArrayBuffer(4) : null;
const signalView = signalBuffer ? new Int32Array(signalBuffer) : null;

function rejectPending(message) {
    for (const { reject } of pending.values()) {
        reject(new Error(message));
    }
    pending.clear();
}

function post(command, args, includeBuffer = false) {
    if (signalView) {
        Atomics.store(signalView, 0, 0);
    }
    const id = nextId++;
    const response = new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
    worker.postMessage(includeBuffer && signalBuffer ? { id, command, args, signalBuffer } : { id, command, args });
    return response;
}

export function start(progressTarget) {
    terminate();
    const url = new URL(workerUrl);
    url.searchParams.set('dotnet', dotnetUrl);
    const current = new Worker(url, { type: 'module' });

    current.addEventListener('message', e => {
        if (current !== worker) return;
        const message = e.data;
        if (message.type === 'progress') {
            progressTarget.invokeMethodAsync('OnProgress', message.json);
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
    return post('init', [], true);
}

export function call(command, ...args) {
    if (!worker) {
        return Promise.reject(new Error('The engine is not running.'));
    }
    return post(command, args);
}

// Signals cooperative stop (1 = moveNow, 2 = cancel). Returns true if SharedArrayBuffer signaling is supported.
export function signal(code) {
    if (!signalView) return false;
    Atomics.store(signalView, 0, code);
    return true;
}

export function terminate() {
    worker?.terminate();
    worker = null;
    rejectPending('The engine was stopped.');
}

