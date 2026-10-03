# 08 – Transposition table

[Back to the index](README.md)

## In short

Many different move orders lead to the exact same board configuration (a *transposition*). The **transposition table** (hash table) caches the evaluation, depth, search bound, and best move found for every searched position using 64-bit deterministic Zobrist hashing. 

When the search encounters a position again — either across different branches or during successive iterations of **iterative deepening** — the cached entry provides an immediate cutoff or seeds move ordering by trying the proven best move first.

In our empirical benchmark across 40 diverse, non-trivial positions, the transposition table delivered an overall **70.5% reduction in evaluated nodes** (from **2,408,731** baseline nodes down to **711,546** TT nodes) and an average **3.22x speedup** (with peak reductions reaching **92.3%** and **12.25x speedup**), with **zero search divergence**. Comparing the default **$1,048,576$ entries ($2^{20}$)** against the maximum **$16,777,216$ entries ($2^{24}$)** further reduced hash index collisions by **94.1%** (from **608** down to **36** collisions).

---

## The entry

[TranspositionTable.cs](../../src/Checkers.Core/AI/TranspositionTable.cs):

Each `TranspositionEntry` is packed into a compact **16-byte struct** so that 4 entries fit cleanly into a single 64-byte CPU cache line:

| Bytes | Field | Type | Contents |
|---|---|---|---|
| 0–7 | `Key` | `ulong` | 64-bit Zobrist hash |
| 8–11 | `Score` | `int` | Evaluation score (ply-normalized for mate/win) |
| 12 | `Depth` | `sbyte` | Remaining search depth when stored |
| 13 | `Bound` + `Age` | `byte` | Low 2 bits: `TranspositionBound`; High 6 bits: search generation `Age` ($0\dots 63$) |
| 14 | `BestMoveFrom` | `byte` | Encoded 6-bit source square coordinate (`(Row << 3) \| Col \| 0x40`) |
| 15 | `BestMoveTo` | `byte` | Encoded 6-bit target square coordinate (`(Row << 3) \| Col \| 0x40`) |

### Search bounds

| Bound | Condition | Meaning |
|---|---|---|
| `Exact` | $\alpha < \text{score} < \beta$ | True minimax score within the current search window. |
| `LowerBound` | $\text{score} \ge \beta$ | Beta-cutoff: the position was so good for the player that the opponent would never allow this branch. True score is at least this value. |
| `UpperBound` | $\text{score} \le \alpha$ | Alpha fail-low: no move was able to improve upon alpha. True score is at most this value. |

---

## Memory layout & configurable sizing

The table is sized to an exact power-of-two number of entries, allowing ultra-fast bitwise masking instead of modulo division:

$$
\text{index} = \text{key} \land \text{mask}
$$

The capacity is user-configurable in the **Settings Dialog** (in both WPF Desktop and Blazor WebAssembly) across five power-of-two steps from **$1,048,576$ ($2^{20}$, default)** up to **$16,777,216$ ($2^{24}$, matching Connect-4)**:

| Setting | Entries | Memory (16 B / entry) | Use Case |
|---|---|---|---|
| **Default ($2^{20}$)** | $1,048,576$ | 16 MiB | Default desktop and web client (fits in CPU L3 cache) |
| **Medium ($2^{21}$)** | $2,097,152$ | 32 MiB | Extended time-per-move play |
| **Large ($2^{22}$)** | $4,194,304$ | 64 MiB | Deep analysis |
| **Extra Large ($2^{23}$)** | $8,388,608$ | 128 MiB | Long clock games |
| **Maximum ($2^{24}$)** | $16,777,216$ | 256 MiB | Maximum capacity (matching Connect-4) |
| **Lightweight / Tests** | $65,536$ | 1 MiB | Unit tests & fast verification |

If allocating a larger table fails on a memory-constrained device (`OutOfMemoryException`), `TranspositionTable` automatically falls back to `DefaultEntries` ($1,048,576$) and sets `UsesFallbackCapacity = true`.

---

## Replacement strategy

Each slot holds one entry. When a new entry maps to an occupied slot:
1. **Same position:** Replace immediately with the updated depth or tighter bound.
2. **Older age:** Replace entries stored during earlier searches (`existing.Age != currentAge`).
3. **Depth-preferred:** Replace if the new search explored deeper or equal depth (`depth >= existing.Depth`).

In `MainViewModel`, the `TranspositionTable` instance is retained across moves within the same game (and cleared on `NewGame` or variant change). Calling `NewSearch()` at the start of each computer turn increments the 6-bit generation age so entries from earlier turns are prioritized for replacement while preserving deeper transpositions that remain relevant.

---

## Score normalization (win distance)

When a forced win or loss is detected ($|\text{score}| \ge 90,000$), storing the raw score would distort the win distance if probed from a different ply depth:
- **Storing:** $\text{storedScore} = \text{score} + \text{ply}$ (for wins) or $\text{score} - \text{ply}$ (for losses).
- **Retrieving:** $\text{retrievedScore} = \text{storedScore} - \text{ply}$ (for wins) or $\text{storedScore} + \text{ply}$ (for losses).

This preserves exact mate-in-$N$ distance invariant across all search depths and branches.

---

## Empirical benchmark (40 diverse positions)

To scientifically quantify the performance gains of the Transposition Table and compare the default capacity ($1,048,576$ entries) against the maximum capacity ($16,777,216$ entries), an automated empirical benchmark was run across 40 unique, non-trivial positions sampled from self-play under the International Flying Kings rules.

### Methodology
1. **Non-trivial position filtering:** Every position is verified to have a unique 64-bit Zobrist hash, at least 2 legal moves (excluding 0-node forced moves), and genuine multi-ply branching ($\ge 300$ nodes at 5 plies without immediate mate).
2. **Baseline calibration:** For each position, search depth (7 to 9 plies) was calibrated so that the baseline search completed within $\le 10$ seconds.
3. **Three-way comparative run:** Each position was searched at identical depth under three configurations:
   - **Baseline:** No Transposition Table.
   - **Default TT ($2^{20}$):** $1,048,576$ entries (16 MiB).
   - **Max TT ($2^{24}$):** $16,777,216$ entries (256 MiB).
4. **Verification:** Best moves and evaluation scores were compared to ensure 100% search consistency.

### Table 1: Baseline vs Default Transposition Table ($1,048,576$ entries)

| # | Category | Depth | Baseline Nodes | TT Nodes | Node Reduction | Baseline Time | TT Time | Speedup | TT Cutoffs |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Opening | 8 plies | 38,305 | 21,086 | **45.0%** | 86 ms | 44 ms | **1.95x** | 197 |
| 2 | Opening | 8 plies | 58,204 | 24,096 | **58.6%** | 116 ms | 45 ms | **2.58x** | 222 |
| 3 | Opening | 7 plies | 44,913 | 6,195 | **86.2%** | 75 ms | 11 ms | **6.82x** | 42 |
| 4 | Opening | 7 plies | 67,449 | 17,927 | **73.4%** | 122 ms | 33 ms | **3.70x** | 321 |
| 5 | Opening | 7 plies | 69,129 | 9,540 | **86.2%** | 114 ms | 16 ms | **7.12x** | 175 |
| 6 | Opening | 8 plies | 70,060 | 25,735 | **63.3%** | 117 ms | 45 ms | **2.60x** | 610 |
| 7 | Opening | 7 plies | 87,714 | 6,766 | **92.3%** | 147 ms | 12 ms | **12.25x** | 41 |
| 8 | Opening | 7 plies | 48,374 | 7,890 | **83.7%** | 78 ms | 14 ms | **5.57x** | 64 |
| 9 | Opening | 7 plies | 59,062 | 13,974 | **76.3%** | 93 ms | 23 ms | **4.04x** | 213 |
| 10 | Opening | 7 plies | 46,247 | 8,585 | **81.4%** | 72 ms | 18 ms | **4.00x** | 62 |
| 11 | Middlegame | 8 plies | 42,472 | 16,203 | **61.9%** | 63 ms | 26 ms | **2.42x** | 652 |
| 12 | Middlegame | 7 plies | 53,188 | 11,666 | **78.1%** | 89 ms | 21 ms | **4.24x** | 263 |
| 13 | Middlegame | 7 plies | 134,182 | 35,519 | **73.5%** | 202 ms | 59 ms | **3.42x** | 496 |
| 14 | Middlegame | 7 plies | 56,142 | 10,008 | **82.2%** | 87 ms | 16 ms | **5.44x** | 345 |
| 15 | Middlegame | 7 plies | 69,640 | 31,815 | **54.3%** | 115 ms | 62 ms | **1.85x** | 807 |
| 16 | Middlegame | 8 plies | 11,328 | 7,462 | **34.1%** | 18 ms | 13 ms | **1.38x** | 116 |
| 17 | Middlegame | 8 plies | 36,601 | 10,266 | **72.0%** | 66 ms | 18 ms | **3.67x** | 153 |
| 18 | Middlegame | 7 plies | 41,710 | 6,496 | **84.4%** | 67 ms | 11 ms | **6.09x** | 137 |
| 19 | Middlegame | 8 plies | 55,338 | 22,599 | **59.2%** | 90 ms | 35 ms | **2.57x** | 651 |
| 20 | Middlegame | 8 plies | 118,841 | 32,487 | **72.7%** | 180 ms | 51 ms | **3.53x** | 818 |
| 21 | Middlegame | 8 plies | 43,885 | 12,158 | **72.3%** | 67 ms | 20 ms | **3.35x** | 314 |
| 22 | Middlegame | 8 plies | 17,425 | 11,383 | **34.7%** | 25 ms | 16 ms | **1.56x** | 565 |
| 23 | Middlegame | 7 plies | 61,587 | 12,579 | **79.6%** | 102 ms | 22 ms | **4.64x** | 314 |
| 24 | Middlegame | 7 plies | 36,218 | 8,757 | **75.8%** | 61 ms | 14 ms | **4.36x** | 110 |
| 25 | Middlegame | 8 plies | 67,300 | 24,042 | **64.3%** | 117 ms | 45 ms | **2.60x** | 506 |
| 26 | Endgame | 8 plies | 48,927 | 23,148 | **52.7%** | 51 ms | 26 ms | **1.96x** | 1,083 |
| 27 | Endgame | 7 plies | 46,614 | 16,010 | **65.7%** | 77 ms | 28 ms | **2.75x** | 254 |
| 28 | Endgame | 9 plies | 8,892 | 6,222 | **30.0%** | 13 ms | 9 ms | **1.44x** | 326 |
| 29 | Endgame | 7 plies | 51,929 | 16,865 | **67.5%** | 99 ms | 30 ms | **3.30x** | 728 |
| 30 | Endgame | 8 plies | 48,050 | 15,962 | **66.8%** | 56 ms | 19 ms | **2.95x** | 643 |
| 31 | Endgame | 8 plies | 95,291 | 20,585 | **78.4%** | 89 ms | 23 ms | **3.87x** | 1,550 |
| 32 | Endgame | 7 plies | 78,917 | 20,366 | **74.2%** | 115 ms | 32 ms | **3.59x** | 574 |
| 33 | Endgame | 8 plies | 75,185 | 31,368 | **58.3%** | 125 ms | 55 ms | **2.27x** | 714 |
| 34 | Endgame | 7 plies | 90,965 | 16,121 | **82.3%** | 147 ms | 28 ms | **5.25x** | 608 |
| 35 | Endgame | 8 plies | 29,293 | 13,406 | **54.2%** | 38 ms | 19 ms | **2.00x** | 355 |
| 36 | Blockade/Tension | 8 plies | 43,113 | 17,679 | **59.0%** | 68 ms | 30 ms | **2.27x** | 409 |
| 37 | Blockade/Tension | 8 plies | 25,695 | 10,331 | **59.8%** | 41 ms | 17 ms | **2.41x** | 581 |
| 38 | Blockade/Tension | 8 plies | 33,991 | 13,149 | **61.3%** | 44 ms | 19 ms | **2.32x** | 350 |
| 39 | Blockade/Tension | 8 plies | 160,896 | 61,520 | **61.8%** | 299 ms | 115 ms | **2.60x** | 3,171 |
| 40 | Blockade/Tension | 8 plies | 135,659 | 33,580 | **75.2%** | 224 ms | 58 ms | **3.86x** | 1,146 |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| **Total** | **All 40** | **7–9 plies** | **2,408,731** | **711,546** | **70.5%** | **3,855 ms** | **1,198 ms** | **3.22x** | **20,686** |

---

### Table 2: Default TT ($1,048,576$ entries) vs Max TT ($16,777,216$ entries)

| # | Category | Depth | Baseline Nodes | Default TT Nodes (1M) | Max TT Nodes (16M) | Default Time | Max Time | Default Collisions | Max Collisions |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Opening | 8 plies | 38,305 | 21,086 | 21,086 | 44 ms | 47 ms | 15 | 0 |
| 2 | Opening | 8 plies | 58,204 | 24,096 | 24,096 | 45 ms | 44 ms | 17 | 1 |
| 3 | Opening | 7 plies | 44,913 | 6,195 | 6,195 | 11 ms | 11 ms | 0 | 0 |
| 4 | Opening | 7 plies | 67,449 | 17,927 | 17,927 | 33 ms | 33 ms | 11 | 0 |
| 5 | Opening | 7 plies | 69,129 | 9,540 | 9,540 | 16 ms | 16 ms | 6 | 0 |
| 6 | Opening | 8 plies | 70,060 | 25,735 | 25,735 | 45 ms | 46 ms | 23 | 0 |
| 7 | Opening | 7 plies | 87,714 | 6,766 | 6,766 | 12 ms | 14 ms | 0 | 0 |
| 8 | Opening | 7 plies | 48,374 | 7,890 | 7,890 | 14 ms | 15 ms | 0 | 0 |
| 9 | Opening | 7 plies | 59,062 | 13,974 | 13,974 | 23 ms | 24 ms | 2 | 0 |
| 10 | Opening | 7 plies | 46,247 | 8,585 | 8,585 | 18 ms | 15 ms | 0 | 0 |
| 11 | Middlegame | 8 plies | 42,472 | 16,203 | 16,203 | 26 ms | 27 ms | 1 | 0 |
| 12 | Middlegame | 7 plies | 53,188 | 11,666 | 11,666 | 21 ms | 21 ms | 1 | 0 |
| 13 | Middlegame | 7 plies | 134,182 | 35,519 | 35,504 | 59 ms | 59 ms | 36 | 1 |
| 14 | Middlegame | 7 plies | 56,142 | 10,008 | 10,008 | 16 ms | 16 ms | 0 | 0 |
| 15 | Middlegame | 7 plies | 69,640 | 31,815 | 31,813 | 62 ms | 61 ms | 59 | 2 |
| 16 | Middlegame | 8 plies | 11,328 | 7,462 | 7,462 | 13 ms | 13 ms | 2 | 0 |
| 17 | Middlegame | 8 plies | 36,601 | 10,266 | 10,266 | 18 ms | 18 ms | 5 | 0 |
| 18 | Middlegame | 7 plies | 41,710 | 6,496 | 6,496 | 11 ms | 11 ms | 0 | 0 |
| 19 | Middlegame | 8 plies | 55,338 | 22,599 | 22,594 | 35 ms | 36 ms | 19 | 0 |
| 20 | Middlegame | 8 plies | 118,841 | 32,487 | 32,487 | 51 ms | 52 ms | 44 | 3 |
| 21 | Middlegame | 8 plies | 43,885 | 12,158 | 12,158 | 20 ms | 20 ms | 10 | 7 |
| 22 | Middlegame | 8 plies | 17,425 | 11,383 | 11,383 | 16 ms | 17 ms | 10 | 0 |
| 23 | Middlegame | 7 plies | 61,587 | 12,579 | 12,579 | 22 ms | 22 ms | 6 | 0 |
| 24 | Middlegame | 7 plies | 36,218 | 8,757 | 8,757 | 14 ms | 15 ms | 4 | 4 |
| 25 | Middlegame | 8 plies | 67,300 | 24,042 | 24,040 | 45 ms | 45 ms | 28 | 0 |
| 26 | Endgame | 8 plies | 48,927 | 23,148 | 23,148 | 26 ms | 26 ms | 14 | 2 |
| 27 | Endgame | 7 plies | 46,614 | 16,010 | 16,008 | 28 ms | 29 ms | 6 | 0 |
| 28 | Endgame | 9 plies | 8,892 | 6,222 | 6,222 | 9 ms | 9 ms | 2 | 0 |
| 29 | Endgame | 7 plies | 51,929 | 16,865 | 16,865 | 30 ms | 30 ms | 1 | 0 |
| 30 | Endgame | 8 plies | 48,050 | 15,962 | 15,962 | 19 ms | 20 ms | 25 | 0 |
| 31 | Endgame | 8 plies | 95,291 | 20,585 | 20,585 | 23 ms | 23 ms | 11 | 0 |
| 32 | Endgame | 7 plies | 78,917 | 20,366 | 20,366 | 32 ms | 33 ms | 13 | 4 |
| 33 | Endgame | 8 plies | 75,185 | 31,368 | 31,365 | 55 ms | 56 ms | 52 | 2 |
| 34 | Endgame | 7 plies | 90,965 | 16,121 | 16,121 | 28 ms | 31 ms | 4 | 0 |
| 35 | Endgame | 8 plies | 29,293 | 13,406 | 13,406 | 19 ms | 20 ms | 5 | 0 |
| 36 | Blockade/Tension | 8 plies | 43,113 | 17,679 | 17,677 | 30 ms | 27 ms | 12 | 2 |
| 37 | Blockade/Tension | 8 plies | 25,695 | 10,331 | 10,331 | 17 ms | 18 ms | 2 | 0 |
| 38 | Blockade/Tension | 8 plies | 33,991 | 13,149 | 13,149 | 19 ms | 20 ms | 15 | 0 |
| 39 | Blockade/Tension | 8 plies | 160,896 | 61,520 | 61,505 | 115 ms | 115 ms | 122 | 6 |
| 40 | Blockade/Tension | 8 plies | 135,659 | 33,580 | 33,565 | 58 ms | 59 ms | 25 | 2 |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| **Total** | **All 40** | **7–9 plies** | **2,408,731** | **711,546** | **711,485** | **1,198 ms** | **1,214 ms** | **608** | **36** |

### Key observations
1. **100% Non-trivial search trees:** All 40 positions have multiple legal moves ($\ge 2$) and unique Zobrist hashes, evaluating between **8,892 and 160,896 baseline nodes** per position (totaling **2.41 million baseline nodes**).
2. **Massive pruning vs Baseline:** Enabling the Transposition Table cut total evaluated nodes from **2,408,731** down to **711,546** (**70.5% overall reduction**, **3.22x speedup**, peak **12.25x speedup** on Position 7).
3. **Default ($1,048,576$) vs Max ($16,777,216$) Comparison:**
   - **94.1% fewer hash collisions:** Increasing table capacity $16\times$ (from $2^{20}$ to $2^{24}$ entries) reduced index collisions across the 40 positions by $16.9\times$, from **608 collisions down to just 36 collisions**, saving 61 additional nodes on the largest trees (Positions #13, #15, #19, #25, #27, #33, #36, #39, #40).
   - **CPU Cache Locality vs Capacity:** At depths 7–9 plies, the $1,048,576$-entry table (16 MiB) fits mostly within CPU L3 cache and finishes in **1,198 ms**, whereas the $16,777,216$-entry table (256 MiB) incurs main-memory DRAM latency (**1,214 ms**). Consequently, **$1,048,576$ entries ($2^{20}$) is the optimal default** for standard play, while larger settings up to **$16,777,216$ entries ($2^{24}$)** are available in the Settings Dialog for long time-control games and deep multi-move persistent analysis.
4. **Search consistency:** Across all 40 positions, Baseline, Default TT ($1\text{M}$), and Max TT ($16\text{M}$) selected identical optimal moves and matching evaluation scores.

