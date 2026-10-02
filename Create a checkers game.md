# Checkers Game - Project Specification & Roadmap

## 1. Overview & Vision
The goal is to develop an extensible, high-performance Checkers (Draughts) application in C# and .NET 10. 

The project follows **Clean Architecture** to ensure complete decoupling between the game rules, board state, AI engines, application coordination, and presentation layers.
- **Engine Library:** High-performance, zero-UI class library (`Checkers.Core`).
- **Shared App Layer:** Shared ViewModels, game loop, file persistence, and settings (`Checkers.App`), shared between all clients.
- **Primary Desktop Client:** Modern .NET 10 WPF desktop application (`Checkers.Wpf`) featuring:
  - 60 FPS board rendering with multi-jump path and capture crosshair visualizations.
  - Full Game Save / Load system compatible with standard Draughts notation and PDN metadata tags.
  - Live Analysis telemetry pane (Move, Depth, Value, Best Move, Nodes, Evaluations, Time) matching Stello.
  - Computer Settings modal dialog and time controls matching Stello and Connect-4 (`Fixed depth`, `Time per move`, `Time per game`).
- **Web Client:** Blazor WebAssembly application (`Checkers.Web`) with Ahead-Of-Time (AOT) compilation, sharing the exact same core engine and app layer.
- **Deployment:** Automated CI/CD deploying the Blazor WebAssembly static files to **Azure Static Web Apps** via GitHub Actions, following the deployment method in `C:\Udvikling\Spil\Stello\.github\workflows\azure-static-web-apps.yml`.
- **Documentation:** Comprehensive architectural and engine documentation in `docs/brain/`, following the numbered chapter format and technical depth of `C:\Udvikling\Spil\Connect-4\docs\brain` and `C:\Udvikling\Spil\Stello\docs\brain`. The documentation is served directly inside the web client via Markdig markdown rendering.

Design and user interface inspiration for desktop and web clients is drawn from the existing game projects in `C:\Udvikling\Spil\Stello` and `C:\Udvikling\Spil\Connect-4`.

---

## 2. Game Rules & Mechanics

The game implements an **8x8 Draughts variant combining English capture rules with International Flying Kings**:

### 2.1 Board & Pieces
* **Board:** 8x8 grid (64 squares total; 32 active dark squares).
* **Pieces:** 12 White (Light) pieces vs 12 Black (Dark) pieces.
* **Initial Setup:** Pieces occupy the dark squares of rows 0–2 (Black, Draughts squares 1–12) and rows 5–7 (White, Draughts squares 21–32).
* **Active Player:** White moves first.

### 2.2 Movement Rules
* **Regular Men:**
  * Move forward diagonally by 1 square into an unoccupied dark square.
  * *Captures:* Jump forward diagonally over an adjacent opponent piece into the immediate vacant square behind it.
* **Kings:**
  * Crowned when a regular man reaches the furthest opponent rank.
  * **Flying Kings (International style):** Can move any number of vacant squares diagonally in all 4 directions (forward and backward).
  * **Flying King Captures:** Can jump over an opponent piece at any distance along an open diagonal and land on *any* vacant square beyond it along the same diagonal line.

### 2.3 Captures & Multi-Jumps
* **Strict Mandatory Captures (Forced Jumps):**
  * If one or more capture moves are available on a player's turn, the player **must** make a capture move (regular non-capturing moves are illegal).
  * **Free Choice:** If multiple capture options exist (from the same or different pieces), the player may choose which capture line to initiate.
* **Multi-Jump Sequences:**
  * If a piece makes a jump and has further valid jumps available from its new position, it **must** continue jumping in the same turn until no further captures remain.
  * A captured piece is removed from the board (captured pieces cannot be jumped twice in a single sequence).
* **Promotion Rule:**
  * When a regular man reaches the furthest rank (crown row), it immediately promotes to a King and its turn ends (any further jumping for that piece must wait for subsequent turns).

### 2.4 End Game Conditions
* **Win:** The opponent has no pieces remaining, or has no legal moves remaining on their turn (completely blocked).
* **Draw:**
  * Threefold repetition of the same board state and turn (tracked via deterministic Zobrist hashing).
  * 40-move rule without captures or promotions (80 half-moves).
  * Mutual agreement.

### 2.5 Game Record & Persistence
* **Format:** Standard Draughts notation (`21-17`, `26x17`, or multi-jump routes `2x16x26x17x3`) with optional metadata headers:
  * `[GameMode "HumanVsComputer"]`
  * `[TimeControlMode "TimePerGame"]`
  * `[Depth "8"]`
  * `[SecondsPerMove "5"]`
  * `[MinutesPerGame "5"]`
* **Parser:** Tokenizes standard moves, handles move numbers (`1.`, `2.`), semicolon/brace comments, and tags. Backwards-compatible with legacy `[Difficulty ...]` records.
* **File Operations:** Standard `Open...`, `Save`, and `Save As...` with dirty tracking and window title integration.

---

## 3. System Architecture & Solution Structure

Following the established architecture of `Stello` and `Connect-4`:

```
Checkers/
├── .github/
│   └── workflows/
│       └── azure-static-web-apps.yml  # CI/CD: build .NET 10, wasm-tools, AOT, deploy to Azure SWA
│
├── docs/
│   └── brain/                         # Numbered markdown chapters for engine and architecture
│       ├── README.md                  # The Checkers brain on one page + chapter index
│       ├── 01-overview.md             # Projects, core abstractions, move lifecycle
│       ├── 02-board-and-coordinates.md# 8x8 grid, 1..32 Draughts notation, Zobrist hashing
│       ├── 03-rules-and-move-generation.md # Slides, jumps, flying kings, multi-jumps, forced captures
│       ├── 04-game-record.md          # GameSession, undo/redo, move history notation, draw rules
│       ├── 05-evaluation.md           # Material weights, center control, advancement, king safety
│       ├── 06-move-ordering.md        # Captures first, promotions, PV ordering
│       ├── 07-search.md               # Minimax, Alpha-Beta pruning, iterative deepening
│       ├── 08-transposition-table.md  # 64-bit Zobrist key, entry flags, replacement policy
│       ├── 09-endgame-solver.md       # Solved small-piece endgames / endgame heuristics
│       ├── 10-time-control.md         # Move timers, soft/hard time limits, chess clocks
│       ├── 11-opening-book.md         # Opening repertoire / book lookup
│       ├── 12-app-integration.md      # Game loop, threading, async AI, settings
│       ├── 13-glossary.md             # Checkers terminology and concepts
│       └── 14-references.md           # Literature, rulesets, and algorithms
│
├── src/
│   ├── Checkers.Core/                 # Pure Game Engine & Logic (.NET 10)
│   │   ├── Models/                    # Piece, Position, Move, BoardState, GameStatus
│   │   ├── Engine/                    # RuleEngine, Zobrist, GameSession, GameRecordFormat
│   │   └── AI/                        # IPlayer, MinimaxPlayer, EvaluationFunction, SearchLimits, SearchAnalysis
│   │
│   ├── Checkers.App/                  # Shared Application Layer (.NET 10)
│   │   ├── Models/                    # GameSettings, GameMode, GameFile
│   │   ├── ViewModels/                # MainViewModel, SquareViewModel, AnalysisViewModel, SettingsViewModel
│   │   ├── Services/                  # ISoundService, IDialogService, IGameFileService
│   │   └── Documentation/             # BrainDocs parser & Markdig chapter loader
│   │
│   ├── Checkers.Wpf/                  # Desktop Presentation Layer (.NET 10 WPF)
│   │   ├── Views/                     # MainWindow, BoardView, SettingsWindow
│   │   ├── Services/                  # WpfDialogService, WpfGameFileService, SoundPlayerService
│   │   └── Converters/                # Value converters for XAML bindings
│   │
│   └── Checkers.Web/                  # Web Presentation Layer (Blazor WebAssembly .NET 10)
│       ├── Pages/                     # Home.razor (Game UI), Docs.razor (Markdig docs viewer)
│       ├── Components/                # BoardComponent, MoveHistoryPanel, GameControls
│       └── wwwroot/                   # CSS, audio, icons, static web assets
│
└── tests/
    ├── Checkers.Core.Tests/           # 54 Unit tests: Rules, Moves, Kings, AI, Time Limits
    └── Checkers.App.Tests/            # 32 Unit tests: ViewModels, Session, File I/O, Settings, Clocks
```

### 3.1 `Checkers.Core` (Engine & Logic)
* **Zero UI Dependencies:** Pure C# 13 / .NET 10 base class libraries only.
* **Deterministic & Pure:** State transitions produce verifiable, cloneable states.
* **Key Components:**
  * `IRuleEngine`: Generates all legal moves (including mandatory captures and jump chains), validates moves, applies moves, and detects terminal states.
  * `IPlayer`: Player abstraction implemented by `RandomPlayer` and `MinimaxPlayer`.
  * `SearchLimits`: Encapsulates search bounds (`FixedDepth`, `TimePerMove`, `TimePerGame`).
  * `MinimaxPlayer`: Alpha-Beta search with **Iterative Deepening**, PV move ordering, soft time limits (2/3 of budget), and hard node interrupts.
  * `GameRecordFormat`: Formats and parses text move lists with metadata tags.

### 3.2 `Checkers.App` (Shared Application Layer)
* Shared between `Checkers.Wpf` and `Checkers.Web`.
* Hosts the MVVM ViewModels (`MainViewModel`, `AnalysisViewModel`, `SettingsViewModel`).
* Asynchronous AI game loop with background task execution and cancellation support.
* Chess clock management: tracks remaining time per game, allocates move budgets, and refunds clock time on `Undo`.
* Abstractions for sounds (`ISoundService`), dialogs (`IDialogService`), and storage (`IGameFileService`).

### 3.3 `Checkers.Wpf` (Desktop UI)
* Native Windows desktop UI using WPF and modern clean styling.
* 60 FPS responsive animations, drag-and-drop, click-to-move, route hover previews, and procedural sound effects.
* Dedicated **Analysis** pane displaying engine telemetry.
* **Settings Window** dialog with radio toggles and sliders matching Stello and Connect-4.
* Visual indicators: mode and settings badges, chess clock countdown, and thinking progress bar.

### 3.4 `Checkers.Web` (Blazor WebAssembly)
* High-performance browser client using Blazor WebAssembly targeting .NET 10 with Ahead-Of-Time (AOT) compilation enabled for Release builds (`<RunAOTCompilation>true</RunAOTCompilation>`).
* Responsive board design inspired by Stello and Connect-4.
* Integrated `/docs` route rendering `docs/brain` markdown chapters using Markdig.
* Targets Azure Static Web Apps with automated GitHub Actions deployment.

---

## 4. Artificial Intelligence & Computer Settings

### 4.1 Computer Options Dialog (Matching Stello & Connect-4)
Configurable via `Game -> Settings...` dialog and reflected in the sidebar:
1. **Fixed depth**:
   - Slider: 1 to 20 plies (default: 8 plies).
   - Searches to a fixed ply depth using iterative deepening.
2. **Time per move**:
   - Slider: 1 to 60 seconds (default: 5 seconds).
   - Soft limit: searches up to 2/3 of budget before starting a new depth ply.
   - Hard limit: aborts search mid-ply if time budget expires (checked every 512 nodes).
3. **Time per game**:
   - Slider: 1 to 60 minutes (default: 5 minutes).
   - Allocates remaining clock dynamically across estimated remaining moves.
   - Live chess clock countdown displayed in header banner and sidebar.
   - Undoing moves restores elapsed clock time at that ply.

### 4.2 Search Engine Architecture (`MinimaxPlayer`)
* **Algorithm:** Minimax with Alpha-Beta pruning ($\alpha$-$\beta$).
* **Iterative Deepening:** Explores depths $1, 2, \dots, D$, maintaining the Principal Variation (PV) best move from the previous iteration to seed move ordering for the next depth.
* **Evaluation Heuristics ($H(s)$):**
  * Material balance: Men = 100 pts, Kings = 300 pts.
  * Central control: Bonus for dark squares in columns 2–5 and rows 2–5.
  * Advancement: Tiered bonuses rewarding regular men for advancing toward the crown row.
  * Home row defense: Retaining back-rank men to prevent opponent kinging.
  * Trapped piece penalties: Pieces with 0 legal exits.
* **Telemetry Output (`SearchAnalysis`):**
  * Move, Depth, Value, Best Move, Nodes Evaluated, Leaf Evaluations, and Search Time.

### 4.3 Transposition Table (Zobrist Hashing) & Empirical Benchmark Suite
To accelerate search depth and eliminate duplicate subtree evaluations across branches and iterative deepening passes:
* **Table Design:**
  * 64-bit Zobrist key hashing for every dark square and piece configuration.
  * Entry schema: 64-bit key, search depth, evaluation score, bound flag (`Exact`, `LowerBound` / $\beta$-cutoff, `UpperBound` / $\alpha$-fail-low), best move, and generation age.
  * Configurable table size (e.g. 16 MB to 64 MB, defaulting to 32 MB).
  * Replacement strategy: Two-tier or depth-preferred with age aging to preserve critical deep evaluations.

* **40-Position Empirical Benchmark Methodology:**
  To scientifically quantify and verify the real-world efficiency gains of the Transposition Table, an automated empirical benchmark suite will be executed:
  1. **Test Set Generation (40 Positions via Algorithmic Sampling):**
     * Generated by sampling distinct, legal board states from diverse self-play games under the International Flying Kings ruleset:
       - **10 Opening / Early-Game Positions** (moves 4–12, high piece density, quiet maneuvering).
       - **15 Tactical Middlegame Positions** (complex board states featuring multiple capture branches, multi-jumps, and king promotions).
       - **10 Late Middlegame / Endgame Configurations** (reduced piece counts, flying kings vs men, open diagonals).
       - **5 Blockades & Tactical Tension States** (critical positions testing quiescence and search cutoffs).
  2. **Dedicated Benchmark Runner & Tooling:**
     * Implemented as an independent CLI / benchmark runner command (e.g., `dotnet run --project ... --benchmark-tt`), exporting structured benchmark JSON results (`tt_benchmark_results.json`).
     * Keeps the primary test suite (`dotnet test Checkers.slnx`) blazing fast (< 1 second) while enabling reproducible on-demand benchmarking.
  3. **Baseline Calibration (Without Transposition Table):**
     * For each test position $P_i$ ($i = 1 \dots 40$), the runner calibrates a search depth $D_i$ such that search execution completes in $\le 10$ seconds (targeting a meaningful search workload, typically 2–8 seconds per position).
     * The calibrated results are persisted in a baseline JSON file:
       - Position index & FEN / board notation
       - Calibrated depth $D_i$
       - Baseline nodes evaluated ($N_{\text{baseline}}$)
       - Baseline execution time ($T_{\text{baseline}}$ in ms)
       - Best move found and evaluation score (establishing ground-truth baseline).
  4. **Comparative Evaluation (With Transposition Table Enabled):**
     * Re-runs each position $P_i$ at its saved calibrated depth $D_i$ with the Transposition Table active.
     * Records:
       - TT nodes evaluated ($N_{\text{TT}}$)
       - TT search time ($T_{\text{TT}}$ in ms)
       - Best move found and score (verifies search consistency and absence of score corruption).
       - Telemetry: Cache hits (exact cutoffs, $\alpha$/$\beta$ bounds), hash collisions, and table fill rate.
     * Computes efficiency metrics:
       - Node Reduction %: $\frac{N_{\text{baseline}} - N_{\text{TT}}}{N_{\text{baseline}}} \times 100\%$
       - Speedup Factor: $\frac{T_{\text{baseline}}}{T_{\text{TT}}}$
  5. **Documentation in `docs/brain`:**
     * The full methodology, position dataset, comparative tables, and cache utilization analysis will be published directly in `docs/brain/08-transposition-table.md`.

### 4.4 Future Engine Optimizations
* **Quiescence Search:** Extend capture lines past search horizon to avoid the horizon effect.
* **Opening Book:** Pre-calculated opening repertoire lookup.

---

## 5. Iterative Implementation Plan

| Phase | Milestone | Deliverables | Status |
| :--- | :--- | :--- | :--- |
| **Phase 1** | **Core Domain & Rules Engine** | • Board representation & coordinates<br>• Move generator (regular, jumps, multi-jump chains)<br>• Flying King logic & mandatory jump enforcement<br>• Terminal condition detection (win/loss/draw)<br>• 100% test coverage on move edge cases (39 tests) | **COMPLETED** |
| **Phase 1.5** | **Engine Documentation (`docs/brain`)** | • Create `docs/brain/` structure matching Stello & Connect-4<br>• `README.md` (The Checkers brain on one page)<br>• `01-overview.md` (Architecture, life of a move)<br>• `02-board-and-coordinates.md` (Coordinates, 1..32 notation, Zobrist)<br>• `03-rules-and-move-generation.md` (Rules, flying kings, captures)<br>• `04-game-record.md` (GameSession, history, draw rules) | **COMPLETED** |
| **Phase 2** | **Shared App Layer & WPF Desktop GUI** | • `Checkers.App` shared ViewModels & game coordinator<br>• `Checkers.Wpf` desktop application<br>• Responsive 8x8 checkerboard with piece rendering<br>• Click-to-move, drag-and-drop & visual move indicator highlights<br>• Flying King capture path preview & target crosshairs<br>• Turn management, Undo/Redo, and sound effects | **COMPLETED** |
| **Phase 3** | **AI Opponent, Analysis & Game Records** | • `IPlayer` abstraction & `MinimaxPlayer` with heuristics<br>• Non-blocking asynchronous AI turns with cancellation<br>• Real-time **Analysis Pane** (Move, Depth, Score, Best Move, Nodes, Time) matching Stello<br>• Full **Save & Load** system (PDN-compatible format and tags)<br>• Document chapters `05-evaluation.md`, `06-move-ordering.md`, `07-search.md` | **COMPLETED** |
| **Phase 4** | **Computer Settings, Time Controls & Engine Polish** | • **Computer Settings Dialog** matching Stello & Connect-4 (Fixed depth, Time per move, Time per game) — *Implemented*<br>• **Chess clock / time management** with iterative deepening & clock countdown/refund on undo — *Implemented*<br>• Transposition table (64-bit Zobrist hashing, replacement scheme, entry flags)<br>• **40-position empirical benchmark** (10s baseline calibration, node count & time comparison, correctness validation)<br>• Quiescence search & capture chain extension<br>• Document chapters `08-transposition-table.md` (with benchmark results), `10-time-control.md` | **In Progress** |
| **Phase 5** | **Blazor WebAssembly Client & Deployment** | • `Checkers.Web` project with .NET 10 WebAssembly AOT<br>• Responsive Web board UI inspired by Stello and Connect-4<br>• Integrated `/docs` viewer rendering `docs/brain` using Markdig<br>• CI/CD pipeline: `.github/workflows/azure-static-web-apps.yml`<br>• Automated deployment to Azure Static Web Apps | Planned |

---

## 6. Current Quality & Test Metrics

- **Platform:** .NET 10 (C# 13)
- **Compiler Warnings:** 0 warnings across all projects (Debug & Release builds)
- **Total Automated Unit Tests:** 86 tests (100% pass rate)
  - `Checkers.Core.Tests`: 54 passing tests
  - `Checkers.App.Tests`: 32 passing tests