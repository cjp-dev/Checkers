# 08 – Transposition table

[Back to the index](README.md)

## In short

Many different move orders lead to the exact same board configuration (a *transposition*). The **transposition table** (hash table) caches the evaluation, depth, search bound, and best move found for every searched position using 64-bit deterministic Zobrist hashing. 

When the search encounters a position again — either across different branches or during successive iterations of **iterative deepening** — the cached entry provides an immediate cutoff or seeds move ordering by trying the proven best move first.

In our empirical benchmark across 40 diverse, non-trivial positions, the transposition table delivered an overall **70.8% reduction in evaluated nodes** (from **2,522,691** baseline nodes down to **737,636** TT nodes) and an average **3.09x speedup** (with peak reductions reaching **92.3%** and **12.45x speedup**), with **zero search divergence**.

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

To scientifically quantify the performance gains of the Transposition Table, an automated empirical benchmark was run across 40 unique, non-trivial positions sampled from self-play under the International Flying Kings rules.

### Methodology
1. **Non-trivial position filtering:** Every position is verified to have a unique 64-bit Zobrist hash, at least 2 legal moves (excluding 0-node forced moves), and genuine multi-ply branching ($\ge 300$ nodes at 5 plies without immediate mate).
2. **Baseline calibration:** For each position, search depth (7 to 9 plies) was calibrated so that the baseline search completed within $\le 10$ seconds.
3. **Comparative run:** The exact same position was searched at identical depth with the Transposition Table enabled.
4. **Verification:** Best moves and evaluation scores were compared to ensure 100% search consistency.

### Benchmark results

| # | Category | Depth | Baseline Nodes | TT Nodes | Node Reduction | Baseline Time | TT Time | Speedup | TT Cutoffs |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Opening | 8 plies | 38,305 | 21,086 | **45.0%** | 79 ms | 44 ms | **1.80x** | 197 |
| 2 | Opening | 8 plies | 58,204 | 24,096 | **58.6%** | 96 ms | 45 ms | **2.13x** | 222 |
| 3 | Opening | 7 plies | 44,913 | 6,195 | **86.2%** | 69 ms | 12 ms | **5.75x** | 42 |
| 4 | Opening | 7 plies | 67,449 | 17,927 | **73.4%** | 111 ms | 35 ms | **3.17x** | 321 |
| 5 | Opening | 7 plies | 69,129 | 9,540 | **86.2%** | 106 ms | 18 ms | **5.89x** | 175 |
| 6 | Opening | 8 plies | 70,060 | 25,735 | **63.3%** | 108 ms | 43 ms | **2.51x** | 610 |
| 7 | Opening | 7 plies | 87,714 | 6,766 | **92.3%** | 137 ms | 11 ms | **12.45x** | 41 |
| 8 | Opening | 7 plies | 48,374 | 7,890 | **83.7%** | 74 ms | 13 ms | **5.69x** | 64 |
| 9 | Opening | 7 plies | 59,062 | 13,974 | **76.3%** | 92 ms | 23 ms | **4.00x** | 213 |
| 10 | Opening | 7 plies | 46,247 | 8,585 | **81.4%** | 68 ms | 13 ms | **5.23x** | 62 |
| 11 | Middlegame | 8 plies | 42,472 | 16,203 | **61.9%** | 58 ms | 27 ms | **2.15x** | 652 |
| 12 | Middlegame | 7 plies | 53,188 | 11,666 | **78.1%** | 84 ms | 21 ms | **4.00x** | 263 |
| 13 | Middlegame | 7 plies | 134,182 | 35,519 | **73.5%** | 187 ms | 61 ms | **3.07x** | 496 |
| 14 | Middlegame | 7 plies | 56,142 | 10,008 | **82.2%** | 81 ms | 16 ms | **5.06x** | 345 |
| 15 | Middlegame | 7 plies | 69,640 | 31,815 | **54.3%** | 104 ms | 52 ms | **2.00x** | 807 |
| 16 | Middlegame | 8 plies | 11,328 | 7,462 | **34.1%** | 16 ms | 12 ms | **1.33x** | 116 |
| 17 | Middlegame | 8 plies | 36,601 | 10,266 | **72.0%** | 58 ms | 17 ms | **3.41x** | 153 |
| 18 | Middlegame | 7 plies | 41,710 | 6,496 | **84.4%** | 59 ms | 10 ms | **5.90x** | 137 |
| 19 | Middlegame | 8 plies | 55,338 | 22,599 | **59.2%** | 80 ms | 34 ms | **2.35x** | 651 |
| 20 | Middlegame | 8 plies | 118,841 | 32,487 | **72.7%** | 166 ms | 48 ms | **3.46x** | 818 |
| 21 | Middlegame | 8 plies | 43,885 | 12,158 | **72.3%** | 61 ms | 18 ms | **3.39x** | 314 |
| 22 | Middlegame | 8 plies | 17,425 | 11,383 | **34.7%** | 23 ms | 18 ms | **1.28x** | 565 |
| 23 | Middlegame | 7 plies | 61,587 | 12,579 | **79.6%** | 92 ms | 24 ms | **3.83x** | 314 |
| 24 | Middlegame | 8 plies | 150,178 | 34,847 | **76.8%** | 217 ms | 63 ms | **3.44x** | 492 |
| 25 | Middlegame | 8 plies | 67,300 | 24,042 | **64.3%** | 115 ms | 43 ms | **2.67x** | 506 |
| 26 | Endgame | 8 plies | 48,927 | 23,148 | **52.7%** | 49 ms | 25 ms | **1.96x** | 1,083 |
| 27 | Endgame | 7 plies | 46,614 | 16,010 | **65.7%** | 72 ms | 27 ms | **2.67x** | 254 |
| 28 | Endgame | 9 plies | 8,892 | 6,222 | **30.0%** | 13 ms | 8 ms | **1.62x** | 326 |
| 29 | Endgame | 7 plies | 51,929 | 16,865 | **67.5%** | 92 ms | 29 ms | **3.17x** | 728 |
| 30 | Endgame | 8 plies | 48,050 | 15,962 | **66.8%** | 53 ms | 18 ms | **2.94x** | 643 |
| 31 | Endgame | 8 plies | 95,291 | 20,585 | **78.4%** | 84 ms | 21 ms | **4.00x** | 1,550 |
| 32 | Endgame | 7 plies | 78,917 | 20,366 | **74.2%** | 105 ms | 35 ms | **3.00x** | 574 |
| 33 | Endgame | 8 plies | 75,185 | 31,368 | **58.3%** | 118 ms | 58 ms | **2.03x** | 714 |
| 34 | Endgame | 7 plies | 90,965 | 16,121 | **82.3%** | 138 ms | 26 ms | **5.31x** | 608 |
| 35 | Endgame | 8 plies | 29,293 | 13,406 | **54.2%** | 35 ms | 18 ms | **1.94x** | 355 |
| 36 | Blockade/Tension | 8 plies | 43,113 | 17,679 | **59.0%** | 65 ms | 27 ms | **2.41x** | 409 |
| 37 | Blockade/Tension | 8 plies | 25,695 | 10,331 | **59.8%** | 38 ms | 16 ms | **2.38x** | 581 |
| 38 | Blockade/Tension | 8 plies | 33,991 | 13,149 | **61.3%** | 42 ms | 18 ms | **2.33x** | 350 |
| 39 | Blockade/Tension | 8 plies | 160,896 | 61,520 | **61.8%** | 272 ms | 106 ms | **2.57x** | 3,171 |
| 40 | Blockade/Tension | 8 plies | 135,659 | 33,580 | **75.2%** | 209 ms | 54 ms | **3.87x** | 1,146 |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| **Total** | **All 40** | **7–9 plies** | **2,522,691** | **737,636** | **70.8%** | **3,726 ms** | **1,207 ms** | **3.09x** | **21,068** |

### Key observations
1. **100% Non-trivial search trees:** All 40 positions have multiple legal moves ($\ge 2$) and unique Zobrist hashes, evaluating between **8,892 and 160,896 baseline nodes** per position (totaling **2.52 million baseline nodes**).
2. **Consistent pruning across all game phases:** Every single position in the 40-position suite achieved substantial node reductions (ranging from **30.0% to 92.3%**, averaging **70.8%**) and recorded **21,068 direct hash cutoffs**.
3. **Top speedup factor:** In position 7, the search achieved a **12.45x speedup** (from 137 ms down to 11 ms), converting an 87,714-node search into just 6,766 nodes.
4. **Search consistency:** In 100% of tested positions, both search runs selected identical optimal moves and matching evaluation scores.
