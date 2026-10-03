# The Checkers brain

Checkers (Draughts) is a strategic board game of perfect information for two players played on the 32 dark squares of an $8 \times 8$ grid with 12 pieces per side. The computer's "brain" is the engine library `Checkers.Core`, which models board state using 64-bit bitboards, generates legal moves, evaluates positions, and searches the game tree to decide computer moves.

The engine implements **8×8 Draughts with two selectable rule variants — International Draughts (Flying Kings) and English Checkers (1-Step Kings)**:
- **Board & coordinates**: $8 \times 8$ grid geometry, playable dark-square parity $(\text{Row} + \text{Col}) \bmod 2 = 1$, standard Draughts 1–32 square notation, 64-bit bitboards (`4 × ulong`), and deterministic 64-bit Zobrist hashing;
- **Rules & move generation**: forward diagonal slides and short jumps for regular men, multi-square diagonal flight and long-distance jump captures with mandatory continuation for International flying kings, 4-direction single-step moves and jumps for English kings, strict mandatory captures with free choice among capture lines, and turn-ending crown-row promotion;
- **Game session & persistence**: atomic `Move` records, standard algebraic Draughts notation (`11-15`, `29x18x4`), `GameSession` state machine with Undo/Redo stacks, terminal condition evaluation (piece elimination, blocked opponent, 40-move rule, threefold repetition), and Portable Draughts Notation (PDN) save/load persistence;
- **Evaluation, search & transposition caching**: hardware `POPCNT`-accelerated static heuristic evaluation, in-place move ordering, Negamax with Alpha-Beta pruning, quiescence search, iterative deepening, configurable power-of-two 64-bit Zobrist transposition tables ($1,048,576$ to $16,777,216$ entries), and live search telemetry in both WPF desktop and Blazor WebAssembly;
- **Time control**: Settings dialog supporting Fixed Depth ($1\text{–}20$ plies), Time per Move ($1\text{–}60\text{ s}$), and Time per Game ($1\text{–}60\text{ min}$) with soft/hard time budgets, dynamic piece-count clock allocation, and Undo time refunds.

These documents explain how the engine works, how the mathematical and bitwise concepts are implemented, and how the parts fit together from introduction to references.

---

## The brain on one page

This is how the engine determines legal moves, searches for optimal play, and transitions game states:

```mermaid
flowchart TD
    Start["Current BoardState & ActivePlayer<br/>(64-Bit BitPosition + Zobrist Hash)"] --> ScanCaptures["Scan player bitboards for jumps"]
    ScanCaptures --> HasCaptures{"Any captures available?"}
    
    HasCaptures -- "Yes" --> MandatoryRule["Enforce Mandatory Capture:<br/>Discard all quiet moves"]
    MandatoryRule --> GenCaptures["Generate full capture chains:<br/>• Men: forward 2-step jumps<br/>• Flying Kings: diagonal ray flight + jump + continuation rule<br/>• English Kings: 1-hop jumps in 4 directions<br/>• Mid-jump crown-row promotion ends turn"]
    GenCaptures --> FreeChoice["Free Choice:<br/>Return all completed capture paths"]
    
    HasCaptures -- "No" --> ScanQuiet["Generate quiet moves:<br/>• Men: 1 square forward diagonally<br/>• Flying Kings: diagonal ray slide across empty squares<br/>• English Kings: 1 square in 4 directions"]
    ScanQuiet --> ReturnQuiet["Return all valid quiet moves"]
    
    FreeChoice --> ApplyMove["Apply selected move (Human or Negamax AI)"]
    ReturnQuiet --> ApplyMove
    
    ApplyMove --> Mutate["Update BitPosition & BoardState:<br/>1. Toggle source & destination bits<br/>2. Clear captured bits via ~move.Captured<br/>3. Crown to King if Row 0 (White) or Row 7 (Black)<br/>4. Update HalfMoveClock & switch ActivePlayer<br/>5. XOR-update 64-bit Zobrist hash"]
    
    Mutate --> CheckEnd{"Evaluate terminal status"}
    CheckEnd -- "Opponent has 0 pieces" --> WinElim["OpponentPiecesEliminated: Win!"]
    CheckEnd -- "Opponent has 0 legal moves" --> WinBlock["OpponentNoLegalMoves: Win!"]
    CheckEnd -- "HalfMoveClock >= 80" --> Draw40["FortyMoveRule: Draw"]
    CheckEnd -- "ZobristHash repeated 3 times" --> DrawRep["ThreefoldRepetition: Draw"]
    CheckEnd -- "Game continues" --> NextTurn["InProgress: Next Player's Turn"]
```

---

## Chapters

The documentation is organized in a logical progression from architectural overview and board fundamentals, through rules, AI evaluation, search, and bitboard optimization, to UI integration, glossary, and academic references:

| # | Chapter | Section | Content |
|---|---|---|---|
| 01 | [Overview](01-overview.md) | **Introduction** | Clean Architecture, projects, main engine types, rule variants, and the life of a move |
| 02 | [Board and coordinates](02-board-and-coordinates.md) | **Foundations** | $8 \times 8$ grid geometry, $1\dots 32$ Draughts notation, initial setup, and 64-bit Zobrist hashing |
| 03 | [Rules and move generation](03-rules-and-move-generation.md) | **Foundations** | Men slides/jumps, English 1-step kings, International flying kings, continuation rule, mandatory captures, promotion |
| 04 | [Game record](04-game-record.md) | **Foundations** | Move notation, `GameSession` state machine, Undo/Redo stacks, win/draw detection, and PDN persistence |
| 05 | [Evaluation](05-evaluation.md) | **AI & Search** | Static heuristic scoring: material weights, rank advancement, center control, king centralization, back-rank defense |
| 06 | [Move ordering](06-move-ordering.md) | **AI & Search** | Alpha-Beta pruning efficiency, TT hash move, capture/promotion priority keys, in-place insertion sort, root PV ordering |
| 07 | [Search](07-search.md) | **AI & Search** | Negamax Alpha-Beta pruning, Quiescence search, Iterative Deepening, Distance-to-Mate, async yielding, live telemetry |
| 08 | [Transposition table](08-transposition-table.md) | **AI & Search** | 16-byte cache-aligned Zobrist table, bound flags, replacement strategy, and 40-position empirical benchmarks ($1\text{M}$ vs $16\text{M}$) |
| 09 | [Time control](09-time-control.md) | **AI & Search** | Fixed Depth, Time per Move, Time per Game, soft/hard time budgets, dynamic piece-count allocation, Undo clock refunds |
| 10 | [Bitboards](10-bitboards.md) | **Optimization** | 64-bit bitboard representation (`4 × ulong`), shift/mask & ray-scan move generation, `POPCNT` evaluation, copy-make search, and 65× speedup benchmarks |
| 11 | [App integration](11-app-integration.md) | **Architecture** | Shared MVVM ViewModels, WPF desktop ThreadPool vs. Blazor WebAssembly AOT macrotask yielding, live analysis pipeline |
| 12 | [Glossary](12-glossary.md) | **Reference** | Definitions and cross-references for all Checkers, Draughts, Bitboard, and AI search terms |
| 13 | [References](13-references.md) | **Reference** | Official WCDF/FMJD rulesets, foundational AI search literature, and .NET 10 technical references |

---

## Reading paths

```mermaid
flowchart LR
    C01["01 Overview"] --> C02["02 Board & Coords"]
    C02 --> C03["03 Rules & Moves"]
    C03 --> C04["04 Game Record"]
    C03 --> C05["05 Evaluation"]
    C05 --> C06["06 Move Ordering"]
    C06 --> C07["07 Search"]
    C07 --> C08["08 Transposition Table"]
    C08 --> C09["09 Time Control"]
    C08 --> C10["10 Bitboards"]
    C04 --> C11["11 App Integration"]
    C09 --> C11
    C10 --> C12["12 Glossary"]
    C11 --> C12
    C12 --> C13["13 References"]
```

- **Engine Fundamentals & Rules:** Read chapters [01](01-overview.md), [02](02-board-and-coordinates.md), [03](03-rules-and-move-generation.md), and [04](04-game-record.md).
- **AI Search & Bitboard Optimization:** Read chapters [05](05-evaluation.md), [06](06-move-ordering.md), [07](07-search.md), [08](08-transposition-table.md), [09](09-time-control.md), and [10](10-bitboards.md).
- **Game Application & UI Integration:** Read chapters [01](01-overview.md), [04](04-game-record.md), and [11](11-app-integration.md).
- **Terminology & Academic Literature:** Check chapters [12](12-glossary.md) and [13](13-references.md).
