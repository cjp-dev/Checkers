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

The game implements two selectable **8x8 Draughts variants**, configurable via the Settings dialog:
1. **International Draughts (Flying Kings):** Men move/jump forward; Kings fly along open diagonals and can jump from afar.
2. **English Checkers (American Draughts / Straight Checkers):** Men move/jump forward; Kings move and jump strictly 1 square / 1 hop in all 4 diagonal directions.

### 2.1 Board & Pieces
* **Board:** 8x8 grid (64 squares total; 32 active dark squares).
* **Pieces:** 12 White (Light) pieces vs 12 Black (Dark) pieces.
* **Initial Setup:** Pieces occupy the dark squares of rows 0–2 (Black, Draughts squares 1–12) and rows 5–7 (White, Draughts squares 21–32).
* **Active Player:** White moves first.

### 2.2 Movement Rules

#### Regular Men (Both Variants)
* Move forward diagonally by 1 square into an unoccupied dark square.
* *Captures:* Jump forward diagonally over an adjacent opponent piece into the immediate vacant square behind it. Men cannot move or jump backward in either variant.

#### Kings: International Draughts (Flying Kings)
* **Movement:** Move any number of vacant squares diagonally in all 4 directions (forward and backward).
* **Captures:** Jump over an opponent piece at any distance along an open diagonal and land on *any* vacant square beyond it along the same diagonal line.

#### Kings: English Checkers (American Draughts)
* **Movement:** Move exactly 1 square diagonally in all 4 directions (forward and backward). Not a flying king.
* **Captures:** Jump over an adjacent opponent piece (distance 1) and land on the *immediate* vacant square behind it (distance 2) in all 4 diagonal directions (forward and backward).
* **Multi-Jumps:** If further jumps are available from the landing square in any of the 4 directions, the King must continue jumping in the same turn.

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
  * `[Variant "English"]` or `[Variant "International"]`
  * `[GameMode "HumanVsComputer"]`
  * `[TimeControlMode "TimePerGame"]`
  * `[Depth "8"]`
  * `[SecondsPerMove "5"]`
  * `[MinutesPerGame "5"]`
* **Parser:** Tokenizes standard moves, handles move numbers (`1.`, `2.`), semicolon/brace comments, and tags. Automatically configures the game session rule engine to the saved variant. Backwards-compatible with legacy records (omitted variant defaults to International).
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
    ├── Checkers.Core.Tests/           # 66 Unit tests: Rules, Moves, Kings, AI, Quiescence, TT, Telemetry
    └── Checkers.App.Tests/            # 34 Unit tests: ViewModels, Session, File I/O, Settings, Clocks, Live Analysis
```

### 3.1 `Checkers.Core` (Engine & Logic)
* **Zero UI Dependencies:** Pure C# 13 / .NET 10 base class libraries only.
* **Deterministic & Pure:** State transitions produce verifiable, cloneable states.
* **Key Components:**
  * `IRuleEngine`: Generates all legal moves (including mandatory captures and jump chains), validates moves, applies moves, and detects terminal states.
  * `IPlayer`: Player abstraction implemented by `RandomPlayer` and `MinimaxPlayer`.
  * `SearchLimits`: Encapsulates search bounds (`FixedDepth`, `TimePerMove`, `TimePerGame`).
  * `MinimaxPlayer`: Alpha-Beta search with **Iterative Deepening**, 64-bit Zobrist Transposition Table, Quiescence search, PV move ordering, soft time limits (2/3 of budget), hard node interrupts, and real-time `IProgress<SearchAnalysis>` telemetry streaming.
  * `GameRecordFormat`: Formats and parses text move lists with metadata tags.

### 3.2 `Checkers.App` (Shared Application Layer)
* Shared between `Checkers.Wpf` and `Checkers.Web`.
* Hosts the MVVM ViewModels (`MainViewModel`, `AnalysisViewModel`, `SettingsViewModel`).
* Asynchronous AI game loop with background task execution, cancellation support, and live `IProgress<SearchAnalysis>` reporting directly updating the Analysis pane during active search.
* Chess clock management: tracks remaining time per game, allocates move budgets, and refunds clock time on `Undo`.
* Abstractions for sounds (`ISoundService`), dialogs (`IDialogService`), and storage (`IGameFileService`).

### 3.3 `Checkers.Wpf` (Desktop UI)
* Native Windows desktop UI using WPF and modern clean styling.
* 60 FPS responsive animations, drag-and-drop, click-to-move, route hover previews, and procedural sound effects.
* Dedicated **Analysis** pane displaying engine telemetry (Move, Depth, Value, Best Move, Nodes, Evaluations, Time) updated live while the computer thinks (matching Stello and Connect-4).
* **Settings Window** dialog with radio toggles and sliders matching Stello and Connect-4.
* Visual indicators: mode and settings badges, chess clock countdown, and thinking progress bar.

### 3.4 `Checkers.Web` (Blazor WebAssembly)
* High-performance browser client using Blazor WebAssembly targeting .NET 10 with Ahead-Of-Time (AOT) compilation enabled for Release builds (`<RunAOTCompilation>true</RunAOTCompilation>`).
* Responsive board design inspired by Stello and Connect-4.
* Integrated `/docs` route rendering `docs/brain` markdown chapters using Markdig.
* Targets Azure Static Web Apps with automated GitHub Actions deployment.

---

## 4. Artificial Intelligence & Computer Settings

### 4.1 Computer Options & Variant Settings Dialog (Matching Stello & Connect-4)
Configurable via `Game -> Settings...` dialog and reflected in the sidebar:
1. **Checkers Variant**:
   - Radio selection: **International Draughts (Flying Kings)** vs **English Checkers (1-step Kings)**.
   - Configures the core rule engine, move generator, king behaviors, and evaluation heuristics.
2. **Fixed depth**:
   - Slider: 1 to 20 plies (default: 8 plies).
   - Searches to a fixed ply depth using iterative deepening.
3. **Time per move**:
   - Slider: 1 to 60 seconds (default: 5 seconds).
   - Soft limit: searches up to 2/3 of budget before starting a new depth ply.
   - Hard limit: aborts search mid-ply if time budget expires (checked every 512 nodes).
4. **Time per game**:
   - Slider: 1 to 60 minutes (default: 5 minutes).
   - Allocates remaining clock dynamically across estimated remaining moves.
   - Live chess clock countdown displayed in header banner and sidebar.
   - Undoing moves restores elapsed clock time at that ply.
5. **Transposition Table Size**:
   - Power-of-two capacity slider: **$1,048,576$ entries ($2^{20}$, default)** up to **$16,777,216$ entries ($2^{24}$, matching Connect-4)**.
   - Persisted in PDN game record headers (`[TranspositionTableEntries "..."]`).

### 4.2 Search Engine Architecture (`MinimaxPlayer`)
* **Algorithm:** Minimax with Alpha-Beta pruning ($\alpha$-$\beta$).
* **Iterative Deepening:** Explores depths $1, 2, \dots, D$, maintaining the Principal Variation (PV) best move from the previous iteration to seed move ordering for the next depth.
* **Evaluation Heuristics ($H(s)$):**
  * Material balance: Men = 100 pts. Kings: International = 300 pts (long-range flying diagonals); English = 170 pts (localized short-range power).
  * Central control: Bonus for dark squares in columns 2–5 and rows 2–5.
  * King Centralization: In English Checkers, kings holding center squares receive an active bonus (+10 pts) reflecting short-range endgame dominance.
  * Advancement: Tiered bonuses rewarding regular men for advancing toward the crown row.
  * Home row defense: Retaining back-rank men to prevent opponent kinging.
  * Trapped piece penalties: Pieces with 0 legal exits.
* **Live Search Telemetry Output (`SearchAnalysis`):**
  * Streams real-time updates via `IProgress<SearchAnalysis>` while the computer thinks:
    - At every completed iterative deepening depth.
    - Periodic heartbeat every 150–200 ms during deep evaluations (yielding to browser dispatcher in Blazor WebAssembly).
    - Instantly on single forced moves.
  * Fields: Move, Depth, Value, Best Move, Nodes Evaluated, Leaf Evaluations, and Search Time.

### 4.3 Transposition Table (Zobrist Hashing) & Empirical Benchmark Suite
To accelerate search depth and eliminate duplicate subtree evaluations across branches and iterative deepening passes:
* **Table Design:**
  * 64-bit Zobrist key hashing for every dark square and piece configuration.
  * Compact **16-byte `TranspositionEntry` struct**: 64-bit key (8B), 32-bit score (4B), 8-bit depth (1B), packed 2-bit bound + 6-bit generation age (1B), and encoded 6-bit `BestMoveFrom` and `BestMoveTo` coordinates (2B).
  * Configurable table size from **$1,048,576$ entries ($2^{20}$, 16 MiB default)** up to **$16,777,216$ entries ($2^{24}$, 256 MiB max)** with automatic fallback if memory allocation fails.
  * Replacement strategy: Depth-preferred with generation aging (`NewSearch()`) to preserve critical deep evaluations while replacing stale entries from earlier moves.

* **40-Position Empirical Benchmark Methodology:**
  To scientifically quantify and verify the real-world efficiency gains of the Transposition Table and compare Default ($1,048,576$) vs Max ($16,777,216$) capacities, an automated empirical benchmark suite is executed:
  1. **Test Set Generation (40 Positions via Algorithmic Sampling):**
     * Generated by sampling distinct, legal, non-trivial board states from diverse self-play games under the International Flying Kings ruleset:
       - **10 Opening / Early-Game Positions** (moves 4–12, high piece density, quiet maneuvering).
       - **15 Tactical Middlegame Positions** (complex board states featuring multiple capture branches, multi-jumps, and king promotions).
       - **10 Late Middlegame / Endgame Configurations** (reduced piece counts, flying kings vs men, open diagonals).
       - **5 Blockades & Tactical Tension States** (critical positions testing quiescence and search cutoffs).
  2. **Dedicated Benchmark Runner & Tooling:**
     * Implemented as an independent CLI / benchmark runner command (`tools/Checkers.Benchmark`), exporting structured benchmark JSON results (`tt_benchmark_results.json`).
     * Keeps the primary test suite (`dotnet test Checkers.slnx`) blazing fast (< 1.5 seconds) while enabling reproducible on-demand benchmarking.
  3. **Baseline Calibration (Without Transposition Table):**
     * For each test position $P_i$ ($i = 1 \dots 40$), the runner calibrates a search depth $D_i$ (7–9 plies) such that search execution completes in $\le 10$ seconds.
  4. **Comparative Evaluation (Default TT $1,048,576$ vs Max TT $16,777,216$):**
     * Re-runs each position $P_i$ at its calibrated depth $D_i$ with both the Default TT ($1,048,576$ entries) and Max TT ($16,777,216$ entries).
     * **Empirical Results Summary:**
       - **Node Reduction:** `2,408,731` baseline nodes $\rightarrow$ `711,546` Default TT nodes / `711,485` Max TT nodes (**70.5% overall node reduction**).
       - **Speedup:** `3,855 ms` baseline time $\rightarrow$ `1,198 ms` Default TT time (**3.22x overall speedup**, peak **12.25x speedup** on Position 7).
       - **Collision Reduction ($1\text{M}$ vs $16\text{M}$):** `608` collisions at $1,048,576$ entries $\rightarrow$ `36` collisions at $16,777,216$ entries (**94.1% collision reduction**).
  5. **Documentation in `docs/brain`:**
     * The full methodology, position dataset, comparative tables, and cache utilization analysis are published in `docs/brain/08-transposition-table.md`.

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
| **Phase 4** | **Computer Settings, Time Controls & Engine Polish** | • **Computer Settings Dialog** matching Stello & Connect-4 (Fixed depth, Time per move, Time per game, Transposition Table size $1\text{M}$–$16\text{M}$ entries)<br>• **Chess clock / time management** with iterative deepening & clock countdown/refund on undo<br>• **Transposition table** (64-bit Zobrist hashing, 16-byte compact entry, replacement scheme)<br>• **40-position empirical benchmark** (70.5% node reduction, 3.22x speedup, 94.1% fewer collisions at 16M)<br>• **Quiescence search** & capture chain extension<br>• Document chapters `08-transposition-table.md`, `10-time-control.md` | **COMPLETED** |
| **Phase 5** | **Blazor WebAssembly Client & Deployment** | • `Checkers.Web` project with .NET 10 WebAssembly AOT<br>• Responsive Web board UI inspired by Stello and Connect-4<br>• Integrated `/docs` viewer rendering `docs/brain` using Markdig<br>• CI/CD pipeline: `.github/workflows/azure-static-web-apps.yml`<br>• Automated deployment to Azure Static Web Apps | **COMPLETED** |
| **Phase 6** | **Multi-Variant Support (English Checkers & International Flying Kings)** | • `CheckersVariant` domain enum (`International`, `English`)<br>• 1-step King move generation & 4-direction single-hop multi-jumps in `RuleEngine`<br>• Variant-aware `EvaluationFunction` (King values 300 vs 170, King centralization)<br>• Radio button variant selection in desktop `SettingsWindow.xaml` & web `DialogHost.razor`<br>• PDN `[Variant ...]` tag serialization & deserialization<br>• Full unit test coverage for English Checkers rules & moves | **COMPLETED** |

---

## 6. Current Quality & Test Metrics

- **Platform:** .NET 10 (C# 13)
- **Compiler Warnings:** 0 warnings across all projects (Debug & Release builds)
- **Total Automated Unit Tests:** 118 tests (100% pass rate)
  - `Checkers.Core.Tests`: 78 passing tests
  - `Checkers.App.Tests`: 40 passing tests