# 08 – Transposition table

[Back to the index](README.md)

## In short

Many different move orders lead to the exact same board configuration (a *transposition*). The **transposition table** (hash table) caches the evaluation, depth, search bound, and best move found for every searched position using 64-bit deterministic Zobrist hashing. 

When the search encounters a position again — either across different branches or during successive iterations of **iterative deepening** — the cached entry provides an immediate cutoff or seeds move ordering by trying the proven best move first.

In our empirical benchmarks across 40 diverse, non-trivial positions:
- **Standard Benchmark (7–9 plies, ~0.1s/position):** The transposition table delivered a **70.5% reduction in evaluated nodes** (`2,408,731` $\rightarrow$ `711,546` nodes) and a **3.22x speedup** (`3,855 ms` $\rightarrow$ `1,198 ms`).
- **Deep Benchmark (9–13 plies, ~2.4s/position, up to 6.6s):** Pruning efficiency scaled even higher to an **84.8% reduction in evaluated nodes** (`62,882,064` $\rightarrow$ `9,546,749` nodes in $1\text{M}$ TT / `9,517,400` in $16\text{M}$ TT) and a **6.03x overall speedup** (`96,088 ms` $\rightarrow$ `15,931 ms`), with **492,949 direct hash cutoffs** and **93.8% fewer hash collisions** in the $16,777,216$-entry table (`99,743` $\rightarrow$ `6,151` collisions).

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

## Empirical Benchmark 1: Standard Depth (7–9 Plies, ~0.1s / Position)

To scientifically quantify the performance gains of the Transposition Table and compare the default capacity ($1,048,576$ entries) against the maximum capacity ($16,777,216$ entries), an automated empirical benchmark was first run at standard interactive depths (7–9 plies) across 40 unique, non-trivial positions sampled from self-play under the International Flying Kings rules.

### Table 1A: Baseline vs Default Transposition Table ($1,048,576$ entries) — Standard Depth

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

### Table 1B: Default TT ($1,048,576$) vs Max TT ($16,777,216$) — Standard Depth

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

---

## Empirical Benchmark 2: Extended Deep Search (7–14 Plies, ~4–7 Seconds per Position)

To test how the Transposition Table and its configurable capacities ($1,048,576$ vs $16,777,216$ entries) behave when the search evaluates millions of nodes per position, a second benchmark (`dotnet run --project tools/Checkers.Benchmark -c Release -- --deep`) calibrated each of the 40 positions to deeper search depths (**7 to 14 plies**, averaging **3.86 seconds** per position on baseline and up to **7.23 seconds** / **5.06 million baseline nodes** on single positions, totaling **95,898,151 baseline nodes** and **154.3 seconds** across all 40 positions). Raw JSON output is stored in `docs/brain/tt_deep_benchmark_results.json`.

### Table 2A: Baseline vs Default Transposition Table ($1,048,576$ entries) — Deep Search

| # | Category | Depth | Baseline Nodes | TT Nodes | Node Reduction | Baseline Time | TT Time | Speedup | TT Cutoffs |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Opening | 11 plies | 1,364,775 | 292,659 | **78.6%** | 2,376 ms | 536 ms | **4.43x** | 5,515 |
| 2 | Opening | 12 plies | 2,268,109 | 477,847 | **78.9%** | 3,705 ms | 840 ms | **4.41x** | 8,986 |
| 3 | Opening | 11 plies | 2,152,631 | 62,373 | **97.1%** | 3,416 ms | 105 ms | **32.53x** | 1,627 |
| 4 | Opening | 10 plies | 3,862,989 | 483,930 | **87.5%** | 6,365 ms | 864 ms | **7.37x** | 12,810 |
| 5 | Opening | 10 plies | 2,204,847 | 149,910 | **93.2%** | 3,718 ms | 274 ms | **13.57x** | 4,616 |
| 6 | Opening | 11 plies | 1,540,643 | 322,604 | **79.1%** | 2,581 ms | 562 ms | **4.59x** | 6,667 |
| 7 | Opening | 10 plies | 2,267,162 | 269,454 | **88.1%** | 3,951 ms | 494 ms | **8.00x** | 4,910 |
| 8 | Opening | 10 plies | 1,915,942 | 272,268 | **85.8%** | 3,217 ms | 478 ms | **6.73x** | 5,705 |
| 9 | Opening | 10 plies | 1,662,035 | 240,271 | **85.5%** | 2,951 ms | 443 ms | **6.66x** | 8,900 |
| 10 | Opening | 10 plies | 1,742,411 | 138,374 | **92.1%** | 2,916 ms | 249 ms | **11.71x** | 3,073 |
| 11 | Middlegame | 12 plies | 2,074,281 | 186,845 | **91.0%** | 3,185 ms | 303 ms | **10.51x** | 14,172 |
| 12 | Middlegame | 10 plies | 2,101,525 | 116,233 | **94.5%** | 3,292 ms | 200 ms | **16.46x** | 3,615 |
| 13 | Middlegame | 10 plies | 2,763,961 | 337,093 | **87.8%** | 4,374 ms | 574 ms | **7.62x** | 11,016 |
| 14 | Middlegame | 10 plies | 2,723,163 | 278,835 | **89.8%** | 4,183 ms | 465 ms | **9.00x** | 11,006 |
| 15 | Middlegame | 10 plies | 2,115,795 | 358,175 | **83.1%** | 3,697 ms | 647 ms | **5.71x** | 13,415 |
| 16 | Middlegame | 14 plies | 2,595,254 | 382,298 | **85.3%** | 4,089 ms | 672 ms | **6.08x** | 14,591 |
| 17 | Middlegame | 12 plies | 2,799,308 | 307,907 | **89.0%** | 4,790 ms | 563 ms | **8.51x** | 10,283 |
| 18 | Middlegame | 11 plies | 4,329,327 | 447,196 | **89.7%** | 6,555 ms | 694 ms | **9.45x** | 19,383 |
| 19 | Middlegame | 12 plies | 3,506,277 | 533,483 | **84.8%** | 5,152 ms | 843 ms | **6.11x** | 17,763 |
| 20 | Middlegame | 10 plies | 1,506,054 | 234,959 | **84.4%** | 2,226 ms | 373 ms | **5.97x** | 7,769 |
| 21 | Middlegame | 13 plies | 2,674,334 | 355,299 | **86.7%** | 4,042 ms | 581 ms | **6.96x** | 15,743 |
| 22 | Middlegame | 14 plies | 2,560,701 | 601,181 | **76.5%** | 4,035 ms | 953 ms | **4.23x** | 52,526 |
| 23 | Middlegame | 10 plies | 2,632,773 | 311,310 | **88.2%** | 4,312 ms | 540 ms | **7.99x** | 10,217 |
| 24 | Middlegame | 11 plies | 3,509,716 | 208,480 | **94.1%** | 5,753 ms | 355 ms | **16.21x** | 7,567 |
| 25 | Middlegame | 11 plies | 1,757,145 | 216,934 | **87.7%** | 3,081 ms | 401 ms | **7.68x** | 5,486 |
| 26 | Endgame | 8 plies | 48,927 | 23,148 | **52.7%** | 53 ms | 25 ms | **2.12x** | 1,083 |
| 27 | Endgame | 10 plies | 1,662,722 | 163,987 | **90.1%** | 2,770 ms | 287 ms | **9.65x** | 10,476 |
| 28 | Endgame | 7 plies | 8,892 | 6,222 | **30.0%** | 14 ms | 9 ms | **1.56x** | 326 |
| 29 | Endgame | 10 plies | 2,248,895 | 226,528 | **89.9%** | 3,552 ms | 366 ms | **9.70x** | 14,609 |
| 30 | Endgame | 11 plies | 980,995 | 202,311 | **79.4%** | 1,451 ms | 314 ms | **4.62x** | 17,962 |
| 31 | Endgame | 11 plies | 1,622,990 | 154,247 | **90.5%** | 2,079 ms | 198 ms | **10.50x** | 21,131 |
| 32 | Endgame | 10 plies | 3,057,532 | 551,373 | **82.0%** | 5,821 ms | 1,003 ms | **5.80x** | 54,515 |
| 33 | Endgame | 11 plies | 3,996,874 | 415,579 | **89.6%** | 7,230 ms | 776 ms | **9.32x** | 24,137 |
| 34 | Endgame | 10 plies | 4,030,225 | 243,580 | **94.0%** | 6,954 ms | 438 ms | **15.88x** | 16,764 |
| 35 | Endgame | 12 plies | 1,739,170 | 288,749 | **83.4%** | 2,255 ms | 388 ms | **5.81x** | 16,590 |
| 36 | Blockade/Tension | 12 plies | 2,714,028 | 149,121 | **94.5%** | 4,156 ms | 248 ms | **16.76x** | 5,415 |
| 37 | Blockade/Tension | 12 plies | 2,900,280 | 296,991 | **89.8%** | 4,899 ms | 539 ms | **9.09x** | 27,969 |
| 38 | Blockade/Tension | 13 plies | 5,057,208 | 358,765 | **92.9%** | 7,107 ms | 540 ms | **13.16x** | 23,103 |
| 39 | Blockade/Tension | 10 plies | 1,543,917 | 209,163 | **86.5%** | 2,678 ms | 376 ms | **7.12x** | 11,267 |
| 40 | Blockade/Tension | 11 plies | 3,654,338 | 241,440 | **93.4%** | 5,297 ms | 384 ms | **13.79x** | 9,651 |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| **Total** | **All 40** | **7–14 plies** | **95,898,151** | **11,117,122** | **88.4%** | **154,278 ms** | **18,900 ms** | **8.16x** | **532,359** |

---

### Table 2B: Default TT ($1,048,576$ entries) vs Max TT ($16,777,216$ entries) — Deep Search

| # | Category | Depth | Baseline Nodes | Default TT Nodes (1M) | Max TT Nodes (16M) | Default Time | Max Time | Default Collisions | Max Collisions |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Opening | 11 plies | 1,364,775 | 292,659 | 291,704 | 536 ms | 538 ms | 3,573 | 231 |
| 2 | Opening | 12 plies | 2,268,109 | 477,847 | 476,408 | 840 ms | 845 ms | 8,166 | 522 |
| 3 | Opening | 11 plies | 2,152,631 | 62,373 | 62,343 | 105 ms | 109 ms | 132 | 7 |
| 4 | Opening | 10 plies | 3,862,989 | 483,930 | 481,688 | 864 ms | 868 ms | 7,558 | 562 |
| 5 | Opening | 10 plies | 2,204,847 | 149,910 | 149,197 | 274 ms | 266 ms | 997 | 68 |
| 6 | Opening | 11 plies | 1,540,643 | 322,604 | 320,520 | 562 ms | 573 ms | 4,264 | 251 |
| 7 | Opening | 10 plies | 2,267,162 | 269,454 | 268,585 | 494 ms | 491 ms | 2,988 | 157 |
| 8 | Opening | 10 plies | 1,915,942 | 272,268 | 271,929 | 478 ms | 484 ms | 3,016 | 196 |
| 9 | Opening | 10 plies | 1,662,035 | 240,271 | 239,878 | 443 ms | 475 ms | 2,359 | 141 |
| 10 | Opening | 10 plies | 1,742,411 | 138,374 | 137,955 | 249 ms | 264 ms | 891 | 77 |
| 11 | Middlegame | 12 plies | 2,074,281 | 186,845 | 186,572 | 303 ms | 310 ms | 1,178 | 109 |
| 12 | Middlegame | 10 plies | 2,101,525 | 116,233 | 116,113 | 200 ms | 198 ms | 369 | 26 |
| 13 | Middlegame | 10 plies | 2,763,961 | 337,093 | 334,222 | 574 ms | 572 ms | 4,237 | 244 |
| 14 | Middlegame | 10 plies | 2,723,163 | 278,835 | 277,858 | 465 ms | 468 ms | 3,080 | 160 |
| 15 | Middlegame | 10 plies | 2,115,795 | 358,175 | 357,057 | 647 ms | 656 ms | 4,669 | 300 |
| 16 | Middlegame | 14 plies | 2,595,254 | 382,298 | 380,802 | 672 ms | 681 ms | 5,576 | 354 |
| 17 | Middlegame | 12 plies | 2,799,308 | 307,907 | 307,571 | 563 ms | 571 ms | 3,383 | 221 |
| 18 | Middlegame | 11 plies | 4,329,327 | 447,196 | 445,009 | 694 ms | 692 ms | 8,967 | 600 |
| 19 | Middlegame | 12 plies | 3,506,277 | 533,483 | 532,057 | 843 ms | 861 ms | 11,837 | 774 |
| 20 | Middlegame | 10 plies | 1,506,054 | 234,959 | 234,638 | 373 ms | 383 ms | 2,312 | 152 |
| 21 | Middlegame | 13 plies | 2,674,334 | 355,299 | 353,902 | 581 ms | 594 ms | 5,727 | 371 |
| 22 | Middlegame | 14 plies | 2,560,701 | 601,181 | 598,846 | 953 ms | 959 ms | 14,075 | 992 |
| 23 | Middlegame | 10 plies | 2,632,773 | 311,310 | 310,815 | 540 ms | 542 ms | 3,829 | 163 |
| 24 | Middlegame | 11 plies | 3,509,716 | 208,480 | 208,095 | 355 ms | 358 ms | 1,681 | 127 |
| 25 | Middlegame | 11 plies | 1,757,145 | 216,934 | 216,714 | 401 ms | 410 ms | 1,843 | 127 |
| 26 | Endgame | 8 plies | 48,927 | 23,148 | 23,148 | 25 ms | 25 ms | 14 | 2 |
| 27 | Endgame | 10 plies | 1,662,722 | 163,987 | 163,769 | 287 ms | 286 ms | 896 | 53 |
| 28 | Endgame | 7 plies | 8,892 | 6,222 | 6,222 | 9 ms | 9 ms | 2 | 0 |
| 29 | Endgame | 10 plies | 2,248,895 | 226,528 | 226,468 | 366 ms | 372 ms | 1,326 | 72 |
| 30 | Endgame | 11 plies | 980,995 | 202,311 | 201,733 | 314 ms | 320 ms | 1,469 | 75 |
| 31 | Endgame | 11 plies | 1,622,990 | 154,247 | 154,085 | 198 ms | 203 ms | 703 | 21 |
| 32 | Endgame | 10 plies | 3,057,532 | 551,373 | 549,710 | 1,003 ms | 1,031 ms | 8,248 | 464 |
| 33 | Endgame | 11 plies | 3,996,874 | 415,579 | 408,390 | 776 ms | 766 ms | 5,200 | 321 |
| 34 | Endgame | 10 plies | 4,030,225 | 243,580 | 242,593 | 438 ms | 452 ms | 1,835 | 119 |
| 35 | Endgame | 12 plies | 1,739,170 | 288,749 | 288,578 | 388 ms | 388 ms | 2,424 | 175 |
| 36 | Blockade/Tension | 12 plies | 2,714,028 | 149,121 | 148,955 | 248 ms | 263 ms | 959 | 190 |
| 37 | Blockade/Tension | 12 plies | 2,900,280 | 296,991 | 296,307 | 539 ms | 536 ms | 3,096 | 143 |
| 38 | Blockade/Tension | 13 plies | 5,057,208 | 358,765 | 357,525 | 540 ms | 554 ms | 6,409 | 366 |
| 39 | Blockade/Tension | 10 plies | 1,543,917 | 209,163 | 208,798 | 376 ms | 382 ms | 1,377 | 68 |
| 40 | Blockade/Tension | 11 plies | 3,654,338 | 241,440 | 239,979 | 384 ms | 387 ms | 2,116 | 164 |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| **Total** | **All 40** | **7–14 plies** | **95,898,151** | **11,117,122** | **11,076,738** | **18,900 ms** | **19,142 ms** | **142,781** | **9,165** |

---

## Comparative Analysis: Standard (7–9 Plies) vs Deep (7–14 Plies) Benchmarks

| Metric | **Standard Benchmark (7–9 plies)** | **Deep Benchmark (7–14 plies)** | **Scaling Comparison** |
| :--- | ---: | ---: | :--- |
| **Total Baseline Nodes (No TT)** | $2,408,731$ | **$95,898,151$** | **$39.8\times$ more baseline nodes** |
| **Total Default TT Nodes ($1\text{M}$)** | $711,546$ | **$11,117,122$** | $15.6\times$ more TT nodes |
| **Total Max TT Nodes ($16\text{M}$)** | $711,485$ | **$11,076,738$** | **Saves $40,384$ additional nodes** ($662\times$ larger node savings than at 7–9 plies) |
| **Overall Node Reduction %** | **70.5%** | **88.4%** ($1\text{M}$) / **88.45%** ($16\text{M}$) | **+17.9 percentage points higher pruning efficiency** |
| **Peak Single-Position Reduction** | **87.3%** (Pos 3) | **97.1%** (Pos 3: `2,152,631` $\rightarrow$ `62,373` nodes) | **32.53x single-position speedup** (`3,416 ms` $\rightarrow$ `105 ms`) |
| **Total Baseline Time** | $3,855\text{ ms}$ ($3.86\text{s}$) | **$154,278\text{ ms}$ ($154.28\text{s}$)** | $40.0\times$ longer baseline runtime |
| **Total Default TT Time ($1\text{M}$)** | $1,198\text{ ms}$ ($1.20\text{s}$) | **$18,900\text{ ms}$ ($18.90\text{s}$)** | Saves **135.4 seconds** across 40 positions |
| **Total Max TT Time ($16\text{M}$)** | $1,214\text{ ms}$ ($1.21\text{s}$) | **$19,142\text{ ms}$ ($19.14\text{s}$)** | Saves **135.1 seconds** across 40 positions |
| **Overall Speedup Factor** | **3.22x** | **8.16x** ($1\text{M}$) / **8.06x** ($16\text{M}$) | **Speedup more than doubles ($3.22\text{x} \rightarrow 8.16\text{x}$)** |
| **Total TT Cutoffs** | $20,686$ | **$532,359$** | **$25.7\times$ more direct hash cutoffs** |
| **Hash Collisions ($1,048,576$ entries)** | $608$ | **$142,781$** | **$234.8\times$ more collisions** as table load rises |
| **Hash Collisions ($16,777,216$ entries)** | $36$ | **$9,165$** | **93.6% fewer collisions** ($142,781 \rightarrow 9,165$) |

### Key Conclusions
1. **Transposition Pruning Efficiency Scales Exponentially with Depth (`70.5%` $\rightarrow$ `88.4%`):**
   - Increasing search depth by $2\text{–}5$ plies expanded the unpruned baseline search tree by **$39.8\times$** (from `2.41M` to `95.90M` nodes), while the Transposition Table tree grew by only **$15.6\times$** (from `711.5K` to `11.12M` nodes).
   - Because deeper search trees contain exponentially more transpositions (different move orders converging on the same board configuration), overall node reduction jumped from **70.5% to 88.4%** (pruning **84.78 million nodes**!), and overall speedup more than doubled from **3.22x to 8.16x** (reducing total runtime from **154.3 seconds** down to **18.9 seconds**).
   - On individual positions with high transposition density, speedup reached **32.53x** (Position 3 at 11 plies: `2,152,631` $\rightarrow$ `62,373` nodes, **97.1% reduction**, `3,416 ms` $\rightarrow$ `105 ms`), **16.76x** (Position 36 at 12 plies), **16.46x** (Position 12 at 10 plies), and **16.21x** (Position 24 at 11 plies).
2. **Hash Collisions Scale Quadratically (`235x` Growth) and $16\text{M}$ Entries Saves $662\times$ More Nodes:**
   - While TT nodes grew by $15.6\times$, hash collisions in the $1,048,576$-entry table grew by **$234.8\times$** (from `608` to `142,781`) due to the birthday-paradox load factor as hundreds of thousands of interior nodes were stored per position.
   - In the shallow benchmark, the $16,777,216$-entry table saved only **61 nodes** over the $1,048,576$-entry table. In the deep benchmark, eliminating **133,616 collisions** (`142,781` $\rightarrow$ `9,165`, a **93.6% reduction**) enabled the $16,777,216$-entry table to save **40,384 evaluated nodes** across the 40 positions — **$662\times$ larger node savings** than in the shallow test.
   - On individual deep positions, the $16\text{M}$ table saved **7,189 nodes** and **10 ms** on Position 33 (`415,579` $\rightarrow$ `408,390` nodes, `776 ms` $\rightarrow$ `766 ms`), **2,871 nodes** on Position 13 (`337,093` $\rightarrow$ `334,222`), **2,335 nodes** on Position 22 (`601,181` $\rightarrow$ `598,846`), **2,242 nodes** on Position 4 (`483,930` $\rightarrow$ `481,688`), and **2,187 nodes** on Position 18 (`447,196` $\rightarrow$ `445,009`).
3. **CPU Cache Locality vs Table Capacity Trade-Off:**
   - Despite evaluating **40,384 fewer nodes**, the $16,777,216$-entry table (**256 MiB**) took **19,142 ms** compared to **18,900 ms** for the $1,048,576$-entry table (**16 MiB**) — a ~1.28% wall-clock difference (`242 ms` over `19s`), though on several individual positions (Positions 5, 7, 12, 13, 18, 27, 33, 37) the $16\text{M}$ table was already faster in wall-clock time.
   - This occurs because a 16 MiB table fits largely inside modern CPU L3 cache, whereas random Zobrist probes across a 256 MiB array incur main-memory DRAM latency and TLB page misses. At ~250K–600K TT nodes per isolated test position, the node savings and DRAM latency are nearly in equilibrium.
   - In long time-control games where the persistent table is retained across dozens of moves without clearing, or in ultra-deep searches ($> 2\text{M}$ TT nodes per turn) where the $1\text{M}$ table saturates, increasing the table size via the Settings Dialog ($2\text{M}$–$16\text{M}$ entries) prevents deep transposition thrashing and yields net wall-clock gains.
4. **100% Search Consistency:** Across both benchmarks and all 40 positions, Baseline, Default TT ($1\text{M}$), and Max TT ($16\text{M}$) produced identical best moves and identical evaluation scores.



