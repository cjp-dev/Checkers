# Checkers Game - Project Specification & Roadmap

## 1. Overview & Vision
The goal is to develop an extensible, high-performance Checkers (Draughts) application in C# and .NET 10. 

The project follows **Clean Architecture** to ensure complete decoupling between the game rules, board state, AI engines, application coordination, and presentation layers.
- **Engine Library:** High-performance, zero-UI class library (`Checkers.Core`).
- **Shared App Layer:** Shared ViewModels, game loop, and settings (`Checkers.App`), shared between all clients.
- **Primary Desktop Client:** Modern .NET 10 WPF desktop application (`Checkers.Wpf`).
- **Web Client:** Blazor WebAssembly application (`Checkers.Web`) with Ahead-Of-Time (AOT) compilation, sharing the exact same core engine and app layer.
- **Deployment:** Automated CI/CD deploying the Blazor WebAssembly static files to **Azure Static Web Apps** via GitHub Actions, following the deployment method in `C:\Udvikling\Spil\Stello\.github\workflows\azure-static-web-apps.yml`.
- **Documentation:** Comprehensive architectural and engine documentation in `docs/brain/`, following the numbered chapter format and technical depth of `C:\Udvikling\Spil\Connect-4\docs\brain` and `C:\Udvikling\Spil\Stello\docs\brain`. The documentation is served directly inside the web client via Markdig markdown rendering.

Design and user interface inspiration for the Web client and WebAssembly integration is drawn from the existing game projects in `C:\Udvikling\Spil\Stello` and `C:\Udvikling\Spil\Connect-4`.

---

## 2. Game Rules & Mechanics

The game implements an **8x8 Draughts variant combining English capture rules with International Flying Kings**:

### 2.1 Board & Pieces
* **Board:** 8x8 grid (64 squares total; 32 active dark squares).
* **Pieces:** 12 White (Light) pieces vs 12 Black (Dark) pieces.
* **Initial Setup:** Pieces occupy the dark squares of rows 0–2 (Black) and rows 5–7 (White).

### 2.2 Movement Rules
* **Regular Men:**
  * Move forward diagonally by 1 square into an unoccupied dark square.
  * *Captures:* Jump forward diagonally over an adjacent opponent piece into the immediate vacant square behind it.
  * *(Configurable Option: Backward capture for men, standard in International Draughts).*
* **Kings:**
  * Crowned when a regular man reaches the furthest opponent rank.
  * **Flying Kings (International style):** Can move any number of vacant squares diagonally in all 4 directions (forward and backward).
  * **Flying King Captures:** Can jump over an opponent piece at any distance along an open diagonal and land on any vacant square beyond it along the same diagonal line.

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
│       ├── 06-move-ordering.md        # Captures first, promotions, killer/history heuristics
│       ├── 07-search.md               # Minimax, Alpha-Beta pruning, iterative deepening
│       ├── 08-transposition-table.md  # 64-bit Zobrist key, entry flags, replacement policy
│       ├── 09-endgame-solver.md       # Solved small-piece endgames / endgame heuristics
│       ├── 10-time-control.md         # Move timers, soft/hard time limits
│       ├── 11-opening-book.md         # Opening repertoire / book lookup
│       ├── 12-app-integration.md      # Game loop, threading, web worker / async AI, settings
│       ├── 13-glossary.md             # Checkers terminology and concepts
│       └── 14-references.md           # Literature, rulesets, and algorithms
│
├── src/
│   ├── Checkers.Core/                 # Pure Game Engine & Logic (.NET 10)
│   │   ├── Models/                    # Piece, Position, Move, BoardState, GameStatus
│   │   ├── Engine/                    # RuleEngine, Zobrist, GameSession
│   │   └── AI/                        # IPlayer, RandomPlayer, MinimaxPlayer, Evaluator
│   │
│   ├── Checkers.App/                  # Shared Application Layer (.NET 10)
│   │   ├── ViewModels/                # MainViewModel, BoardViewModel, SettingsViewModel
│   │   ├── Services/                  # GameController, AudioService, ThemeService
│   │   └── Documentation/             # BrainDocs parser & Markdig chapter loader
│   │
│   ├── Checkers.Wpf/                  # Desktop Presentation Layer (.NET 10 WPF)
│   │   ├── Views/                     # MainWindow, BoardControl, SettingsDialog, AboutDialog
│   │   ├── Controls/                  # CheckerSquare, PieceRenderer, MoveIndicator
│   │   └── Converters/                # Value converters for XAML bindings
│   │
│   └── Checkers.Web/                  # Web Presentation Layer (Blazor WebAssembly .NET 10)
│       ├── Pages/                     # Home.razor (Game UI), Docs.razor (Markdig docs viewer)
│       ├── Components/                # BoardComponent, MoveHistoryPanel, GameControls
│       └── wwwroot/                   # CSS, audio, icons, static web assets
│
└── tests/
    ├── Checkers.Core.Tests/           # Unit tests for Domain Rules, Moves, Kings, AI
    └── Checkers.App.Tests/            # Tests for ViewModels, Session flow, and Document parser
```

### 3.1 `Checkers.Core` (Engine & Logic)
* **Zero UI Dependencies:** Pure C# 13 / .NET 10 base class libraries only.
* **Deterministic & Pure:** State transitions produce verifiable, cloneable states.
* **Interfaces:**
  * `IRuleEngine`: Generates all legal moves (including mandatory captures and jump chains), validates moves, applies moves, and detects terminal states.
  * `IPlayer`: Player abstraction implemented by Human, `RandomPlayer`, and `MinimaxPlayer`.
  * `IEvaluationFunction`: Static board scoring heuristics.

### 3.2 `Checkers.App` (Shared Application Layer)
* Shared between `Checkers.Wpf` and `Checkers.Web`.
* Hosts the MVVM ViewModels (`BoardViewModel`, `GameSettingsViewModel`), the asynchronous game loop, player turn dispatching, sound cues, and document parsing for `docs/brain/`.

### 3.3 `Checkers.Wpf` (Desktop UI)
* Native Windows desktop UI using WPF and modern styling.
* 60 FPS responsive animations, drag-and-drop, click-to-move, move sound effects, and Undo/Redo controls.

### 3.4 `Checkers.Web` (Blazor WebAssembly)
* High-performance browser client using Blazor WebAssembly targeting .NET 10 with Ahead-Of-Time (AOT) compilation enabled for Release builds (`<RunAOTCompilation>true</RunAOTCompilation>`).
* Responsive board design inspired by Stello and Connect-4.
* Integrated `/docs` route rendering `docs/brain` markdown chapters using Markdig.
* Targets Azure Static Web Apps with automated GitHub Actions deployment.

---

## 4. Artificial Intelligence (AI) Roadmap

### Tier 1: Random Player (`RandomPlayer`)
* Identifies all valid moves (strictly respecting forced jumps).
* Selects one uniformly at random.
* Serves as baseline opponent and rule verification agent.

### Tier 2: Minimax with Alpha-Beta Pruning (`MinimaxPlayer`)
* **Algorithm:** Minimax search tree with $\alpha$-$\beta$ pruning.
* **Search Depth:** Configurable from Depth 2 (Beginner) to Depth 6–8 (Challenging).
* **Static Evaluation Function ($H(s)$):**
  * Material balance: Men = 100 pts, Kings = 300 pts.
  * Board positioning: Central square control bonus.
  * Advancement: Rewarding men closer to promotion.
  * Back-row defense: Retaining pieces on the home row to prevent enemy kinging.
  * Trapped piece penalty: Pieces with no legal moves.

### Tier 3: Advanced Engine Optimizations
* **Quiescence Search:** Prevents the "horizon effect" by continuing search through unstable capture chains.
* **Move Ordering:** Sorting captures and promotions first to maximize alpha-beta cutoffs.
* **Transposition Table (Zobrist Hashing):** Cache previously evaluated board states.
* **Iterative Deepening & Time Management:** Dynamic search depth based on move timer.

---

## 5. Iterative Implementation Plan

| Phase | Milestone | Deliverables | Status |
| :--- | :--- | :--- | :--- |
| **Phase 1** | **Core Domain & Rules Engine** | • Board representation & coordinates<br>• Move generator (regular, jumps, multi-jump chains)<br>• Flying King logic & mandatory jump enforcement<br>• Terminal condition detection (win/loss/draw)<br>• 100% test coverage on move edge cases (39 tests) | **COMPLETED** |
| **Phase 1.5** | **Engine Documentation (`docs/brain`)** | • Create `docs/brain/` structure matching Stello & Connect-4<br>• `README.md` (The Checkers brain on one page)<br>• `01-overview.md` (Architecture, life of a move)<br>• `02-board-and-coordinates.md` (Coordinates, 1..32 notation, Zobrist)<br>• `03-rules-and-move-generation.md` (Rules, flying kings, captures)<br>• `04-game-record.md` (GameSession, history, draw rules) | **COMPLETED** |
| **Phase 2** | **Shared App Layer & WPF Desktop GUI** | • `Checkers.App` shared ViewModels & game coordinator<br>• `Checkers.Wpf` desktop application<br>• Responsive 8x8 checkerboard with piece rendering<br>• Click-to-move & visual move indicator highlights<br>• Turn management, Undo/Redo, and sound effects | **COMPLETED** |
| **Phase 3** | **AI Opponent Integration** | • `IPlayer` abstraction<br>• Level 1: Random AI<br>• Level 2: Minimax with Alpha-Beta pruning & heuristics<br>• Difficulty selector in UI (Easy / Medium / Hard)<br>• Non-blocking asynchronous AI turns<br>• Document chapters `05-evaluation.md`, `06-move-ordering.md`, `07-search.md` | **Next Up** |
| **Phase 4** | **Engine Polish & Advanced Search** | • Quiescence search & move ordering<br>• Transposition table (Zobrist hashing)<br>• Game timers (chess clock / time per move)<br>• Document chapters `08-transposition-table.md`, `10-time-control.md` | Planned |
| **Phase 5** | **Blazor WebAssembly Client & Deployment** | • `Checkers.Web` project with .NET 10 WebAssembly AOT<br>• Responsive Web board UI inspired by Stello and Connect-4<br>• Integrated `/docs` viewer rendering `docs/brain` using Markdig<br>• CI/CD pipeline: `.github/workflows/azure-static-web-apps.yml`<br>• Automated deployment to Azure Static Web Apps | Planned |