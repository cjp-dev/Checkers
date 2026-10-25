# Checkers Web Architecture Implementation Plan

## Goal & Overview
You are tasked with upgrading the **Checkers** application (`C:\Udvikling\Spil\Checkers`) to implement the architectural recommendations from the Web Architecture Review.

### Summary of Changes to Implement
1. **Engine Execution & "Move Now" Support (`Checkers.Core` & `Checkers.App`)**:
   - Remove the browser UI yielding hacks (`await Task.Delay(1, cancellationToken)`) from `MinimaxPlayer` so the engine runs at maximum synchronous throughput without 4ms browser timer stalls on shallow depths.
   - Add `CancellationToken moveNowToken = default` support to `IPlayer` and `MinimaxPlayer` so "Move Now" stops the search gracefully and returns the best move found so far.
   - Introduce `IEngineHost` and `LocalEngineHost` in `Checkers.App.Services` to decouple `MainViewModel` from how the engine is hosted (in-process thread pool on WPF/tests vs. Web Worker on Blazor WebAssembly), while keeping 100% backward compatibility with all existing `MainViewModel` constructors and unit tests.
   - Add `MoveNowCommand` (`Ctrl+M`) to `MainViewModel`.
   - Introduce `ISettingsStore` in `Checkers.App.Services` so user preferences (`GameSettings`, `IsAnalysisVisible`, `IsSoundOn`) persist automatically.
2. **Dedicated Web Worker Engine & Non-Destructive Cancellation (`Checkers.Web`)**:
   - Implement a dedicated Web Worker (`EngineProtocol.cs`, `EngineWorker.cs`, `WebEngineHost.cs`, `wwwroot/js/engine-worker.js`, `wwwroot/js/engine-client.js`) that runs `MinimaxPlayer` in an isolated .NET WebAssembly runtime with 100ms throttled progress reporting (`ProgressPoster`) and cooperative `SharedArrayBuffer` signaling (with `worker.terminate()` fallback) so `Move Now` and `Cancel` preserve the Transposition Table in memory.
   - Add `wwwroot/staticwebapp.config.json` with `Cross-Origin-Opener-Policy` and `Cross-Origin-Embedder-Policy` headers.
   - Implement `LocalStorageSettingsStore` in `Checkers.Web.Services`.
3. **Decoupled `<AnalysisPanel>` Rendering (`Checkers.Web`)**:
   - Extract the Analysis panel from `Home.razor` into `Components/AnalysisPanel.razor` with `_renderPending` batching so 10 Hz search telemetry updates never re-render the 64-square `Board` or `Move History` list.
4. **Responsive Design, Touch/Pointer Drag-and-Drop, & Accessibility (`Checkers.Web`)**:
   - Upgrade `wwwroot/css/app.css` to use `100dvh`, CSS Container Queries (`container-type: size`, `cqw`, `cqh`) with orientation-aware mobile breakpoints, CSS Grid for `.dialog-with-picture`, `@media (prefers-reduced-motion: reduce)`, and real-time `--blazor-load-percentage` loading ring styling.
   - Add Pointer Events touch drag-and-drop and keyboard (`Enter`/`Space` + `tabindex`) accessibility to `Components/Board.razor`.
   - Expose a **Move Now** button in `Home.razor` and `Ctrl+M` shortcut in `wwwroot/js/checkers.js`.

> **CRITICAL RULE**: Do NOT break any existing unit tests in `tests/Checkers.Core.Tests` or `tests/Checkers.App.Tests`, and do NOT break `src/Checkers.Wpf`. Run `dotnet build` and `dotnet test` after each phase.

---

## Phase 1: Core Engine Enhancements (`src/Checkers.Core`)

### 1.1 Update `src/Checkers.Core/AI/IPlayer.cs`
Add an overload of `GetMoveAsync` that accepts `CancellationToken moveNowToken`:

```csharp
    /// <summary>
    /// Chooses a move from the available legal moves for the given state,
    /// supporting both hard cancellation and early "Move Now" termination.
    /// </summary>
    ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        IReadOnlyList<ulong>? stateHashHistory,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken) =>
        GetMoveAsync(state, legalMoves, progress, stateHashHistory, cancellationToken);
```

### 1.2 Update `src/Checkers.Core/AI/MinimaxPlayer.cs`
1. Remove the four `await Task.Delay(1, cancellationToken);` blocks (around lines 166–169, 194–197, 329, and 371–374) so `MinimaxPlayer` has zero artificial timer stalls.
2. Add the `CancellationToken moveNowToken = default` overload (or optional parameter) to `GetMoveAsync`:
   ```csharp
   public ValueTask<Move> GetMoveAsync(
       BoardState state,
       IReadOnlyList<Move> legalMoves,
       IProgress<SearchAnalysis>? progress,
       IReadOnlyList<ulong>? stateHashHistory,
       CancellationToken cancellationToken = default) =>
       GetMoveAsync(state, legalMoves, progress, stateHashHistory, cancellationToken, moveNowToken: default);

   public ValueTask<Move> GetMoveAsync(
       BoardState state,
       IReadOnlyList<Move> legalMoves,
       IProgress<SearchAnalysis>? progress,
       IReadOnlyList<ulong>? stateHashHistory,
       CancellationToken cancellationToken,
       CancellationToken moveNowToken)
   ```
   *(Note: Since `await Task.Delay` is removed, `GetMoveAsync` can return `ValueTask.FromResult(ChooseMove(...))` or execute synchronously and return `new ValueTask<Move>(...)`).*
3. Inside `GetMoveAsync` and `NegaMax` (and `Quiescence`), pass `moveNowToken` alongside `cancellationToken`:
   - Keep `cancellationToken.ThrowIfCancellationRequested();` for hard cancellation.
   - Wherever `sw.ElapsedMilliseconds >= hardLimitMs` is checked (in the root loop at line 261, inside `NegaMax` at line 640, and inside `Quiescence` if checked), change the condition to:
     ```csharp
     if (moveNowToken.IsCancellationRequested || sw.ElapsedMilliseconds >= hardLimitMs)
     ```
     This causes `moveNowToken` to abort the current depth cleanly and return `bestMoveOverall` (the best move found so far) without throwing `OperationCanceledException`!
4. Run `dotnet test tests/Checkers.Core.Tests/Checkers.Core.Tests.csproj` to verify all Core tests pass.

---

## Phase 2: Engine Host & Settings Abstractions (`src/Checkers.App`)

### 2.1 Create `src/Checkers.App/Services/IEngineHost.cs`
Create `IEngineHost.cs`:
```csharp
using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.Services;

/// <summary>
/// Abstraction for running the Checkers AI engine (in-process on desktop/tests, or in a Web Worker on the browser).
/// </summary>
public interface IEngineHost
{
    Task<(Move Move, SearchAnalysis? LastAnalysis)> ChooseMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken);

    void ClearTranspositionTable();
}
```

### 2.2 Create `src/Checkers.App/Services/LocalEngineHost.cs`
Create `LocalEngineHost.cs` for in-process `Task.Run` execution (used by WPF and unit tests):
```csharp
using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.App.Services;

/// <summary>
/// Runs the Checkers AI engine on a thread-pool thread in the current process.
/// </summary>
public sealed class LocalEngineHost : IEngineHost
{
    private TranspositionTable _transpositionTable = TranspositionTable.FromEntries(GameSettings.DefaultTranspositionTableEntries);
    private CheckersVariant _lastVariant = GameSettings.Default.Variant;

    public Func<GameSettings, TimeSpan, IPlayer>? PlayerFactory { get; set; }

    public async Task<(Move Move, SearchAnalysis? LastAnalysis)> ChooseMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken)
    {
        if (_transpositionTable.Capacity != settings.TranspositionTableEntries)
        {
            _transpositionTable = TranspositionTable.FromEntries(settings.TranspositionTableEntries);
        }
        else if (_lastVariant != settings.Variant)
        {
            _transpositionTable.Clear();
        }
        _lastVariant = settings.Variant;

        IPlayer ai = PlayerFactory?.Invoke(settings, computerTimeLeft) ?? new MinimaxPlayer(
            settings.ToLimits(computerTimeLeft),
            ruleEngine: new RuleEngine(settings.Variant),
            evaluator: new EvaluationFunction(settings.Variant),
            transpositionTable: _transpositionTable);

        Move move = await Task.Run(
            async () => await ai.GetMoveAsync(state, legalMoves, progress, stateHashHistory, cancellationToken, moveNowToken),
            cancellationToken);

        SearchAnalysis? lastAnalysis = (ai as MinimaxPlayer)?.LastAnalysis;
        return (move, lastAnalysis);
    }

    public void ClearTranspositionTable() => _transpositionTable.Clear();
}
```

### 2.3 Create `src/Checkers.App/Services/ISettingsStore.cs`
Create `ISettingsStore.cs`:
```csharp
using Checkers.App.Models;

namespace Checkers.App.Services;

public sealed record AppPreferences(
    GameSettings Game,
    bool ShowAnalysis = true,
    bool SoundEnabled = true);

public interface ISettingsStore
{
    AppPreferences Load();
    bool Save(AppPreferences preferences);
}
```

### 2.4 Update `src/Checkers.App/ViewModels/MainViewModel.cs`
1. Add optional `IEngineHost? engineHost = null` and `ISettingsStore? settingsStore = null` parameters to the `MainViewModel` constructor (defaulting to `null` so all existing callers in `MainViewModelTests` and `Checkers.Wpf` compile without changes):
   ```csharp
   private readonly LocalEngineHost _localEngineHost = new();
   private readonly IEngineHost _engineHost;
   private readonly ISettingsStore? _settingsStore;
   private readonly bool _settingsLoaded;
   private CancellationTokenSource? _moveNowCts;
   ```
   In the constructor:
   - Set `_engineHost = engineHost ?? _localEngineHost;`
   - Set `_settingsStore = settingsStore;`
   - If `_settingsStore != null`, load `AppPreferences saved = _settingsStore.Load();` and initialize `Settings = saved.Game.Normalize();`, `IsAnalysisVisible = saved.ShowAnalysis;`, `IsSoundOn = saved.SoundEnabled;`, `_soundService.IsEnabled = saved.SoundEnabled;`, and if `session == null`, recreate `Session = new GameSession(new RuleEngine(Settings.Variant));`. Set `_settingsLoaded = true;`.
2. Wire `AiPlayerFactory` to `_localEngineHost.PlayerFactory`:
   ```csharp
   public Func<GameSettings, TimeSpan, IPlayer>? AiPlayerFactory
   {
       get => _localEngineHost.PlayerFactory;
       set => _localEngineHost.PlayerFactory = value;
   }
   ```
3. Add `MoveNowCommand` and notify it when `IsAiThinking` changes:
   ```csharp
   [ObservableProperty]
   [NotifyCanExecuteChangedFor(nameof(MoveNowCommand))]
   private bool _isAiThinking;

   [RelayCommand(CanExecute = nameof(IsAiThinking))]
   public void MoveNow() => _moveNowCts?.Cancel();
   ```
4. Save preferences whenever `Settings`, `IsAnalysisVisible`, or `IsSoundOn` changes:
   ```csharp
   private void SavePreferences()
   {
       if (_settingsLoaded)
       {
           _settingsStore?.Save(new AppPreferences(Settings, IsAnalysisVisible, IsSoundOn));
       }
   }
   ```
   Call `SavePreferences()` in `OnSettingsChanged`, `OnIsAnalysisVisibleChanged`, and `ToggleSound` / `OnIsSoundOnChanged`.
5. In `CheckAndTriggerAiTurn`:
   - Create `_moveNowCts = new CancellationTokenSource();` and `var moveNowToken = _moveNowCts.Token;`.
   - Call `var (move, lastAnalysis) = await _engineHost.ChooseMoveAsync(Session.CurrentState, legalMoves, Settings, _computerTimeLeft, stateHashes, progress, token, moveNowToken);`
   - Clean up `_moveNowCts` in `CancelAi()` and `finally`.
6. Run `dotnet test` across the solution to ensure all existing tests in `Checkers.App.Tests` pass!

---

## Phase 3: Dedicated Web Worker & Persistence (`src/Checkers.Web`)

### 3.1 Update `src/Checkers.Web/Checkers.Web.csproj`
Ensure `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` is present in `<PropertyGroup>` if needed, and keep existing references.

### 3.2 Create `src/Checkers.Web/Worker/EngineProtocol.cs`
Create `EngineProtocol.cs` using AOT-safe `JsonSerializerContext` source generation:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Bitboards;
using Checkers.Core.Models;

namespace Checkers.Web.Worker;

internal sealed record MoveRequest(
    ulong WhiteMen,
    ulong BlackMen,
    ulong WhiteKings,
    ulong BlackKings,
    ulong Hash,
    int HalfMoveClock,
    int SideToMove,
    int FullMoveNumber,
    CheckersVariant Variant,
    TimeControlMode Mode,
    int Depth,
    long TimeTicks,
    bool UseOpeningBook,
    int TranspositionTableEntries,
    ulong[] StateHashHistory);

internal sealed record ProgressDto(
    string Move,
    string Depth,
    string Value,
    string BestMove,
    string Nodes,
    string Evaluations,
    string Time,
    bool FromBook);

internal sealed record MoveResponse(
    string MoveNotation,
    ProgressDto? LastAnalysis);

[JsonSerializable(typeof(MoveRequest))]
[JsonSerializable(typeof(ProgressDto))]
[JsonSerializable(typeof(MoveResponse))]
internal sealed partial class EngineProtocolJson : JsonSerializerContext;

internal static class EngineProtocol
{
    public static string WriteRequest(
        BoardState state,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory)
    {
        BitPosition bp = state.BitPosition;
        SearchLimits limits = settings.ToLimits(computerTimeLeft);
        var req = new MoveRequest(
            bp.WhiteMen,
            bp.BlackMen,
            bp.WhiteKings,
            bp.BlackKings,
            bp.Hash,
            bp.HalfMoveClock,
            (int)bp.SideToMove,
            state.FullMoveNumber,
            settings.Variant,
            limits.Mode,
            limits.Depth,
            limits.Time.Ticks,
            limits.UseOpeningBook,
            settings.TranspositionTableEntries,
            stateHashHistory.ToArray());
        return JsonSerializer.Serialize(req, EngineProtocolJson.Default.MoveRequest);
    }

    public static (BoardState State, CheckersVariant Variant, SearchLimits Limits, int TtEntries, ulong[] History) ReadRequest(string json)
    {
        MoveRequest req = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.MoveRequest)!;
        var bp = new BitPosition
        {
            WhiteMen = req.WhiteMen,
            BlackMen = req.BlackMen,
            WhiteKings = req.WhiteKings,
            BlackKings = req.BlackKings,
            Hash = req.Hash,
            HalfMoveClock = req.HalfMoveClock,
            SideToMove = (PieceColor)req.SideToMove
        };
        var state = new BoardState(bp, req.FullMoveNumber);
        TimeSpan time = TimeSpan.FromTicks(req.TimeTicks);
        SearchLimits limits = req.Mode switch
        {
            TimeControlMode.FixedDepth => SearchLimits.FixedDepth(req.Depth, req.UseOpeningBook),
            TimeControlMode.TimePerMove => SearchLimits.TimePerMove(time, req.UseOpeningBook),
            _ => SearchLimits.TimePerGame(time > TimeSpan.Zero ? time : TimeSpan.FromMilliseconds(10), req.UseOpeningBook)
        };
        return (state, req.Variant, limits, req.TranspositionTableEntries, req.StateHashHistory);
    }

    public static string WriteProgress(SearchAnalysis info) => JsonSerializer.Serialize(
        new ProgressDto(info.Move, info.Depth, info.Value, info.BestMove, info.Nodes, info.Evaluations, info.Time, info.FromBook),
        EngineProtocolJson.Default.ProgressDto);

    public static SearchAnalysis ReadProgress(string json)
    {
        ProgressDto dto = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.ProgressDto)!;
        return new SearchAnalysis
        {
            Move = dto.Move,
            Depth = dto.Depth,
            Value = dto.Value,
            BestMove = dto.BestMove,
            Nodes = dto.Nodes,
            Evaluations = dto.Evaluations,
            Time = dto.Time,
            FromBook = dto.FromBook
        };
    }

    public static string WriteResponse(Move move, SearchAnalysis? last) => JsonSerializer.Serialize(
        new MoveResponse(
            move.Notation,
            last is null ? null : new ProgressDto(last.Move, last.Depth, last.Value, last.BestMove, last.Nodes, last.Evaluations, last.Time, last.FromBook)),
        EngineProtocolJson.Default.MoveResponse);

    public static (Move Move, SearchAnalysis? LastAnalysis) ReadResponse(string json, IReadOnlyList<Move> legalMoves)
    {
        MoveResponse res = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.MoveResponse)!;
        Move chosen = legalMoves.FirstOrDefault(m => m.Notation == res.MoveNotation) ?? legalMoves[0];
        SearchAnalysis? last = res.LastAnalysis is { } dto
            ? new SearchAnalysis
            {
                Move = dto.Move,
                Depth = dto.Depth,
                Value = dto.Value,
                BestMove = dto.BestMove,
                Nodes = dto.Nodes,
                Evaluations = dto.Evaluations,
                Time = dto.Time,
                FromBook = dto.FromBook
            }
            : null;
        return (chosen, last);
    }
}
```

### 3.3 Create `src/Checkers.Web/Worker/EngineWorker.cs`
Create `EngineWorker.cs` with `ProgressPoster` (100ms throttling + immediate depth/best-move forwarding) and `[JSImport("pollSignal", "engine-worker")]` so the worker can poll the `SharedArrayBuffer` signal (`1 = moveNow`, `2 = cancel`) and cancel the tokens without terminating the worker:
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Checkers.Core.AI;
using Checkers.Core.AI.Book;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Web.Worker;

[SupportedOSPlatform("browser")]
public static partial class EngineWorker
{
    private static TranspositionTable s_tt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
    private static CheckersVariant s_lastVariant = CheckersVariant.International;

    [JSExport]
    public static void Init()
    {
        // Pre-warm default opening book on worker startup
        _ = OpeningBook.GetDefault(CheckersVariant.International);
    }

    [JSExport]
    public static void ClearTranspositionTable() => s_tt.Clear();

    [JSExport]
    public static string ChooseMove(string requestJson)
    {
        (BoardState state, CheckersVariant variant, SearchLimits limits, int ttEntries, ulong[] history) =
            EngineProtocol.ReadRequest(requestJson);

        if (s_tt.Capacity != ttEntries)
        {
            s_tt = TranspositionTable.FromEntries(ttEntries);
        }
        else if (s_lastVariant != variant)
        {
            s_tt.Clear();
        }
        s_lastVariant = variant;

        var ruleEngine = new RuleEngine(variant);
        IReadOnlyList<Move> legalMoves = ruleEngine.GetLegalMoves(state);

        using var cancelCts = new CancellationTokenSource();
        using var moveNowCts = new CancellationTokenSource();
        var poster = new ProgressPoster(cancelCts, moveNowCts);

        var ai = new MinimaxPlayer(
            limits,
            ruleEngine: ruleEngine,
            evaluator: new EvaluationFunction(variant),
            transpositionTable: s_tt);

        Move move = ai.GetMoveAsync(state, legalMoves, poster, history, cancelCts.Token, moveNowCts.Token)
            .GetAwaiter().GetResult();

        return EngineProtocol.WriteResponse(move, ai.LastAnalysis);
    }

    [JSImport("postProgress", "engine-worker")]
    private static partial void PostProgress(string json);

    [JSImport("pollSignal", "engine-worker")]
    private static partial int PollSignal();

    private sealed class ProgressPoster(CancellationTokenSource cancelCts, CancellationTokenSource moveNowCts) : IProgress<SearchAnalysis>
    {
        private const long IntervalMs = 100;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastPostMs = -IntervalMs;
        private string _lastDepth = "";
        private string _lastBest = "";

        public void Report(SearchAnalysis info)
        {
            int signal = PollSignal();
            if (signal == 2)
            {
                cancelCts.Cancel();
                return;
            }
            if (signal == 1)
            {
                moveNowCts.Cancel();
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            if (info.Depth == _lastDepth && info.BestMove == _lastBest && now - _lastPostMs < IntervalMs)
            {
                return;
            }

            _lastDepth = info.Depth;
            _lastBest = info.BestMove;
            _lastPostMs = now;
            PostProgress(EngineProtocol.WriteProgress(info));
        }
    }
}
```

### 3.4 Create `src/Checkers.Web/wwwroot/js/engine-worker.js` and `engine-client.js`
Create `src/Checkers.Web/wwwroot/js/engine-worker.js`:
```javascript
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
```

Create `src/Checkers.Web/wwwroot/js/engine-client.js`:
```javascript
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
```

### 3.5 Create `src/Checkers.Web/Services/WebEngineHost.cs`
Create `WebEngineHost.cs` implementing `IEngineHost`.
- On `ChooseMoveAsync`, if `moveNowToken` or `cancellationToken` fires:
  - First invoke `client.Invoke<bool>("signal", isMoveNow ? 1 : 2)`.
  - If `signal` returns `true`, wait up to `300ms` (`Task.WhenAny(search, Task.Delay(300))`) for the worker to finish cleanly and return its response without losing the Transposition Table!
  - If `signal` returns `false` (no `SharedArrayBuffer`) or the 300ms grace period times out, fall back to `Restart(client)` (`client.InvokeVoid("terminate")` + `StartAsync()`) and return the best move reported in `lastAnalysis` (for `MoveNow`) or throw `OperationCanceledException` (for `Cancel`).

### 3.6 Create `src/Checkers.Web/wwwroot/staticwebapp.config.json`
Create `src/Checkers.Web/wwwroot/staticwebapp.config.json` to enable Cross-Origin Isolation (`SharedArrayBuffer`) on Azure Static Web Apps:
```json
{
  "globalHeaders": {
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cross-Origin-Embedder-Policy": "require-corp"
  }
}
```

### 3.7 Create `src/Checkers.Web/Services/LocalStorageSettingsStore.cs` and Update `Program.cs`
1. Create `LocalStorageSettingsStore.cs` implementing `ISettingsStore` using `IJSInProcessRuntime` (`localStorage.getItem("checkers.settings")` / `localStorage.setItem("checkers.settings", json)`) with source-generated `JsonSerializerContext` for `AppPreferences`.
2. In `src/Checkers.Web/Program.cs`, register `ISettingsStore` (`LocalStorageSettingsStore`) and `IEngineHost` (`WebEngineHost`), and pass both into `new MainViewModel(...)`.

---

## Phase 4: Decoupled `<AnalysisPanel>` & Responsive/Touch UI (`src/Checkers.Web`)

### 4.1 Create `src/Checkers.Web/Components/AnalysisPanel.razor`
Extract the Analysis `<aside>` from `Home.razor` into `src/Checkers.Web/Components/AnalysisPanel.razor`:
- Inject or accept `[Parameter, EditorRequired] public AnalysisViewModel Analysis { get; set; } = default!;`
- Subscribe to `Analysis.Updated` in `OnInitialized` and unsubscribe in `Dispose`.
- Coalesce renders using `_renderPending` + `await Task.Yield(); StateHasChanged();`.
- In `src/Checkers.Web/Pages/Home.razor`:
  - Replace the inline `<aside class="panel analysis-panel">...</aside>` with `<AnalysisPanel Analysis="Vm.Analysis" />`.
  - **Remove** `Vm.Analysis.Updated += OnAnalysisUpdated;` and `Vm.Analysis.Updated -= OnAnalysisUpdated;` from `Home.razor` so `Home` and `Board` never re-render when search telemetry updates!

### 4.2 Add "Move Now" UI & Shortcut (`Home.razor` & `checkers.js`)
1. In `src/Checkers.Web/Pages/Home.razor`:
   - Add `new("Move Now", Vm.MoveNowCommand, "Ctrl+M", SeparatorBefore: true)` to the `"Game"` menu.
   - In `.action-buttons`, add a **Move Now** button (`@if (Vm.IsAiThinking)`) or a dedicated button bound to `Vm.MoveNowCommand` so mobile/desktop users can trigger Move Now with a single tap/click.
   - In `OnShortcut(string key)`, add `"m" => RunAsync(Vm.MoveNowCommand),`.
2. In `src/Checkers.Web/wwwroot/js/checkers.js`:
   - Ensure `shortcutKeys` includes `'m'` (it is currently `'oszyman'`, which already includes `'m'`).

### 4.3 Add Pointer Drag-and-Drop & Keyboard Accessibility (`Board.razor` & `checkers.js`)
1. In `src/Checkers.Web/Components/Board.razor`:
   - Add `tabindex="0"` and `@onkeydown="e => HandleKeyDown(e, sq)"` on `.square` elements (`Enter` or ` ` invokes `OnSquareClick.InvokeAsync(sq)`).
   - Support touch drag-and-drop alongside mouse drag-and-drop by adding `data-row="@r"` and `data-col="@c"` on `.square` elements, and handling `@onpointerdown` on movable pieces and `@onpointerup` on the board (using a small JS helper `checkers.getSquareAtPoint(clientX, clientY)` in `checkers.js` that uses `document.elementFromPoint(x, y)?.closest('.square')` to return `[row, col]`).
2. Add `:focus-visible` styling for `.square:focus-visible` in `wwwroot/css/app.css`.

### 4.4 Upgrade Responsive CSS & Loading Progress (`wwwroot/css/app.css` & `wwwroot/index.html`)
1. **Container Queries & `100dvh`**:
   - In `src/Checkers.Web/wwwroot/css/app.css`:
     - Update `.app` to `min-height: 100vh; min-height: 100dvh;`.
     - Add `container-type: inline-size;` on `.content`.
     - Size `.checkerboard-container` using `width: min(calc(100cqw - 420px), 80dvh, 1080px); aspect-ratio: 1 / 1;` (removing the redundant `height` property so `aspect-ratio: 1 / 1` governs height cleanly).
     - On `@media (max-width: 900px) and (max-aspect-ratio: 1/1), (max-width: 600px)`, stack `.content` vertically (`flex-direction: column`) and size `.checkerboard-container` to `width: min(94cqw, 62dvh, 1080px)`.
     - On compact landscape screens (`@media (max-width: 900px) and (min-aspect-ratio: 1/1) and (min-width: 601px)`), keep `flex-direction: row` with a compact `.side-panels` (`min-width: 240px; max-width: 280px;`) so landscape phones and tablets display the board and controls side-by-side without vertical clipping.
2. **Dialog Grid & Reduced Motion**:
   - Convert `.dialog-with-picture` in `app.css` from `float: left` to CSS Grid (`grid-template-columns: 140px minmax(0, 1fr); gap: 0 1.25rem;`) with a `@media (max-width: 520px)` single-column rule.
   - Add `@media (prefers-reduced-motion: reduce)` disabling animations on `.square.mandatory-capture`, `.capture-target-indicator`, and `.thinking-progress`.
3. **Percentage Loading Progress**:
   - Update `.loading-progress circle:last-child` and `.loading-progress-text:after` in `app.css` and `index.html` to use `var(--blazor-load-percentage, 0%)` and `var(--blazor-load-percentage-text, "Loading Checkers...")`.

---

## Phase 5: Verification & Testing

1. Add unit tests in `tests/Checkers.Core.Tests/AiTests.cs` verifying that triggering `moveNowToken` during a search returns a valid move without throwing `OperationCanceledException`.
2. Add unit tests in `tests/Checkers.App.Tests/MainViewModelTests.cs` verifying:
   - `MoveNowCommand` is enabled while `IsAiThinking` is true and disabled otherwise.
   - `ISettingsStore` loads initial preferences and saves when `Settings`, `IsAnalysisVisible`, or `IsSoundOn` changes.
3. Run full build and test suite:
   ```powershell
   dotnet build Checkers.slnx
   dotnet test Checkers.slnx
   ```
4. Verify zero compiler warnings or errors across `Checkers.Core`, `Checkers.App`, `Checkers.Wpf`, `Checkers.Web`, and both test projects.

