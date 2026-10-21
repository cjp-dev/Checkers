// Web Worker that runs the Checkers AI engine in its own .NET runtime.
const dotnetUrl = new URL(self.location.href).searchParams.get('dotnet');
let signalView = null;

const started = (async () => {
    const { dotnet } = await import(dotnetUrl);
    const runtime = await dotnet.create();
    runtime.setModuleImports('engine-worker', {
        postProgress: json => self.postMessage({ type: 'progress', json }),
        pollSignal: () => signalView ? Atomics.load(signalView, 0) : 0,
    });
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return exports.Checkers.Web.Worker.EngineWorker;
})();

self.addEventListener('message', async e => {
    const { id, command, args, signalBuffer } = e.data;
    if (signalBuffer) {
        signalView = new Int32Array(signalBuffer);
    }
    try {
        const engine = await started;
        let result;
        switch (command) {
            case 'init': result = engine.Init(); break;
            case 'chooseMove': result = engine.ChooseMove(...args); break;
            case 'clearTt': result = engine.ClearTranspositionTable(); break;
            default: throw new Error(`Unknown engine command: ${command}`);
        }
        self.postMessage({ type: 'response', id, result });
    } catch (error) {
        self.postMessage({ type: 'response', id, error: error?.message ?? String(error) });
    }
});

