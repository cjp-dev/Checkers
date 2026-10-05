# 10 – Opening book

[Back to the index](README.md)

## In short

During the first 8 plies (`4` full turns for both White and Black), the Checkers game tree branches across thousands of symmetries and move-order transpositions. Searching the initial board from scratch on every move wastes clock time and tends to play the exact same deterministic opening line in every game.

To give `Checkers.Core` instant, varied, and deeply analyzed opening play for both **English Checkers** and **International Draughts (Flying Kings)**, the engine embeds two pre-computed, transposition-aware **8-ply Opening Books**:
- [`src/Checkers.Core/AI/Book/OpeningBook.English.txt`](../../src/Checkers.Core/AI/Book/OpeningBook.English.txt) (`2,100` unique positions, `66.6 KB`)
- [`src/Checkers.Core/AI/Book/OpeningBook.International.txt`](../../src/Checkers.Core/AI/Book/OpeningBook.International.txt) (`2,228` unique positions, `71.3 KB`)

---

## 1. Architecture Overview

The opening book subsystem in [`src/Checkers.Core/AI/Book/`](../../src/Checkers.Core/AI/Book/) consists of three core classes and a CLI generator/verifier in [`tools/Checkers.Benchmark/BookCommands.cs`](../../tools/Checkers.Benchmark/BookCommands.cs):

```mermaid
flowchart TD
    subgraph Gen["Offline CLI Generation (BookCommands.cs &amp; BookBuilder.cs)"]
        L0["Level 0 (1 pos)<br/>60s Multi-PV (k=3) Search<br/>Stores 3 best moves"]
        L17["Levels 1 .. 7 (Zobrist DAG Dedup)<br/>60s Multi-PV (k=3) Search per Node<br/>8 Parallel Workers + .partial Checkpoints"]
        BU["Bottom-Up Negamax BackUp (lv 8 → lv 0)<br/>Effective Horizon: ply 7 + ~27 = ~34 plies<br/>Verified by BookBuilder.CheckBackUp"]
        L0 --> L17 --> BU
    end

    subgraph Storage["Embedded Book Resources (Checkers.Core.dll)"]
        BF["BookFile.cs<br/>Text Format: &lt;ZobristHex16&gt; &lt;Ply&gt; &lt;Score&gt; [Move1:Score1 Move2:Score2 Move3:Score3]"]
        EN["OpeningBook.English.txt<br/>(2,100 positions, 8 plies)"]
        IN["OpeningBook.International.txt<br/>(2,228 positions, 8 plies)"]
        BU --> BF --> EN & IN
    end

    subgraph Runtime["Runtime Lookup (OpeningBook.cs &amp; MinimaxPlayer.cs)"]
        OB["OpeningBook.GetDefault(variant)<br/>O(1) Dictionary&lt;ulong, BookNode&gt; Lookup by BoardState.ZobristHash"]
        Margin["Randomized Top-Move Selection<br/>Picks uniformly among book moves within BookRandomMarginCp (10 cp) of best"]
        EN & IN --> OB --> Margin
    end
```

| File | Responsibility |
| :--- | :--- |
| [`BookFile.cs`](../../src/Checkers.Core/AI/Book/BookFile.cs) | Reads, writes, parses, and formats [`BookNode`](../../src/Checkers.Core/AI/Book/BookFile.cs) records and resilient `.partial` checkpoints (`ReadPartialNodes`). |
| [`BookBuilder.cs`](../../src/Checkers.Core/AI/Book/BookBuilder.cs) | Implements Top-Down Level-by-Level Multi-PV generation (`GenerateTopDown`), breadth-first DAG enumeration (`Enumerate`), bottom-up Negamax score propagation (`BackUp`), and full tree verification (`CheckBackUp`). |
| [`OpeningBook.cs`](../../src/Checkers.Core/AI/Book/OpeningBook.cs) | Lazy-loads the embedded resource for each [`CheckersVariant`](../../src/Checkers.Core/Models/CheckersVariant.cs) into an $O(1)$ `Dictionary<ulong, BookNode>` keyed by 64-bit `ZobristHash`, and implements `TryGetMove` with configurable randomization margin (`10 cp`) and child-transposition fallback. |
| [`BookCommands.cs`](../../tools/Checkers.Benchmark/BookCommands.cs) | Multi-threaded CLI (`book generate` and `book verify`) with per-node `.partial` checkpointing, incremental playable book flushes after each completed level, and `Ctrl+C` graceful stop/resume. |

---

## 2. Top-Down Level-by-Level Multi-PV Generation (`Approach A`)

A full-width Checkers tree to 8 plies contains over `200,000` positions, most of which involve inferior opening moves. Instead of expanding weak moves or relying on a shallow screening search to guess candidate moves, `BookBuilder.GenerateTopDown` builds a **width-$k$ ($k = 3$) Directed Acyclic Graph (DAG)** top-down from `lv 0` through `lv 7`:

### 2.1 Single-Search Multi-PV ($k = 3$) Root Search
At each unique position at level `lv p` (`0 <= p <= 7`), [`MinimaxPlayer.SearchMultiPv`](../../src/Checkers.Core/AI/MinimaxPlayer.cs) runs a **single 60-second iterative-deepening search** (`d = 1 .. 48`) to find the **3 best moves and their exact scores**:
- In standard single-PV alpha-beta, root `alpha` is raised to the `1st`-best move's score (`topScores[0]`), which causes the `2nd` and `3rd` best moves to fail low without receiving exact scores.
- In `SearchMultiPv(state, legalMoves, multiPvCount: 3)`, root `alpha` is instead held at the **$k$-th (`3rd`) best score found so far at depth $d$** (`topKScores[k - 1]`, or `-200,000` for the first 3 moves):
  - All moves that enter the top 3 beat `alpha` against an open upper window (`beta = +200,000`, i.e., `alpha_child = -200,000`) and therefore return their **exact minimax scores** in a single pass sharing the same `32 MiB` `TranspositionTable`.
  - All inferior moves (`4th`, `5th`, ...) that score $\le \text{topKScores}[2]$ fail high in the child node and are pruned by alpha-beta.
- **Forced-Capture Fast Path (`lv < 7`):** When a position at an internal level (`lv 0..6`) has only **1 legal move** (a mandatory jump), the move choice is 100% forced and its final score will be overwritten by the bottom-up Negamax `BackUp` from `lv 7`. `GenerateTopDown` scores single forced moves at `lv < 7` with a fast depth-10 search (`< 10 ms`) rather than spending 60 seconds choosing between 1 move.

### 2.2 `ZobristHash` Transposition Deduplication
Independent opening moves by White (`rows 5..7`) and Black (`rows 0..2`) frequently commute (`22-18, 11-15, 23-19` reaches the exact same board state as `23-19, 11-15, 22-18`).
- Every board state is identified by its 64-bit [`ZobristHash`](02-board-and-coordinates.md) (`BitPosition.ComputeZobristHash()`), which depends only on `(WhiteMen, WhiteKings, BlackMen, BlackKings, SideToMove)`.
- `GenerateTopDown` maintains a `reservedHashes` set across levels. Whenever two different move sequences transpose into the same `ZobristHash`, they point to the **same child node in the DAG**, and that board position is evaluated **only once**.
- Across `lv 0 .. lv 7`, transposition deduplication and forced captures reduce the number of evaluated positions from $1 + 3 + 9 + 27 + 81 + 243 + 729 + 2,187 = 3,280$ raw nodes down to **`975` unique internal positions** (English) and **`1,028` unique internal positions** (International) — a **~70% reduction** in search time.

### 2.3 Bottom-Up Negamax Score Propagation (`BackUp` & `CheckBackUp`)
When `lv 7` completes its 60-second Multi-PV searches (reaching search depths of `24–30 plies` from `lv 7`, i.e. **`ply 31–37` from the start of the game**):
1. Each move $(u \xrightarrow{m} w)$ from `lv 7` to a frontier leaf $w$ at `lv 8` defines the implied leaf score `-move.Score` from $w$'s perspective.
2. [`BookBuilder.BackUp`](../../src/Checkers.Core/AI/Book/BookBuilder.cs) propagates those scores bottom-up from `lv 8` down to `lv 0` via Negamax:
   $$\text{MoveScore}(u, m) = -\text{Score}(\text{Child}(u, m)), \qquad \text{Score}(u) = \max_{m \in \text{Moves}(u)} \text{MoveScore}(u, m)$$
3. Each node's stored moves are sorted descending by their backed-up score, and [`BookBuilder.CheckBackUp`](../../src/Checkers.Core/AI/Book/BookBuilder.cs) verifies that every reachable node and move from `lv 0` to `lv 8` satisfies `node.Score == max(m.Score)` and `m.Score == -child.Score` with **0 errors**.

---

## 3. Empirical Generation Results (`AMD Ryzen 7 7800X3D`, `8 Workers`, `60s / Node`)

Both opening books were generated on an **AMD Ryzen 7 7800X3D** (`8 physical cores / 16 threads, 96 MB 3D V-Cache, 32 GB RAM`) using `8` parallel workers (`1` worker per physical Zen 4 core, `32 MiB` TT per worker, `~480M–690M` nodes evaluated per 60-second position at depths `24–30 plies`):

### 3.1 Summary Comparison

| Metric | **English Checkers (`OpeningBook.English.txt`)** | **International Checkers (`OpeningBook.International.txt`)** |
| :--- | :---: | :---: |
| **Evaluated Internal Levels** | `lv 0 .. lv 7` (`8` plies of moves) | `lv 0 .. lv 7` (`8` plies of moves) |
| **Moves Stored per Position ($k$)** | Up to `3` | Up to `3` |
| **Search Budget per Position** | `60s` Multi-PV ($k=3$), `24–30 plies` (`~500M` nodes) | `60s` Multi-PV ($k=3$), `24–30 plies` (`~500M` nodes) |
| **Unique Internal Positions (`lv 0..7`)** | **`975`** | **`1,028`** |
| **Unique Frontier Leaves (`lv 8`)** | **`1,125`** | **`1,200`** |
| **Total Unique Book Positions (`lv 0..8`)** | **`2,100`** | **`2,228`** |
| **Embedded File Size** | **`68,181 bytes` (`66.6 KB`)** | **`73,042 bytes` (`71.3 KB`)** |
| **Wall-Clock Generation Time (`8 Workers`)** | **`1h 57m 00s`** | **`2h 04m 00s`** |
| **Root (`lv 0`) Backed-Up Score & Top 3 Moves** | **`+2` — `22-18 (+2)`, `22-17 (-2)`, `21-17 (-6)`** | **`+4` — `22-17 (+4)`, `22-18 (+2)`, `21-17 (-3)`** |
| **`BookBuilder.CheckBackUp` Verification** | **`OK (0 errors)`** | **`OK (0 errors)`** |

### 3.2 Unique Positions per Level (`lv 0` through `lv 8`)

| Book Level (`Ply`) | Side to Move | Raw Full-Tree Branches ($3^{\text{lv}}$) | **English Unique Positions** | **International Unique Positions** |
| :---: | :---: | :---: | :---: | :---: |
| **`lv 0` (`Ply 0`)** | White (Move 1) | `1` | `1` | `1` |
| **`lv 1` (`Ply 1`)** | Black (Move 1) | `3` | `3` | `3` |
| **`lv 2` (`Ply 2`)** | White (Move 2) | `9` | `9` | `9` |
| **`lv 3` (`Ply 3`)** | Black (Move 2) | `27` | `23` | `23` |
| **`lv 4` (`Ply 4`)** | White (Move 3) | `81` | `51` | `52` |
| **`lv 5` (`Ply 5`)** | Black (Move 3) | `243` | `110` | `117` |
| **`lv 6` (`Ply 6`)** | White (Move 4) | `729` | `247` | `264` |
| **`lv 7` (`Ply 7`)** | Black (Move 4) | `2,187` | `531` | `559` |
| **`lv 8` (`Ply 8` Leaves)** | White (Move 5) | `6,561` | `1,125` | `1,200` |
| **Total (`lv 0 .. 8`)** | — | `9,841` | **`2,100`** | **`2,228`** |

---

## 4. Text File Format & Runtime Integration

### 4.1 Human-Readable Text Format
Each line in `OpeningBook.English.txt` and `OpeningBook.International.txt` stores one unique board position:
```text
# Checkers Opening Book (English) — depth 8 plies (lv 0..7 evaluated), width 3, 2100 unique positions (975 internal, 1125 leaves)
# Format: <ZobristHex> <Ply> <Score> [Move1:Score1 Move2:Score2 Move3:Score3]
# Generated 2026-10-05 17:00 by Checkers.Benchmark book generate (60s Multi-PV (3) search/node, 8 workers, 1h 57m 00s)
29E98DC071E54022 0 +2 22-18:+2 22-17:-2 21-17:-6
C5BCD8C573E91211 1 +2 10-15:+2 11-15:0 10-14:-24
45FB48D367B7B66A 1 -2 11-15:-2 10-14:-4 11-16:-6
D771BBCAEDE04A59 1 +6 9-13:+6 11-15:+1 10-15:-2
```

### 4.2 Runtime Lookup & `10 cp` Randomized Variety
At the beginning of [`MinimaxPlayer.GetMoveAsync`](../../src/Checkers.Core/AI/MinimaxPlayer.cs), when `UseOpeningBook` is enabled:
1. `OpeningBook.GetDefault(_variant)` looks up `state.ZobristHash` in $O(1)$.
2. `TryGetMove` filters the node's stored moves to those within **`BookRandomMarginCp` (`10 cp` by default)** of the highest-scored book move (`bestScore - move.Score <= 10`) and selects one uniformly at random.
   - For example, at `lv 0` in English Checkers (`22-18:+2`, `22-17:-2`, `21-17:-6`), all three moves are within `8 cp` ($\le 10\text{ cp}$) of `+2`, so the AI plays all three classic openings with equal probability while automatically excluding any move that drops more than `10 cp` (such as `10-14:-24` on `lv 1`).
3. If a position has no explicit move list (or the opponent played an off-book move that transposes back into a known book position on the next ply), `TryGetMove` also checks if any legal move leads to a child `ZobristHash` present in the book (`impliedScore = -childNode.Score`).
4. When a book move is returned, `MinimaxPlayer` returns immediately (`0.0s`, `NodesEvaluated = 0`) and reports `Depth = "Book (8 plies)"` and `FromBook = true` in [`SearchAnalysis`](../../src/Checkers.Core/AI/SearchAnalysis.cs).