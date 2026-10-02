# 08 – Transposition table

[Back to the index](README.md)

## In short

Many different move orders lead to the exact same board configuration (a *transposition*). The **transposition table** (hash table) caches the evaluation, depth, search bound, and best move found for every searched position using 64-bit deterministic Zobrist hashing. 

When the search encounters a position again — either across different branches or during successive iterations of **iterative deepening** — the cached entry provides an immediate cutoff or seeds move ordering by trying the proven best move first.

In our empirical benchmark across 40 diverse positions, the transposition table delivered an overall **71.9% reduction in evaluated nodes** and an average **2.88x speedup** (with peak reductions exceeding **92%** and **10.6x speedup**), with **zero search divergence**.

---

## The entry

[TranspositionTable.cs](../../src/Checkers.Core/AI/TranspositionTable.cs):

```csharp
public struct TranspositionEntry
{
    public ulong Key;            // 64-bit Zobrist hash
    public int Score;            // Evaluation score (ply-normalized for mate/win)
    public int Depth;            // Remaining search depth when stored
    public TranspositionBound Bound; // Exact (0), LowerBound (1), UpperBound (2)
    public byte Age;             // Generation index for replacement aging
    public Position BestMoveFrom;// Source square of the principal move
    public Position BestMoveTo;  // Target square of the principal move
}
```

### Search bounds

| Bound | Condition | Meaning |
|---|---|---|
| `Exact` | $\alpha < \text{score} < \beta$ | True minimax score within the current search window. |
| `LowerBound` | $\text{score} \ge \beta$ | Beta-cutoff: the position was so good for the player that the opponent would never allow this branch. True score is at least this value. |
| `UpperBound` | $\text{score} \le \alpha$ | Alpha fail-low: no move was able to improve upon alpha. True score is at most this value. |

---

## Memory layout & sizing

The table is sized to an exact power-of-two number of entries, allowing ultra-fast bitwise masking instead of modulo division:

$$
\text{index} = \text{key} \land \text{mask}
$$

| Allocation | Entries | Approximate Memory | Use Case |
|---|---|---|---|
| **Default** | $2^{20} = 1,048,576$ | ~32 MiB | Desktop and Web client |
| **High Performance** | $2^{21} = 2,097,152$ | ~64 MiB | Deep tournament analysis |
| **Lightweight / Tests** | $2^{16} = 65,536$ | ~2 MiB | Unit tests & fast verification |

---

## Replacement strategy

Each slot holds one entry. When a new entry maps to an occupied slot:
1. **Same position:** Replace immediately with the updated depth or tighter bound.
2. **Older age:** Replace entries stored during earlier searches (`existing.Age != currentAge`).
3. **Depth-preferred:** Replace if the new search explored deeper or equal depth (`depth >= existing.Depth`).

Calling `NewSearch()` at the start of each player move increments the generation age. This ensures that old positions from earlier moves in the game are replaced first, while relevant positions from the current thinking turn are protected.

---

## Score normalization (win distance)

When a forced win or loss is detected ($|\text{score}| \ge 90,000$), storing the raw score would distort the win distance if probed from a different ply depth:
- **Storing:** $\text{storedScore} = \text{score} + \text{ply}$ (for wins) or $\text{score} - \text{ply}$ (for losses).
- **Retrieving:** $\text{retrievedScore} = \text{storedScore} - \text{ply}$ (for wins) or $\text{storedScore} + \text{ply}$ (for losses).

This preserves exact mate-in-$N$ distance invariant across all search depths and branches.

---

## Empirical benchmark (40 diverse positions)

To scientifically quantify the performance gains of the Transposition Table, an automated empirical benchmark was run across 40 diverse positions sampled from self-play under the International Flying Kings rules.

### Methodology
1. **Baseline calibration:** For each position, search depth was calibrated so that the baseline search completed within $\le 10$ seconds.
2. **Comparative run:** The exact same position was searched at identical depth with the Transposition Table enabled.
3. **Verification:** Best moves and evaluation scores were compared to ensure 100% search consistency.

### Benchmark results

| # | Category | Depth | Baseline Nodes | TT Nodes | Node Reduction | Baseline Time | TT Time | Speedup | TT Cutoffs |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Opening | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 2 | Opening | 7 plies | 10,821 | 7,876 | **27.2%** | 107 ms | 80 ms | **1.34x** | 82 |
| 3 | Opening | 7 plies | 5,700 | 2,318 | **59.3%** | 53 ms | 31 ms | **1.71x** | 18 |
| 4 | Opening | 7 plies | 7,885 | 4,069 | **48.4%** | 14 ms | 8 ms | **1.75x** | 64 |
| 5 | Opening | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 6 | Opening | 7 plies | 23,714 | 9,175 | **61.3%** | 37 ms | 18 ms | **2.06x** | 205 |
| 7 | Opening | 7 plies | 87,714 | 6,766 | **92.3%** | 138 ms | 13 ms | **10.62x** | 41 |
| 8 | Opening | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 9 | Opening | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 10 | Opening | 7 plies | 18,678 | 5,294 | **71.7%** | 32 ms | 9 ms | **3.56x** | 49 |
| 11 | Middlegame | 7 plies | 8,636 | 3,093 | **64.2%** | 12 ms | 5 ms | **2.40x** | 71 |
| 12 | Middlegame | 7 plies | 53,188 | 11,666 | **78.1%** | 89 ms | 18 ms | **4.94x** | 263 |
| 13 | Middlegame | 7 plies | 134,182 | 35,519 | **73.5%** | 187 ms | 56 ms | **3.34x** | 496 |
| 14 | Middlegame | 7 plies | 56,142 | 10,008 | **82.2%** | 80 ms | 14 ms | **5.71x** | 345 |
| 15 | Middlegame | 7 plies | 69,640 | 31,815 | **54.3%** | 103 ms | 51 ms | **2.02x** | 807 |
| 16 | Middlegame | 7 plies | 3,712 | 2,388 | **35.7%** | 5 ms | 3 ms | **1.67x** | 56 |
| 17 | Middlegame | 7 plies | 15,470 | 5,410 | **65.0%** | 23 ms | 8 ms | **2.88x** | 52 |
| 18 | Middlegame | 7 plies | 41,710 | 6,496 | **84.4%** | 60 ms | 11 ms | **5.45x** | 137 |
| 19 | Middlegame | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 20 | Middlegame | 7 plies | 31,400 | 14,145 | **55.0%** | 44 ms | 21 ms | **2.10x** | 268 |
| 21 | Middlegame | 7 plies | 18,803 | 7,682 | **59.1%** | 25 ms | 11 ms | **2.27x** | 115 |
| 22 | Middlegame | 7 plies | 9,033 | 6,482 | **28.2%** | 12 ms | 8 ms | **1.50x** | 244 |
| 23 | Middlegame | 7 plies | 61,587 | 12,579 | **79.6%** | 91 ms | 20 ms | **4.55x** | 314 |
| 24 | Middlegame | 7 plies | 36,218 | 8,757 | **75.8%** | 56 ms | 14 ms | **4.00x** | 110 |
| 25 | Middlegame | 7 plies | 26,460 | 8,655 | **67.3%** | 42 ms | 16 ms | **2.62x** | 150 |
| 26 | Endgame | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 27 | Endgame | 7 plies | 1,500 | 1,342 | **10.5%** | 2 ms | 1 ms | **2.00x** | 76 |
| 28 | Endgame | 7 plies | 25,455 | 8,443 | **66.8%** | 40 ms | 13 ms | **3.08x** | 115 |
| 29 | Endgame | 7 plies | 25,455 | 8,443 | **66.8%** | 38 ms | 14 ms | **2.71x** | 115 |
| 30 | Endgame | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 31 | Endgame | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 32 | Endgame | 7 plies | 14,413 | 8,263 | **42.7%** | 26 ms | 17 ms | **1.53x** | 262 |
| 33 | Endgame | 7 plies | 25,455 | 8,443 | **66.8%** | 37 ms | 16 ms | **2.31x** | 115 |
| 34 | Endgame | 7 plies | 10,384 | 5,179 | **50.1%** | 14 ms | 6 ms | **2.33x** | 678 |
| 35 | Endgame | 7 plies | 4,829 | 4,119 | **14.7%** | 7 ms | 5 ms | **1.40x** | 356 |
| 36 | Blockade/Tension | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 37 | Blockade/Tension | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 38 | Blockade/Tension | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
| 39 | Blockade/Tension | 7 plies | 85,130 | 12,022 | **85.9%** | 101 ms | 17 ms | **5.94x** | 333 |
| 40 | Blockade/Tension | 7 plies | 0 | 0 | **0.0%** | 1 ms | 1 ms | **1.00x** | 0 |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| **Total** | **All 40** | **7 plies** | **913,314** | **256,447** | **71.9%** | **1,487 ms** | **516 ms** | **2.88x** | **5,937** |

### Key observations
1. **Forced moves (0 nodes):** In positions where only 1 legal capture or move exists (such as positions 1, 5, 8, 9, 19, 26, 30, 31, 36–38, 40), the engine immediately plays the forced move in 1 ms without launching full search.
2. **Massive pruning in branching positions:** Across complex tactical middlegames (positions 13, 14, 18, 23, 39), the transposition table eliminated between **73% and 86%** of all search nodes.
3. **Top speedup factor:** In position 7, the search achieved a **10.62x speedup** (from 138 ms down to 13 ms), converting an 87,714-node search into just 6,766 nodes.
4. **Consistency:** In 100% of tested positions, both search runs selected identical optimal moves and matching scores.
