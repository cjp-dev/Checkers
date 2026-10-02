# Checkers Game - Project Specification & Roadmap

## 1. Overview & Vision
The goal is to develop an extensible, high-performance Checkers (Draughts) application in C# and .NET. 

The project follows **Clean Architecture** to ensure complete decoupling between the game rules, board state, AI engines, and presentation layers.
- **Primary Client:** Modern .NET 10 WPF application.
- **Future Client:** Blazor WebAssembly / Server web application sharing the exact same core engine.

---

## 2. Game Rules & Mechanics

The game implements an **8x8 Draughts variant combining English capture rules with International Flying Kings**:

### 2.1 Board & Pieces
* **Board:** 8x8 grid (64 squares total; 32 active dark squares).
* **Pieces:** 12 White (Light) pieces vs 12 Black (Dark) pieces.
* **Initial Setup:** Pieces occupy the dark squares of the first three rows on opposing sides.

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
  * If multiple pieces have capture options, the player may choose which piece to initiate the capture with.
* **Multi-Jump Sequences:**
  * If a piece makes a jump and has further valid jumps available from its new position, it **must** continue jumping in the same turn until no further captures remain.
  * A captured piece is removed from the board (captured pieces are removed according to official rules without jumping the same piece twice in a single sequence).
* **Promotion Rule:**
  * When a regular man reaches the furthest rank (crown row), it immediately promotes to a King and its turn ends (any further jumping for that piece must wait for subsequent turns).

### 2.4 End Game Conditions
* **Win:** The opponent has no pieces remaining, or has no legal moves remaining on their turn (completely blocked).
* **Draw:**
  * Threefold repetition of the same board state and turn.
  * 40-move rule without captures or promotions (configurable threshold).
  * Mutual agreement.

---

## 3. System Architecture & Solution Structure

```
Checkers/
├── src/
│   ├── Checkers.Core/             # Shared Class Library (.NET 10)
│   │   ├── Models/                # Piece, Square, Position, Move, BoardState
│   │   ├── Engine/                # RuleEngine, MoveGenerator, GameSession
│   │   └── AI/                    # IPlayer, RandomPlayer, MinimaxPlayer, Evaluator
│   │
│   ├── Checkers.Wpf/              # Desktop Presentation Layer (WPF, MVVM)
│   │   ├── ViewModels/            # MainViewModel, BoardViewModel, SettingsViewModel
│   │   ├── Views/                 # MainWindow, BoardControl, GameOverDialog
│   │   └── Converters/            # Board & Piece UI converters
│   │
│   └── Checkers.Blazor/           # (Future) Web Presentation Layer (Blazor WebAssembly)
│       └── Components/            # BoardComponent, MoveHistoryComponent
│
└── tests/
    └── Checkers.Core.Tests/       # Unit tests for Rules, Edge Cases, and AI
```

### 3.1 `Checkers.Core` (Engine & Logic)
* **Zero UI Dependencies:** Independent of WPF, Avalonia, or web frameworks.
* **Deterministic & Pure:** Board state mutations produce verifiable events and states.
* **Interfaces:**
  * `IRuleEngine`: Evaluates board state, generates all legal moves (including mandatory captures and jump chains), validates moves, and detects terminal states.
  * `IPlayer`: Common abstraction for human input, random AI, minimax AI, or network players.
  * `IEvaluationFunction`: Heuristic evaluator for AI board scoring.

### 3.2 `Checkers.Wpf` (Desktop UI)
* **Framework:** .NET 10 WPF.
* **Pattern:** MVVM using `CommunityToolkit.Mvvm`.
* **Asynchronous AI:** AI computations run on background tasks (`Task.Run`) to keep the UI smooth and responsive at 60 FPS.
* **Visual Features:**
  * Visual highlights for valid moves and selectable pieces.
  * Visual indicator for mandatory capture requirements.
  * Highlight of the last move executed.
  * King distinction (crown icon/badge).
  * Move history notation panel.
  * Undo / Redo support.

---

## 4. Artificial Intelligence (AI) Roadmap

### Tier 1: Random Player (`RandomPlayer`)
* Identifies all valid moves (strictly respecting forced jumps).
* Selects one uniformly at random.
* Serves as baseline opponent and rule verification agent.

### Tier 2: Minimax with Alpha-Beta Pruning (`MinimaxPlayer`)
* **Algorithm:** Minimax search tree with $\alpha$-$\beta$ pruning.
* **Search Depth:** Configurable from Depth 2 (Beginner) to Depth 6-8 (Challenging).
* **Static Evaluation Function ($H(s)$):**
  * Material balance: Men = 100 pts, Kings = 300 pts.
  * Board positioning: Central square control bonus.
  * Advancement: Rewarding men closer to promotion.
  * Back-row defense: Retaining pieces on the home row to prevent enemy kinging.
  * Trapped piece penalty: Pieces with no legal moves.

### Tier 3: Advanced Engine Optimizations (Future Iteration)
* **Quiescence Search:** Prevents the "horizon effect" by continuing search through unstable capture chains.
* **Move Ordering:** Sorting captures and promotions first to maximize alpha-beta cutoffs.
* **Transposition Table (Zobrist Hashing):** Cache previously evaluated board states.
* **Iterative Deepening & Time Management:** Dynamic search depth based on move timer.

---

## 5. Iterative Implementation Plan

| Phase | Milestone | Deliverables |
| :--- | :--- | :--- |
| **Phase 1** | **Core Domain & Rules Engine** | • Board representation & coordinates<br>• Move generator (regular, jumps, multi-jump chains)<br>• Flying King logic & mandatory jump enforcement<br>• Terminal condition detection (win/loss/draw)<br>• 100% test coverage on move edge cases |
| **Phase 2** | **WPF GUI (Local 2-Player)** | • 8x8 interactive board with responsive sizing<br>• Click-to-move & visual move indicator highlights<br>• Turn management & game reset/new game<br>• Move history list & Undo/Redo stack |
| **Phase 3** | **AI Opponent Integration** | • `IPlayer` abstraction<br>• Level 1: Random AI<br>• Level 2: Minimax with Alpha-Beta pruning<br>• Difficulty selector in UI (Easy / Medium / Hard)<br>• Non-blocking asynchronous AI turns |
| **Phase 4** | **Engine Polish & Features** | • Quiescence search & move ordering<br>• Game timers (optional chess clock mode)<br>• PGN/PDN game notation save/load |
| **Phase 5** | **Blazor Web Application** | • WebAssembly Blazor project referencing `Checkers.Core`<br>• Interactive web board component<br>• Full feature parity with desktop game |