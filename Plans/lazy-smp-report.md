# Lazy SMP — implementation report

Date: 2026-10-27. Branch `feature/lazy-smp`. Spec: `Checkers Lazy SMP.md` (in the Spil folder). The design follows Gomoku's Lazy SMP and its threaded web engine.

## What was done

| Step | Change | Files |
|---|---|---|
| 1 | **Lock-free transposition table.** Each 16-byte entry is two 64-bit words: `_keyWord` = `Key32 ^ Fold(_data)` plus age, `_data` = score, static eval, move, depth, flags. A half-written entry reads as a miss. Telemetry counters only run with `CollectStatistics`. The megabyte constructor now divides by the real 16-byte entry size (callers halved, so every table keeps its size). | `src/Checkers.Core/AI/TranspositionTable.cs` |
| 2 | **Per-thread state split.** `SearchWorker` holds the move buffers, killers, history, `DrawTable`, node counters and the NegaMax / quiescence / ordering code. `MinimaxPlayer` keeps the root loop, book, time control and result. | `src/Checkers.Core/AI/SearchWorker.cs`, `MinimaxPlayer.cs` |
| 3 | **Lazy SMP.** `MinimaxPlayer.Threads` (default 1). Helpers run full-window iterative deepening on the same root; odd helpers start one ply deeper, each rotates the root moves after the first. Only the main thread decides the move. A shared stop flag, checked every 4,096 nodes, ends the helpers; the main thread waits for them. Custom evaluators, no-TT searches and `SearchMultiPv` always use one thread. | `MinimaxPlayer.cs`, `SearchWorker.cs` |
| 4 | **Tests.** TT stress test (8 threads, tiny table), SMP finds the clearly best move (5 positions, 2–8 threads), node reporting, Move Now / cancel / hard limit with 4 threads, custom evaluator stays on one thread. Settings tests in the App test project. | `tests/Checkers.Core.Tests/LazySmpTests.cs`, `tests/Checkers.App.Tests/*` |
| 5 | **Tooling.** `signature` (fixed-depth node counts of 160 searches, save/compare), `smp-bench` (time to depth per thread count), `smp-match` (N threads against M threads, same build). The match runner takes two player factories; `eval-match` is unchanged. | `tools/Checkers.Benchmark/SmpCommands.cs`, `EvaluationMatchRunner.cs` |
| 6 | **Settings.** `GameSettings.ThreadCount` (0 = Auto), saved in settings and the game record. WPF slider (Auto = min(16, cores), up to all cores); web drop-down (Auto = min(hardwareConcurrency, 4), up to 8; disabled when the threaded engine isn't running). | `Checkers.App`, `Checkers.Wpf`, `Checkers.Web` |
| 7 | **Threaded web engine.** New `Checkers.Web.Engine` project (`WasmEnableThreads`, AOT in CI) served from `/engine/_framework/`, booted on the Window by `engine-client.js` with 16 pthreads; async `[JSExport]`s, queued progress, direct token cancellation. Falls back to the existing single-threaded worker. `coi-serviceworker.js`, `.mjs` MIME type and `/engine/*` fallback exclusion; the deploy workflow publishes and copies the engine. | `src/Checkers.Web.Engine/*`, `src/Checkers.Web/wwwroot/*`, `.github/workflows/azure-static-web-apps.yml` |
| 8 | **Docs.** Chapters 07 (Lazy SMP section), 08 (lock-free layout, diagram), 12 (Milestone 10, commands), 13 and 14. | `docs/brain/*` |

## Gates

### G0 — the 1-thread search is unchanged

- `signature` (160 fixed-depth searches: 40 positions × 2 variants × no TT / fresh 1M TT) is **identical** to a benchmark build of `main` from before the change at calibrated depth and at calibrated depth + 2 (72,570,162 nodes): nodes, move and score of every search match.
- Speed: depth + 2 signature, best of 10 interleaved runs, **5,131 ms new vs 5,251 ms old** (about 3% faster; NegaMax lost parameters when it moved into `SearchWorker`).

### G1 — correctness

- Core tests 135/135 pass, including the new `LazySmpTests` (TT stress, best move with 2/4/8 threads, node reporting, Move Now / cancel / hard limit, custom evaluator on one thread).
- App tests: all new settings tests pass. Two tests fail on Linux on `main` as well (`OpenCommand_LoadsGameAndRestoresSettings`, `SaveCommand_WhenFileNameEmpty_PromptsForPathAndUpdatesTitle`: they expect a Windows path in the title); unrelated to this work.

### G2 — time to depth (`smp-bench`, informational)

40 International positions at calibrated depth + 4, fresh 1M TT, minimum of 3 interleaved passes:

| Threads | Time to depth | Speedup | Nodes vs 1T | NPS |
|---:|---:|---:|---:|---:|
| 1 | 2,065 ms | 1.00× | 1.00× | 9.3M |
| 2 | 1,513 ms | 1.36× | 1.38× | 17.6M |
| 4 | 1,093 ms | 1.89× | 1.90× | 33.5M |
| 8 | 858 ms | 2.41× | 2.61× | 58.7M |
| 16 | 702 ms | 2.94× | 3.13× | 86.0M |

Gomoku reached about 1.5× at 4 threads; Checkers scales better.

### G3 — matches (`smp-match`, same build, 1 thread = old engine)

200 balanced International ballots × 2 colours = 400 games, 500 ms/move, own 1M TT per side:

| Match | Games at once | Score | W / D / L | 95% lower bound | Elo (± 95%) | Avg depth | NPS |
|---|---:|---:|---:|---:|---:|---:|---:|
| 2T vs 1T | 12 | 210.0 / 400 (52.5%) | +79 = 262 −59 | 49.6% | +17.4 ± 20.0 | 20.99 vs 20.52 | 7.9M vs 4.0M |
| **4T vs 1T (gate)** | 5 | 216.0 / 400 (**54.0%**) | +85 = 262 −53 | **51.1%** | **+27.9 ± 20.0** | 22.70 vs 21.72 | 19.7M vs 5.2M |
| 8T vs 1T | 3 | 242.5 / 400 (60.6%) | +125 = 235 −40 | 57.7% | +75.0 ± 21.7 | 22.34 vs 21.06 | 35.1M vs 5.0M |

**Gate passed** (4T lower bound 51.1% > 50%). 2T is ahead but not significant at 400 games. JSON/Markdown per match in `Plans/lazy-smp-results/`.

The optional variants (Stockfish skip-depth table, adopting a deeper helper result) were not tried; the baseline diversification already passes the gate.

### G4 — browser

Dev server, headless Chromium (Playwright), AOT-compiled engine, 3 s/move from the start position:

| Threads | NPS | Depth |
|---|---:|---:|
| 1 | 4.6M | 19 |
| 4 | 15.2M | 20 |
| Auto (4) | 16.6M | 21 |
| 8 | 29.1M | 21 |

- The page is `crossOriginIsolated` and logs `[Checkers.Engine] Multithreaded engine started (...)`.
- Move Now answers in about 0.1 s with 4 threads; New Game during a search works.
- With the service worker blocked (no isolation) the single-threaded worker takes over and the Threads choice is disabled with a note.
- The docs pages still render under isolation (KaTeX, Mermaid, images).

## Problems found on the way

- **Service-worker scope.** `coi-serviceworker.js` must be in the site root: a service worker only controls pages below its own folder. Gomoku has it in `js/`, where it never controls the page; it only works there because Azure sends the COOP/COEP headers itself.
- **Broken responses from the dev server.** Copying the engine's precompressed `.br`/`.gz` files into the app's `wwwroot` made the dev server send broken response bodies. The build now copies only uncompressed files, refreshes the Content items and lets the app compress them.
- **Pruning at low depth.** At depth 9 the 1-thread search can prefer a move that multi-PV shows is 184 cp worse, so the SMP best-move tests compare against `SearchMultiPv` at depth 10 instead of the plain search.
