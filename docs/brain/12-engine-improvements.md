# 12 – Engine improvements during development

[Back to the index](README.md)

## In short

During the development of `Checkers.Core`, the AI engine evolved through **nine rigorously benchmarked engineering milestones**—starting from an initial object-oriented `Piece?[8, 8]` array prototype and progressing to a zero-allocation 64-bit bitboard engine equipped with a 4-way set-associative cache-line transposition table, hardware `Sse.Prefetch0` prefetching, Principal Variation Search (PVS), Killer/History move ordering, path-aware repetition detection (`DrawTable`), Reverse Futility Pruning (RFP), Futility Pruning (FP), two-stage Verified Late Move Reductions (LMR), root-level partial-iteration adoption & dominant-move early termination, a hardware `POPCNT`-vectorized static evaluation overhaul (`board_eval.c` 1:1 port for English Checkers + Flying Kings adaptation for International Draughts), and embedded 12-ply Drop-Out Expansion (DOE) opening books (`32,369` English / `26,367` International positions).

Every milestone was validated against automated benchmark suites (`tools/Checkers.Benchmark`) to verify search correctness, measure exact node reductions, and quantify wall-clock throughput (`nodes/s`), search depth gains, and head-to-head self-play Elo strength:
* **Raw Tree-Walk Throughput:** Increased from **`0.64M–0.66M nodes/s`** in the `Piece?[8, 8]` array engine to **`32.83M–37.18M nodes/s`** in the bitboard engine (**51×–65× raw speedup** with 100% node-for-node equivalence).
* **40-Position Deep Fixed-Depth Suite ($7\text{–}14$ plies):** Total runtime across all 40 deep International positions dropped from **`148,822 ms` ($2.48\text{ minutes}$)** in the uncached array baseline down to **`193 ms` ($0.19\text{ seconds}$)** in Phase 6 (**771× total speedup**), and in English Checkers from **`142,038 ms` down to `173 ms` (821× total speedup; 875× No-TT speedup)**.
* **5-Position Timed Suite ($5.0\text{ s}$ per position, $16\text{M}$ TT):** Completed iterative deepening depth increased by **+4 to +5 plies** across every position (reaching **21 to 25 plies**, plus partial depth-22 root adoption on `Pos #1`), enabling the engine to solve a **21-ply forced win** on Position #40 in **`< 2.2 seconds`**.
* **200-Game Bot-vs-Bot Self-Play Verification (`1000 ms/move`, 50 Balanced Opening Ballots × 2 Sides per Variant):** The Phase 6 static evaluation overhaul defeated the baseline evaluation by **`69.5 – 30.5` (`+41 =57 -2`, `+143.1 ± 42.9 Elo`, `100.0% LOS`)** in **English Checkers** and **`56.5 – 43.5` (`+22 =69 -9`, `+45.4 ± 37.8 Elo`, `99.2% LOS`)** in **International (Flying Kings) Checkers** (`126.0 / 200` combined, `+63 =126 -11`).
* **Embedded 12-Ply DOE Opening Books (Phase 7):** Built across 3 stages of innovation—Stage 1 Top-Down Multi-PV ($k=3$) DAG to `lv 8` (`999` / `995` positions), Stage 2 Full-Width Early Plies (`lv 0..3`, `149` positions), and Stage 3 Depth-First Drop-Out Expansion (`lv 4..12`, search depth $d=16$)—yielding **`32,369` English** and **`26,367` International** book positions (`0` Negamax back-up errors).

> [!NOTE]
> **Engineering Inspiration ([`Stermere/Checkers-Engine`](https://github.com/Stermere/Checkers-Engine)):** Phases 1 through 6 of this progression—as well as our node-equality verification gate, fixed-depth node-reduction benchmarks, and paired-ballot bot-vs-bot Elo harness (`EvaluationMatchRunner.cs`)—were directly inspired by Collin Kees's open-source C engine [**Checkers-Engine (Marcher Engine)**](https://github.com/Stermere/Checkers-Engine) (included locally in [`Checkers-Engine-main/`](../../Checkers-Engine-main/)). Specifically, Phase 1 adapted `has_any_jump` and 16-bit `(from << 8) | to` move packing; Phase 2 adapted the 16-byte 4-way cache-line bucket layout, victim replacement formula, and `_mm_prefetch` from [`hash_table.c`](../../Checkers-Engine-main/src/engine/hash_table.c); Phase 3 adapted [`killer_table.c`](../../Checkers-Engine-main/src/engine/killer_table.c), [`draw_table.c`](../../Checkers-Engine-main/src/engine/draw_table.c), and `USE_PVS` / `USE_HISTORY` from [`board_search.c`](../../Checkers-Engine-main/src/engine/board_search.c); Phase 4 adapted `USE_RFP`, `USE_FUTILITY`, and `USE_VERIFIED_LMR` from [`board_search.c`](../../Checkers-Engine-main/src/engine/board_search.c); Phase 5 adapted `USE_PARTIAL_ITERATION` and `TERMINATE_EARLY_THRESHOLD` from [`board_search.c`](../../Checkers-Engine-main/src/engine/board_search.c); and Phase 6 ported the handcrafted static evaluator from [`board_eval.c`](../../Checkers-Engine-main/src/engine/board_eval.c) and validated it using a paired-ballot match methodology modeled on [`bot_vs_bot.py`](../../Checkers-Engine-main/src/python/bot_vs_bot.py) and [`bench_eval_speed.py`](../../Checkers-Engine-main/src/python/nnue/bench_eval_speed.py).

![Checkers Engine Evolution: From 2D Array Prototype to Phase 5 Bitboard Engine](images/engine-evolution-chart.svg)

---

## Development Roadmap & Cumulative Progression

```mermaid
flowchart LR
    M0["Baseline Prototype<br/>Piece?[8,8] Array Engine<br/>0.65M NPS • 148.8s (40 Deep)"] --> M1["Milestone 1: Bitboards<br/>4×ulong + Copy-Make + POPCNT<br/>35.6M NPS • 50x–65x Faster"]
    M1 --> M2["Milestone 2: Zobrist TT<br/>1M–16M Power-of-Two Cache<br/>−88.5% Nodes • 8.2x Pruning"]
    M2 --> P1["Milestone 3 (Phase 1): Speed<br/>O(1) HasAnyCapture + Ray Guards<br/>+6.2%..10.9% TT Throughput"]
    P1 --> P2["Milestone 4 (Phase 2): 4-Way TT<br/>64B Cache Buckets + Sse.Prefetch0<br/>−99.9%..100% Collisions"]
    P2 --> P3["Milestone 5 (Phase 3): Stage A<br/>Exact PVS + Killers + History<br/>−64.9% Nodes (No-TT) / −28.5% (TT)"]
    P3 --> P4["Milestone 6 (Phase 4): Stage B<br/>Verified LMR + RFP + FP<br/>−93.7% Nodes • 21–25 Plies in 5s"]
    P4 --> P5["Milestone 7 (Phase 5): Root &amp; Time<br/>Partial Iteration Adoption<br/>+ Dominant Move Early Exit"]
    P5 --> P6["Milestone 8 (Phase 6): Static Eval<br/>board_eval.c + Flying Kings Adaptation<br/>+143.1 Elo (Eng) • +45.4 Elo (Intl)"]
    P6 --> P7["Milestone 9 (Phase 7): Opening Book<br/>Full-Width lv 0..3 + DOE to lv 12<br/>32,369 Eng • 26,367 Intl Nodes"]
```

### Executive Summary: 40-Position Deep Suite ($7\text{–}14$ Plies) Across All Milestones

| Milestone / Engine Stage | Variant | Mode | Evaluated Nodes | Node Reduction vs. Baseline | Total Time | Throughput (NPS) | Cumulative Speedup |
|---|---|---|---:|---:|---:|---:|---:|
| **0A. Array Engine Prototype** | International | No-TT | `95,811,314` | — | `148,822 ms` | `0.64M/s` | `1.00×` |
| **0B. Array Engine + 1M TT** | International | 1M TT | `10,962,720` | `-88.56%` | `17,941 ms` | `0.61M/s` | `8.30×` |
| **Phase 0. Bitboard Baseline** | International | No-TT | `95,811,314` | `0.00% (Exact)` | `2,951 ms` | `32.47M/s` | `50.43×` |
| **Phase 0. Bitboard Baseline** | International | 1M TT | `10,962,720` | `-88.56% (Exact)` | `714 ms` | `15.35M/s` | `208.43×` |
| **Phase 1. Speed Optimizations** | International | 1M TT | `10,962,720` | `-88.56% (Exact)` | `672 ms` | `16.30M/s` | `221.46×` |
| **Phase 2. 4-Way Bucket TT + Prefetch** | International | 1M TT | `10,956,192` | `-88.56% (Exact)` | `638 ms` | `17.18M/s` | `233.26×` |
| **Phase 3. Stage A Exact (PVS + Killers)** | International | No-TT | `33,933,396` | `-64.58% (Exact)` | `1,367 ms` | `24.82M/s` | `108.87×` |
| **Phase 3. Stage A Exact (PVS + Killers)** | International | 1M TT | `7,840,974` | `-91.82% (Exact)` | `548 ms` | `14.31M/s` | `271.57×` |
| **Phase 4. Stage B Selective (LMR/RFP/FP)** | International | No-TT | `6,727,472` | `-92.98% (14.2×)` | `458 ms` | `14.69M/s` | `324.94×` |
| **Phase 4. Stage B Selective (LMR/RFP/FP)** | International | 1M TT | `2,349,717` | `-97.55% (40.8×)` | `246 ms` | `9.55M/s` | `604.97×` |
| **Phase 5. Root & Time Management** | International | No-TT | **`6,727,472`** | **`-92.98% (14.2×)`** | **`433 ms`** | **`15.51M/s`** | **`343.70×`** |
| **Phase 5. Root & Time Management** | International | 1M TT | **`2,349,717`** | **`-97.55% (40.8×)`** | **`236 ms`** | **`9.94M/s`** | **`630.60×`** |
| **0A. Array Engine Prototype** | English | No-TT | `94,110,293` | — | `142,038 ms` | `0.66M/s` | `1.00×` |
| **Phase 0. Bitboard Baseline** | English | No-TT | `94,110,293` | `0.00% (Exact)` | `2,641 ms` | `35.63M/s` | `53.78×` |
| **Phase 2. 4-Way Bucket TT + Prefetch** | English | 1M TT | `9,944,217` | `-89.43% (Exact)` | `557 ms` | `17.85M/s` | `255.01×` |
| **Phase 3. Stage A Exact (PVS + Killers)** | English | 1M TT | `7,324,657` | `-92.22% (Exact)` | `447 ms` | `16.39M/s` | `317.76×` |
| **Phase 4 & 5. Stage B + Root/Time** | English | No-TT | **`5,922,594`** | **`-93.71% (15.9×)`** | **`300 ms`** | **`19.70M/s`** | **`473.46×`** |
| **Phase 4 & 5. Stage B + Root/Time** | English | 1M TT | **`2,083,260`** | **`-97.79% (45.2×)`** | **`173 ms`** | **`12.04M/s`** | **`821.03×`** |

### Executive Summary: 5-Position Timed Suite ($5.0\text{ s}$ Budget / Position, $16\text{M}$ TT, International)

| Position | Category | Phase 0 Depth | Phase 1 Depth | Phase 2 Depth | Phase 3 (Stage A Exact) | **Phase 4 (Stage B Selective)** | Total Depth Gain | Phase 4 Best Move & Score |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Pos #1** | Opening | `17 plies` | `17 plies` | `17 plies` | `18 plies (+1)` | **`21 plies`** | **+4 plies** | `9-14` (`+5`) |
| **Pos #13** | Middlegame | `16 plies` | `17 plies (+1)` | `17 plies (+1)` | `17 plies (+1)` | **`21 plies`** | **+5 plies** | `28-24` (`+7`) |
| **Pos #20** | Middlegame | `18 plies` | `18 plies` | `18 plies` | `20 plies (+2)` | **`22 plies`** | **+4 plies** | `12-16` (`0`) |
| **Pos #32** | Endgame | `20 plies` | `20 plies` | `20 plies` | `20 plies` | **`25 plies`** | **+5 plies** | `18-14` (`+439`) |
| **Pos #40** | Blockade/Tension | `18 plies` | `18 plies` | `18 plies` | `19 plies (+1)` | **`22 plies`** *(Solved Mate!)* | **+4 plies** | `29x8` (**`+Win in 23 plies` in `2.15s`**) |

---

## Milestone 1: 64-Bit Bitboard Refactor (Array vs. Bitboard Engine)

Detailed architectural documentation: [Chapter 11 – Bitboards](11-bitboards.md).

In Milestone 1, the heap-allocated `Piece?[8, 8]` board representation and `List<Move>` generator were replaced by a value-type 64-bit bitboard engine (`BitPosition`, `BitMove`, `BitboardMoveGenerator`, and `POPCNT`-based `EvaluationFunction`). Across all 40 benchmark positions at both standard ($7\text{–}9$ plies) and deep ($7\text{–}14$ plies) search depths, the bitboard engine visited the **exact same number of nodes** while running **50.4× to 65.1× faster** without TT and **23.6× to 25.6× faster** with the 1M TT.

### Summary Across All 40 Positions (Standard 7–9 Plies & Deep 7–14 Plies)

| Suite | Variant | Mode | Array Nodes | Bitboard Nodes | Node Equivalence | Array Time | Bitboard Time | Speedup | Bitboard NPS |
|---|---|---|---:|---:|:---:|---:|---:|---:|---:|
| **Standard (7–9p)** | **International** | **No-TT** | `2,317,030` | `2,317,030` | **100% (40/40)** | `3,776 ms` | **`58 ms`** | **65.10×** | `39.95M/s` |
| **Standard (7–9p)** | **International** | **1M TT** | `664,471` | `664,471` | **100% (40/40)** | `1,128 ms` | **`47 ms`** | **24.00×** | `14.14M/s` |
| **Standard (7–9p)** | **English** | **No-TT** | `2,454,069` | `2,454,069` | **100% (40/40)** | `3,857 ms` | **`61 ms`** | **63.23×** | `40.23M/s` |
| **Standard (7–9p)** | **English** | **1M TT** | `665,966` | `665,966` | **100% (40/40)** | `1,088 ms` | **`46 ms`** | **23.65×** | `14.48M/s` |
| **Deep (7–14p)** | **International** | **No-TT** | `95,811,314` | `95,811,314` | **100% (40/40)** | `148,822 ms` | **`2,951 ms`** | **50.43×** | **`32.47M/s`** |
| **Deep (7–14p)** | **International** | **1M TT** | `10,962,720` | `10,962,720` | **100% (40/40)** | `17,941 ms` | **`714 ms`** | **25.13×** | **`15.35M/s`** |
| **Deep (7–14p)** | **English** | **No-TT** | `94,110,293` | `94,110,293` | **100% (40/40)** | `142,038 ms` | **`2,641 ms`** | **53.78×** | **`35.63M/s`** |
| **Deep (7–14p)** | **English** | **1M TT** | `9,974,305` | `9,974,305` | **100% (40/40)** | `16,342 ms` | **`638 ms`** | **25.61×** | **`15.63M/s`** |

### Analytical Observations

1. **50×–65× Raw Tree-Walk Acceleration (No-TT):**
   Without a transposition table, the array engine spends nearly 2.5 minutes (`148.8s` in International, `142.0s` in English) evaluating ~95M nodes (`~644k–662k nodes/s`), bound by `Piece?[8, 8]` heap cloning, `List<Move>` allocations, and 64-square loops. The bitboard engine walks the exact same 95M-node trees in **2.95s** (`International`, **32.47M nodes/s**) and **2.64s** (`English`, **35.63M nodes/s**).
2. **Amdahl's Law & DRAM Latency with TT Enabled (~25× vs. ~52×):**
   When the 1M-entry (16 MiB) Transposition Table is enabled, every node probes and stores a 16-byte `TranspositionEntry` in L3/main memory. In the array engine, a 15–20 ns L3/DRAM access is negligible compared to ~1,550 ns of per-node array cloning. In the bitboard engine, where move generation, copy-make, and `PopCount` evaluation take only **~28–30 ns per node**, the L3/DRAM transposition table probe/store accounts for roughly half of the per-node execution time (`~65 ns/node` total, or **~15.5M nodes/s**). Even so, `Bitboard + 1M TT` finishes all 40 deep positions in **0.71s** (`International`) and **0.64s** (`English`)—a **208×–222× combined speedup** over `Array No-TT`.

### Table 1A: International Draughts — Deep No-TT Baseline (Array vs. Bitboard, 7–14 Plies)

| # | Category | Depth | Array Nodes (No-TT) | Bitboard Nodes (No-TT) | Array Time | Bitboard Time | Speedup | Identical |
|---|:---:|:---:|---:|---:|---:|---:|---:|:---:|
| 1 | Opening | 11 plies | 1,364,775 | 1,364,775 | 2,423 ms | 134 ms | **18.08x** | Yes |
| 2 | Opening | 12 plies | 2,268,109 | 2,268,109 | 3,806 ms | 69 ms | **55.16x** | Yes |
| 3 | Opening | 11 plies | 2,152,631 | 2,152,631 | 3,494 ms | 67 ms | **52.15x** | Yes |
| 4 | Opening | 10 plies | 3,862,989 | 3,862,989 | 6,496 ms | 111 ms | **58.52x** | Yes |
| 5 | Opening | 10 plies | 2,204,847 | 2,204,847 | 3,817 ms | 65 ms | **58.72x** | Yes |
| 6 | Opening | 11 plies | 1,540,643 | 1,540,643 | 2,653 ms | 45 ms | **58.96x** | Yes |
| 7 | Opening | 10 plies | 2,267,162 | 2,267,162 | 3,989 ms | 70 ms | **56.99x** | Yes |
| 8 | Opening | 10 plies | 1,915,942 | 1,915,942 | 3,276 ms | 59 ms | **55.53x** | Yes |
| 9 | Opening | 10 plies | 1,662,035 | 1,662,035 | 2,908 ms | 47 ms | **61.87x** | Yes |
| 10 | Opening | 10 plies | 1,742,411 | 1,742,411 | 2,825 ms | 48 ms | **58.85x** | Yes |
| 11 | Middlegame | 12 plies | 2,074,281 | 2,074,281 | 2,997 ms | 51 ms | **58.76x** | Yes |
| 12 | Middlegame | 10 plies | 2,101,525 | 2,101,525 | 3,069 ms | 55 ms | **55.80x** | Yes |
| 13 | Middlegame | 10 plies | 2,763,961 | 2,763,961 | 4,107 ms | 78 ms | **52.65x** | Yes |
| 14 | Middlegame | 10 plies | 2,723,163 | 2,723,163 | 3,904 ms | 70 ms | **55.77x** | Yes |
| 15 | Middlegame | 10 plies | 2,115,795 | 2,115,795 | 3,466 ms | 60 ms | **57.77x** | Yes |
| 16 | Middlegame | 14 plies | 2,595,254 | 2,595,254 | 3,825 ms | 84 ms | **45.54x** | Yes |
| 17 | Middlegame | 12 plies | 2,799,308 | 2,799,308 | 4,481 ms | 108 ms | **41.49x** | Yes |
| 18 | Middlegame | 11 plies | 4,329,327 | 4,329,327 | 6,165 ms | 120 ms | **51.38x** | Yes |
| 19 | Middlegame | 12 plies | 3,506,277 | 3,506,277 | 4,825 ms | 112 ms | **43.08x** | Yes |
| 20 | Middlegame | 11 plies | 4,253,714 | 4,253,714 | 6,008 ms | 113 ms | **53.17x** | Yes |
| 21 | Middlegame | 13 plies | 2,674,334 | 2,674,334 | 3,739 ms | 88 ms | **42.49x** | Yes |
| 22 | Middlegame | 14 plies | 2,560,701 | 2,560,701 | 3,658 ms | 75 ms | **48.77x** | Yes |
| 23 | Middlegame | 10 plies | 2,632,773 | 2,632,773 | 4,027 ms | 72 ms | **55.93x** | Yes |
| 24 | Middlegame | 11 plies | 3,509,716 | 3,509,716 | 5,410 ms | 104 ms | **52.02x** | Yes |
| 25 | Middlegame | 11 plies | 1,757,145 | 1,757,145 | 2,890 ms | 59 ms | **48.98x** | Yes |
| 26 | Endgame | 8 plies | 48,927 | 48,927 | 58 ms | 1 ms | **58.00x** | Yes |
| 27 | Endgame | 10 plies | 1,662,722 | 1,662,722 | 2,895 ms | 55 ms | **52.64x** | Yes |
| 28 | Endgame | 7 plies | 8,892 | 8,892 | 18 ms | 1 ms | **18.00x** | Yes |
| 29 | Endgame | 10 plies | 2,248,895 | 2,248,895 | 3,434 ms | 63 ms | **54.51x** | Yes |
| 30 | Endgame | 11 plies | 980,995 | 980,995 | 1,350 ms | 31 ms | **43.55x** | Yes |
| 31 | Endgame | 11 plies | 1,622,990 | 1,622,990 | 1,934 ms | 41 ms | **47.17x** | Yes |
| 32 | Endgame | 9 plies | 1,648,479 | 1,648,479 | 2,319 ms | 43 ms | **53.93x** | Yes |
| 33 | Endgame | 11 plies | 3,996,874 | 3,996,874 | 6,758 ms | 144 ms | **46.93x** | Yes |
| 34 | Endgame | 10 plies | 4,030,225 | 4,030,225 | 7,398 ms | 121 ms | **61.14x** | Yes |
| 35 | Endgame | 12 plies | 1,739,170 | 1,739,170 | 2,283 ms | 49 ms | **46.59x** | Yes |
| 36 | Blockade/Tension | 12 plies | 2,714,028 | 2,714,028 | 4,273 ms | 88 ms | **48.56x** | Yes |
| 37 | Blockade/Tension | 12 plies | 2,900,280 | 2,900,280 | 4,915 ms | 100 ms | **49.15x** | Yes |
| 38 | Blockade/Tension | 12 plies | 1,596,534 | 1,596,534 | 2,225 ms | 42 ms | **52.98x** | Yes |
| 39 | Blockade/Tension | 10 plies | 3,579,147 | 3,579,147 | 5,692 ms | 108 ms | **52.70x** | Yes |
| 40 | Blockade/Tension | 11 plies | 3,654,338 | 3,654,338 | 5,012 ms | 100 ms | **50.12x** | Yes |
|---|:---:|:---:|---:|---:|---:|---:|---:|:---:|
| **Total** | **All 40** | **7–14 plies** | **95,811,314** | **95,811,314** | **148,822 ms** | **2,951 ms** | **50.43x** | **100%** |

### Table 1B: International Draughts — Deep 1M TT (Array vs. Bitboard, 7–14 Plies)

| # | Category | Depth | Array TT Nodes (1M) | Bitboard TT Nodes (1M) | Array TT Time | Bitboard TT Time | Speedup | TT Cutoffs | Identical |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|:---:|
| 1 | Opening | 11 plies | 292,659 | 292,659 | 533 ms | 38 ms | **14.03x** | 5,515 | Yes |
| 2 | Opening | 12 plies | 477,847 | 477,847 | 844 ms | 30 ms | **28.13x** | 8,986 | Yes |
| 3 | Opening | 11 plies | 62,373 | 62,373 | 110 ms | 4 ms | **27.50x** | 1,627 | Yes |
| 4 | Opening | 10 plies | 483,930 | 483,930 | 875 ms | 31 ms | **28.23x** | 12,810 | Yes |
| 5 | Opening | 10 plies | 149,910 | 149,910 | 273 ms | 10 ms | **27.30x** | 4,616 | Yes |
| 6 | Opening | 11 plies | 322,604 | 322,604 | 581 ms | 23 ms | **25.26x** | 6,667 | Yes |
| 7 | Opening | 10 plies | 269,454 | 269,454 | 499 ms | 19 ms | **26.26x** | 4,910 | Yes |
| 8 | Opening | 10 plies | 272,268 | 272,268 | 489 ms | 18 ms | **27.17x** | 5,705 | Yes |
| 9 | Opening | 10 plies | 240,271 | 240,271 | 444 ms | 16 ms | **27.75x** | 8,900 | Yes |
| 10 | Opening | 10 plies | 138,374 | 138,374 | 234 ms | 9 ms | **26.00x** | 3,073 | Yes |
| 11 | Middlegame | 12 plies | 186,845 | 186,845 | 287 ms | 10 ms | **28.70x** | 14,172 | Yes |
| 12 | Middlegame | 10 plies | 116,233 | 116,233 | 185 ms | 7 ms | **26.43x** | 3,615 | Yes |
| 13 | Middlegame | 10 plies | 337,093 | 337,093 | 586 ms | 21 ms | **27.90x** | 11,016 | Yes |
| 14 | Middlegame | 10 plies | 278,835 | 278,835 | 432 ms | 17 ms | **25.41x** | 11,006 | Yes |
| 15 | Middlegame | 10 plies | 358,175 | 358,175 | 596 ms | 22 ms | **27.09x** | 13,415 | Yes |
| 16 | Middlegame | 14 plies | 382,298 | 382,298 | 621 ms | 26 ms | **23.88x** | 14,591 | Yes |
| 17 | Middlegame | 12 plies | 307,907 | 307,907 | 518 ms | 22 ms | **23.55x** | 10,283 | Yes |
| 18 | Middlegame | 11 plies | 447,196 | 447,196 | 643 ms | 26 ms | **24.73x** | 19,383 | Yes |
| 19 | Middlegame | 12 plies | 533,483 | 533,483 | 785 ms | 32 ms | **24.53x** | 17,763 | Yes |
| 20 | Middlegame | 11 plies | 489,824 | 489,824 | 729 ms | 29 ms | **25.14x** | 18,960 | Yes |
| 21 | Middlegame | 13 plies | 355,299 | 355,299 | 528 ms | 24 ms | **22.00x** | 15,743 | Yes |
| 22 | Middlegame | 14 plies | 601,181 | 601,181 | 879 ms | 35 ms | **25.11x** | 52,526 | Yes |
| 23 | Middlegame | 10 plies | 311,310 | 311,310 | 507 ms | 19 ms | **26.68x** | 10,217 | Yes |
| 24 | Middlegame | 11 plies | 208,480 | 208,480 | 332 ms | 13 ms | **25.54x** | 7,567 | Yes |
| 25 | Middlegame | 11 plies | 216,934 | 216,934 | 399 ms | 19 ms | **21.00x** | 5,486 | Yes |
| 26 | Endgame | 8 plies | 23,148 | 23,148 | 27 ms | 1 ms | **27.00x** | 1,083 | Yes |
| 27 | Endgame | 10 plies | 163,987 | 163,987 | 281 ms | 10 ms | **28.10x** | 10,476 | Yes |
| 28 | Endgame | 7 plies | 6,222 | 6,222 | 10 ms | 1 ms | **10.00x** | 326 | Yes |
| 29 | Endgame | 10 plies | 226,528 | 226,528 | 338 ms | 12 ms | **28.17x** | 14,609 | Yes |
| 30 | Endgame | 11 plies | 202,311 | 202,311 | 288 ms | 13 ms | **22.15x** | 17,962 | Yes |
| 31 | Endgame | 11 plies | 154,247 | 154,247 | 184 ms | 8 ms | **23.00x** | 21,131 | Yes |
| 32 | Endgame | 9 plies | 177,484 | 177,484 | 263 ms | 9 ms | **29.22x** | 19,183 | Yes |
| 33 | Endgame | 11 plies | 415,579 | 415,579 | 718 ms | 27 ms | **26.59x** | 24,137 | Yes |
| 34 | Endgame | 10 plies | 243,580 | 243,580 | 420 ms | 15 ms | **28.00x** | 16,764 | Yes |
| 35 | Endgame | 12 plies | 288,749 | 288,749 | 432 ms | 16 ms | **27.00x** | 16,590 | Yes |
| 36 | Blockade/Tension | 12 plies | 149,121 | 149,121 | 242 ms | 10 ms | **24.20x** | 5,415 | Yes |
| 37 | Blockade/Tension | 12 plies | 296,991 | 296,991 | 545 ms | 21 ms | **25.95x** | 27,969 | Yes |
| 38 | Blockade/Tension | 12 plies | 184,351 | 184,351 | 295 ms | 12 ms | **24.58x** | 11,248 | Yes |
| 39 | Blockade/Tension | 10 plies | 348,199 | 348,199 | 602 ms | 24 ms | **25.08x** | 24,611 | Yes |
| 40 | Blockade/Tension | 11 plies | 241,440 | 241,440 | 387 ms | 15 ms | **25.80x** | 9,651 | Yes |
|---|:---:|:---:|---:|---:|---:|---:|---:|---:|:---:|
| **Total** | **All 40** | **7–14 plies** | **10,962,720** | **10,962,720** | **17,941 ms** | **714 ms** | **25.13x** | **509,707** | **100%** |

---

## Milestone 2: Transposition Table Caching & Capacity Scaling ($1\text{M}$ vs. $16\text{M}$)

Detailed architectural documentation: [Chapter 08 – Transposition table](08-transposition-table.md).

Before upgrading to 4-way buckets in Phase 2, we benchmarked the impact of Zobrist transposition caching across both Standard ($7\text{–}9$ plies) and Deep ($7\text{–}14$ plies) suites and compared the default $1,048,576$-entry table ($16\text{ MiB}$) against the maximum $16,777,216$-entry table ($256\text{ MiB}$):

| Metric | **Standard Benchmark (7–9 plies)** | **Deep Benchmark (7–14 plies)** | **Scaling Comparison** |
| :--- | ---: | ---: | :--- |
| **Total Baseline Nodes (No TT)** | $2,408,731$ | **$95,898,151$** | **$39.8\times$ more baseline nodes** |
| **Total Default TT Nodes ($1\text{M}$)** | $711,546$ | **$11,117,122$** | $15.6\times$ more TT nodes |
| **Total Max TT Nodes ($16\text{M}$)** | $711,485$ | **$11,076,738$** | **Saves $40,384$ additional nodes** ($662\times$ larger node savings than at 7–9 plies) |
| **Overall Node Reduction %** | **70.5%** | **88.4%** ($1\text{M}$) / **88.45%** ($16\text{M}$) | **+17.9 percentage points higher pruning efficiency** |
| **Peak Single-Position Reduction** | **92.3%** (Pos 7) | **97.1%** (Pos 3: `2,152,631` $\rightarrow$ `62,373` nodes) | **32.53x single-position speedup** (`3,416 ms` $\rightarrow$ `105 ms`) |
| **Overall Speedup Factor** | **3.22x** | **8.16x** ($1\text{M}$) / **8.06x** ($16\text{M}$) | **Speedup more than doubles ($3.22\text{x} \rightarrow 8.16\text{x}$)** |
| **Total TT Cutoffs** | $20,686$ | **$532,359$** | **$25.7\times$ more direct hash cutoffs** |
| **1-Slot Hash Collisions ($1\text{M}$ vs. $16\text{M}$)** | $608 \rightarrow 36$ | **$142,781 \rightarrow 9,165$** | **93.6% fewer collisions** at $16\text{M}$ (and later **100% eliminated** in Phase 2!) |

---

## Milestone 3 (Phase 1): Engine Speed Improvements (Move Generation & Data Structures)

Detailed architectural documentation: [Chapter 11 – Bitboards](11-bitboards.md#bitboard-move-generation--phase-1-speed-optimizations-bitboardmovegeneratorcs).

In **Phase 1**, we implemented five targeted micro-optimizations that preserve 100% node-for-node equivalence while accelerating hot-path move generation and quiescence search:
1. **Set-Wise $O(1)$ `BitboardMoveGenerator.HasAnyCapture` Fast-Path:** Uses 4 bitwise shift-and-mask expressions across all pieces simultaneously so `MinimaxPlayer.Quiescence` immediately returns `standPat` on quiet leaf nodes without slicing move buffers or invoking `GenerateCaptures`.
2. **Flying King Ray Fast-Rejection (`(ray & unjumpedEnemy) == 0UL` + `StepTarget[4, 64]`):** Skips `LeadingZeroCount`/`TrailingZeroCount` and landing-mask computation whenever a diagonal ray has no unjumped enemy piece or the square immediately behind the first blocker is blocked.
3. **Redundant `HasAnyLegalMove` Elimination:** Avoids calling `HasAnyLegalMove` on interior nodes (`depth > 0` with `HalfMoveClock < 80`, where `Generate` already detects `moveCount == 0`) and on the initial `depth == 0` entry into `Quiescence`.
4. **Packed `ushort` Move Identifier (`BitMove.PackedMove`):** Packs `(ushort)((From << 8) | To)` so TT move matching in `OrderBitMoves` is a single 16-bit equality comparison.
5. **Amortized Timer Check Mask (`4095`):** Checks `Stopwatch.ElapsedMilliseconds` every $4{,}096$ nodes instead of $1{,}024$, cutting OS timer queries by $4\times$.

### Part A: 40-Position Deep Fixed-Depth Comparison (Phase 0 vs. Phase 1)

| Variant | Mode | Phase 0 Nodes | Phase 1 Nodes | Node Equivalence | Phase 0 Time | Phase 1 Time | Phase 0 NPS | Phase 1 NPS | Throughput Gain |
|---|---|---:|---:|:---:|---:|---:|---:|---:|---:|
| **International** | **No-TT** | `95,811,314` | `95,811,314` | **100% (40/40)** | `2,951 ms` | **`2,918 ms`** | `32.47M/s` | **`32.83M/s`** | **+1.1%** |
| **International** | **1M TT** | `10,962,720` | `10,962,720` | **100% (40/40)** | `714 ms` | **`672 ms`** | `15.35M/s` | **`16.30M/s`** | **+6.2%** |
| **International** | **16M TT** | `10,921,266` | `10,921,266` | **100% (40/40)** | `850 ms` | **`824 ms`** | `12.85M/s` | **`13.24M/s`** | **+3.0%** |
| **English** | **No-TT** | `94,110,293` | `94,110,293` | **100% (40/40)** | `2,641 ms` | **`2,567 ms`** | `35.63M/s` | **`36.66M/s`** | **+2.9%** |
| **English** | **1M TT** | `9,974,305` | `9,974,305` | **100% (40/40)** | `638 ms` | **`575 ms`** | `15.63M/s` | **`17.34M/s`** | **+10.9%** |
| **English** | **16M TT** | `9,944,268` | `9,944,268` | **100% (40/40)** | `720 ms` | **`708 ms`** | `13.81M/s` | **`14.03M/s`** | **+1.6%** |

### Part B: 5-Position Timed Benchmark (5.0s Budget / Position, 16M TT, International)

| Position | Category | Phase 0 Depth | Phase 1 Depth | Best Move | Score | Phase 1 Nodes | Phase 1 Time | Phase 0 NPS | Phase 1 NPS |
|---|:---:|:---:|:---:|:---:|:---:|---:|---:|---:|---:|
| **Pos #1** | Opening | `17 plies` | **`17 plies`** | `9-14` | `+5` | `60,440,576` | `5,000 ms` | `11.02M/s` | **`12.09M/s`** |
| **Pos #13** | Middlegame | `16 plies` | **`17 plies (+1)`** | `28-24` | `-3` | `50,385,840` | `4,508 ms` | `9.84M/s` | **`11.17M/s`** |
| **Pos #20** | Middlegame | `18 plies` | **`18 plies`** | `5-9` | `+12` | `55,496,704` | `5,000 ms` | `10.07M/s` | **`11.10M/s`** |
| **Pos #32** | Endgame | `20 plies` | **`20 plies`** | `18-14` | `+439` | `51,732,793` | `4,311 ms` | `10.53M/s` | **`12.00M/s`** |
| **Pos #40** | Blockade/Tension | `18 plies` | **`18 plies`** | `29x8` | `+765` | `37,892,371` | `3,352 ms` | `10.85M/s` | **`11.30M/s`** |

---

## Milestone 4 (Phase 2): Transposition Table Management (4-Way Cache-Line Buckets, `StaticEval` & `Sse.Prefetch0`)

Detailed architectural documentation: [Chapter 08 – Transposition table](08-transposition-table.md).

In **Phase 2**, `TranspositionTable` was upgraded from a 1-slot direct-mapped array to a **4-way set-associative table with 64-byte CPU cache-line buckets**:
1. **4-Way Set-Associative 64-Byte Cache-Line Buckets (`BucketSize = 4`):** Groups every 4 consecutive 16-byte entries into a single 64-byte CPU cache line (`baseIndex = (int)(key & _bucketMask) << 2`) with Age + Depth + Exact-Bound victim selection, eliminating **99.91%–100% of hash collisions** across all 40 deep positions (`141,091` $\rightarrow$ `120` in $1\text{M}$ TT; `9,175` $\rightarrow$ `0` in $16\text{M}$ TT).
2. **Compact 16-Byte `TranspositionEntry` Layout:** Stores `uint Key32` (upper 32 bits of Zobrist hash), `short Score`, `short StaticEval` (cached heuristic evaluation), `ushort BestMove` (`(fromSq << 8) | toSq`), `sbyte Depth`, `byte Age`, and `byte Flags` without increasing the 16-byte entry size.
3. **Pinned Object Heap Allocation & `Sse.Prefetch0` Hardware Prefetching:** Allocates `_entries` on the .NET Pinned Object Heap (`GC.AllocateArray<TranspositionEntry>(count, pinned: true)`) and issues `Sse.Prefetch0` immediately after `pos.Apply(in move)` to hide L3/DRAM latency.
4. **Multi-Turn TT Persistence:** Retains cached subtrees across moves during gameplay via `_tt.NewSearch()` age increments.

### Part A: 40-Position Deep Fixed-Depth Comparison (Phase 0 vs. Phase 1 vs. Phase 2)

| Variant | Mode | Phase 1 Collisions | Phase 2 Collisions | Phase 2 Nodes | Phase 0 Time | Phase 1 Time | Phase 2 Time | Phase 2 NPS | Total Gain vs. Phase 0 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **International** | **No-TT** | — | — | `95,811,314` | `2,951 ms` | `2,918 ms` | **`2,961 ms`** | `32.35M/s` | — |
| **International** | **1M TT** | `141,091` | **`120 (-99.91%)`** | `10,956,192` | `714 ms` | `672 ms` | **`638 ms`** | **`17.18M/s`** | **+11.9%** |
| **International** | **16M TT** | `9,175` | **`0 (-100.00%)`** | `10,954,812` | `850 ms` | `824 ms` | **`764 ms`** | **`14.34M/s`** | **+11.6%** |
| **English** | **No-TT** | — | — | `94,110,293` | `2,641 ms` | `2,567 ms` | **`2,531 ms`** | **`37.18M/s`** | **+4.3%** |
| **English** | **1M TT** | `121,868` | **`78 (-99.94%)`** | **`9,944,217`** | `638 ms` | `575 ms` | **`557 ms`** | **`17.85M/s`** | **+14.2%** |
| **English** | **16M TT** | `7,915` | **`0 (-100.00%)`** | **`9,944,060`** | `720 ms` | `708 ms` | **`656 ms`** | **`15.14M/s`** | **+9.6%** |

### Part B: 5-Position Timed Benchmark (5.0s Budget / Position, 16M TT, International)

| Position | Category | Phase 0 Depth | Phase 1 Depth | Phase 2 Depth | Best Move | Score | Phase 2 Nodes | Phase 2 Time | Phase 2 NPS |
|---|:---:|:---:|:---:|:---:|:---:|:---:|---:|---:|---:|
| **Pos #1** | Opening | `17 plies` | `17 plies` | **`17 plies`** | `9-14` | `+5` | `56,889,344` | `5,000 ms` | **`11.38M/s`** |
| **Pos #13** | Middlegame | `16 plies` | `17 plies` | **`17 plies`** | `28-24` | `-3` | `48,537,532` | `4,970 ms` | **`9.76M/s`** |
| **Pos #20** | Middlegame | `18 plies` | `18 plies` | **`18 plies`** | `5-9` | `+12` | `49,897,472` | `5,000 ms` | **`9.98M/s`** |
| **Pos #32** | Endgame | `20 plies` | `20 plies` | **`20 plies`** | `18-14` | `+439` | `54,060,293` | **`4,145 ms`** | **`13.04M/s`** |
| **Pos #40** | Blockade/Tension | `18 plies` | `18 plies` | **`18 plies`** | `29x8` | `+760` | `60,432,384` | `5,000 ms` | **`12.09M/s`** |

---

## Milestone 5 (Phase 3 — Search Stage A): Exact Node Reduction (PVS, Killer Moves, History Heuristic & `DrawTable`)

Detailed architectural documentation: [Chapter 06 – Move ordering](06-move-ordering.md) and [Chapter 07 – Search (Stage A)](07-search.md#stage-a-exact-search-enhancements-phase-3).

In **Phase 3**, we implemented all exact search enhancements that prune suboptimal branches without sacrificing exact minimax evaluation:
1. **Killer Move Heuristic (2 FIFO Slots per Ply) & History Heuristic (`[2][64][64]`):** Maintains `ushort _killers[MaxPly * 2]` and `int _history[2 * 64 * 64]` (`+ depth * depth` bonus on quiet $\beta$-cutoffs, clamped to `70,000`). Pre-scores moves once into `Span<int> scores = stackalloc int[count]` (`TT = 10M`, `Captures = 1M+`, `Promotions = 500k`, `Killer 1 = 90k`, `Killer 2 = 80k`, `History = 0..70k`) before running in-place insertion sort.
   - *Benchmark Experiment Note:* We tested replacing `Span<int> scores = stackalloc int[count]` with a preallocated per-ply `int[][] _scoreBuffers` heap array; `stackalloc int[count]` was ~5% faster because RyuJIT eliminates bounds checks when `scores.Length == moves.Length` and keeps the buffer in the L1 stack frame.
2. **Principal Variation Search (PVS / Null-Window Search):** Searches move $i = 0$ at `ply > 0` with the full window $[-\beta, -\alpha]$, and probes all subsequent moves ($i > 0$) with a zero-width window $[-\alpha - 1, -\alpha]$, re-searching with $[-\beta, -\alpha]$ only when $\alpha < \text{score} < \beta$.
3. **In-Search Repetition Detection (`DrawTable`):** Tracks 64-bit Zobrist hashes along the active search path and from pre-root game history (`_twoFoldHashes` and `_oneFoldHashes`), returning `0` (Draw) at `ply > 0` before probing the Transposition Table whenever a repetition occurs.

### Part A: 40-Position Deep Fixed-Depth Comparison (Phase 0 vs. Phase 2 vs. Phase 3)

| Variant | Mode | Phase 0 Nodes | Phase 2 Nodes | **Phase 3 Nodes** | **Node Reduction** | Phase 0 Time | Phase 2 Time | **Phase 3 Time** | **Time Speedup vs. P0** |
|---|---|---:|---:|---:|:---:|---:|---:|---:|:---:|
| **International** | **No-TT** | `95,811,314` | `95,811,314` | **`33,933,396`** | **-64.58% (2.82× fewer)** | `2,951 ms` | `2,870 ms` | **`1,367 ms`** | **2.16× faster (-53.7%)** |
| **International** | **1M TT** | `10,962,720` | `10,956,192` | **`7,840,974`** | **-28.48% (1.40× fewer)** | `714 ms` | `638 ms` | **`548 ms`** | **1.30× faster (-23.2%)** |
| **International** | **16M TT** | `10,921,266` | `10,954,812` | **`7,840,967`** | **-28.42% (1.40× fewer)** | `850 ms` | `764 ms` | **`617 ms`** | **1.38× faster (-27.4%)** |
| **English** | **No-TT** | `94,110,293` | `94,110,293` | **`33,065,884`** | **-64.86% (2.85× fewer)** | `2,641 ms` | `2,566 ms` | **`1,124 ms`** | **2.35× faster (-57.4%)** |
| **English** | **1M TT** | `9,974,305` | `9,944,217` | **`7,324,657`** | **-26.56% (1.36× fewer)** | `638 ms` | `557 ms` | **`447 ms`** | **1.43× faster (-29.9%)** |
| **English** | **16M TT** | `9,944,268` | `9,944,060` | **`7,324,654`** | **-26.34% (1.36× fewer)** | `720 ms` | `656 ms` | **`516 ms`** | **1.40× faster (-28.3%)** |

### Part B: 5-Position Timed Benchmark (5.0s Budget / Position, 16M TT, International)

| Position | Category | Phase 0 Depth | Phase 2 Depth | **Phase 3 Depth** | Best Move | Score | Phase 3 Nodes | Phase 3 Time | Phase 3 NPS |
|---|:---:|:---:|:---:|:---:|:---:|:---:|---:|---:|---:|
| **Pos #1** | Opening | `17 plies` | `17 plies` | **`18 plies (+1)`** | `12-16` | `+3` | `49,086,464` | `5,000 ms` | `9.82M/s` |
| **Pos #13** | Middlegame | `16 plies` | `17 plies` | **`17 plies (+1 vs P0)`** | `28-24` | `-3` | `46,913,456` | **`4,721 ms`** | `9.94M/s` |
| **Pos #20** | Middlegame | `18 plies` | `18 plies` | **`20 plies (+2)`** | `5-9` | `+15` | `45,441,944` | `4,954 ms` | `9.17M/s` |
| **Pos #32** | Endgame | `20 plies` | `20 plies` | **`20 plies`** | `18-14` | `+439` | `50,345,995` | `4,502 ms` | `11.18M/s` |
| **Pos #40** | Blockade/Tension | `18 plies` | `18 plies` | **`19 plies (+1)`** | `29x8` | `+762` | `47,138,615` | **`4,032 ms`** | `11.69M/s` |

---

## Milestone 6 (Phase 4 — Search Stage B): Selective Pruning & Reductions (Verified LMR, RFP & FP)

Detailed architectural documentation: [Chapter 07 – Search (Stage B)](07-search.md#stage-b-selective-pruning--reductions-phase-4).

In **Phase 4**, we added three selective pruning and depth-reduction algorithms guarded by $O(1)$ bitboard capture checks (`!BitboardMoveGenerator.HasAnyCapture`):
1. **Verified Late Move Reductions (LMR):** For quiet moves (`!isCapture && !isPromotion`) at `depth >= 3`, `ply >= 2`, and move index $i \ge 3$ where `nextPos` does not give the opponent an immediate capture (`!BitboardMoveGenerator.HasAnyCapture(in nextPos, _variant)`), reduces the initial null-window search by $R = \min(d - 2,\, 1 + [i \ge 8])$. Any reduced search that beats $\alpha$ is immediately verified at full depth $d - 1$ on the null window before being allowed to raise $\alpha$.
2. **Reverse Futility Pruning (RFP):** At shallow non-PV quiet nodes (`beta <= alpha + 1`, `depth <= 6`, `!isCaptureNode`) where neither player has a capture available and `beta` is non-mate, immediately returns `cachedStaticEval` when $\text{eval}_0 - 40 \cdot d \ge \beta$.
3. **Futility Pruning (FP):** At shallow non-PV quiet nodes (`beta <= alpha + 1`, `depth <= 3`, `!isCaptureNode`) where $\text{eval}_0 + 60 \cdot d \le \alpha$, skips subsequent ($i > 0$) quiet non-promoting moves that do not create a tactical capture threat.

### Part A: 40-Position Deep Fixed-Depth Comparison (Phase 0 vs. Phase 3 Exact vs. Phase 4 Selective)

| Variant | Mode | Phase 0 Nodes | Phase 3 (Exact) Nodes | **Phase 4 (Selective) Nodes** | **Node Reduction vs. P0 (vs. P3)** | Phase 0 Time | Phase 3 Time | **Phase 4 Time** | **Speedup vs. P0** |
|---|---|---:|---:|---:|:---:|---:|---:|---:|:---:|
| **International** | **No-TT** | `95,811,314` | `33,933,396` | **`6,727,472`** | **-92.98% / 14.24× (-80.17% vs P3)** | `2,951 ms` | `1,367 ms` | **`458 ms`** | **6.44× faster** |
| **International** | **1M TT** | `10,962,720` | `7,840,974` | **`2,349,717`** | **-78.57% / 4.67× (-70.03% vs P3)** | `714 ms` | `548 ms` | **`246 ms`** | **2.90× faster** |
| **International** | **16M TT** | `10,921,266` | `7,840,967` | **`2,349,717`** | **-78.48% / 4.65× (-70.03% vs P3)** | `850 ms` | `617 ms` | **`263 ms`** | **3.23× faster** |
| **English** | **No-TT** | `94,110,293` | `33,065,884` | **`5,922,594`** | **-93.71% / 15.89× (-82.09% vs P3)** | `2,641 ms` | `1,124 ms` | **`302 ms`** | **8.75× faster** |
| **English** | **1M TT** | `9,974,305` | `7,324,657` | **`2,083,260`** | **-79.11% / 4.79× (-71.56% vs P3)** | `638 ms` | `447 ms` | **`173 ms`** | **3.69× faster** |
| **English** | **16M TT** | `9,944,268` | `7,324,654` | **`2,083,260`** | **-79.05% / 4.77× (-71.56% vs P3)** | `720 ms` | `516 ms` | **`187 ms`** | **3.85× faster** |

### Part B: 5-Position Timed Benchmark (5.0s Budget / Position, 16M TT, International)

| Position | Category | Phase 0 Depth | Phase 3 (Exact) Depth | **Phase 4 (Selective) Depth** | Best Move | Score | Phase 4 Nodes | Phase 4 Time | Phase 4 NPS |
|---|:---:|:---:|:---:|:---:|:---:|:---:|---:|---:|---:|
| **Pos #1** | Opening | `17 plies` | `18 plies` | **`21 plies (+4 vs P0, +3 vs P3)`** | `9-14` | `+5` | `40,181,760` | `5,000 ms` | `8.04M/s` |
| **Pos #13** | Middlegame | `16 plies` | `17 plies` | **`21 plies (+5 vs P0, +4 vs P3)`** | `28-24` | `+7` | `32,912,269` | **`4,122 ms`** | `7.98M/s` |
| **Pos #20** | Middlegame | `18 plies` | `20 plies` | **`22 plies (+4 vs P0, +2 vs P3)`** | `12-16` | `0` | `35,436,215` | **`4,320 ms`** | `8.20M/s` |
| **Pos #32** | Endgame | `20 plies` | `20 plies` | **`25 plies (+5 vs P0 & P3)`** | `18-14` | `+439` | `34,374,464` | **`4,207 ms`** | `8.17M/s` |
| **Pos #40** | Blockade/Tension | `18 plies` | `19 plies` | **`22 plies (+4 vs P0, Solved Mate!)`** | `29x8` | **`+Win in 23 plies`** | `20,214,797` | **`2,151 ms`** | `9.40M/s` |

---

## Milestone 7 (Phase 5): Root Search & Time Management (Partial-Iteration Adoption & Dominant-Move Exit)

Detailed architectural documentation: [Chapter 07 – Search (Iterative Deepening)](07-search.md#iterative-deepening-root-search--asynchronous-execution) and [Chapter 09 – Time control](09-time-control.md#root-search--time-management-optimizations-phase-5).

In **Phase 5**, [`MinimaxPlayer.GetMoveAsync`](../../src/Checkers.Core/AI/MinimaxPlayer.cs) was enhanced with two root-level search and clock management improvements:
1. **Partial-Iteration Root Move Adoption on Timeout (`LastSearchAdoptedPartialIteration`):** Because `currentOrder[0]` is always the best move from completed depth $d - 1$, once $i = 0$ finishes cleanly at depth $d$, any subsequent root move $i > 0$ that completes without aborting and strictly beats `bestScoreThisDepth` is proven superior at full depth $d$. `GetMoveAsync` publishes it to `bestMoveOverall` and `bestScoreOverall` immediately so that if a later root move hits `hardLimitMs`, the engine preserves the deeper depth-$d$ result instead of discarding the entire partial iteration.
2. **Early Root Termination on Single Viable Move (`UseEarlyRootTermination` / `LastSearchTerminatedEarly`):** Tracks `bestScoreThisDepth` and `secondBestScoreThisDepth` across root moves. In timed modes (`TimePerMove` and `TimePerGame`), iterative deepening terminates early when all moves are proven forced losses ($\text{bestScoreOverall} \le -28{,}000$) or when at depth $d \ge 8$ the same root move leads all alternatives by $\ge 150\text{ cp}$ ($1.5\text{ men}$) for $2$ consecutive completed iterations.

### Part A: 40-Position Deep Fixed-Depth Suite (Phase 0 vs. Phase 5)

Because Phase 5 preserves exact root windows in `FixedDepth` mode, all 40 fixed-depth positions evaluate the **exact same node counts** as Phase 4 while achieving peak throughput:

| Variant | Mode | Phase 0 Nodes | **Phase 5 Nodes** | **Node Reduction vs. P0** | Phase 0 Time | **Phase 5 Time** | **Phase 5 NPS** | **Speedup vs. P0 (vs. Array 0A)** |
|---|---|---:|---:|:---:|---:|---:|---:|:---:|
| **International** | **No-TT** | `95,811,314` | **`6,727,472`** | **-92.98% (14.24× fewer)** | `2,951 ms` | **`433 ms`** | **`15.51M/s`** | **6.82× faster (343.7× vs Array)** |
| **International** | **1M TT** | `10,962,720` | **`2,349,717`** | **-78.57% (4.67× fewer)** | `714 ms` | **`236 ms`** | **`9.94M/s`** | **3.03× faster (630.6× vs Array)** |
| **International** | **16M TT** | `10,921,266` | **`2,349,717`** | **-78.48% (4.65× fewer)** | `850 ms` | **`251 ms`** | **`9.34M/s`** | **3.39× faster (592.9× vs Array)** |
| **English** | **No-TT** | `94,110,293` | **`5,922,594`** | **-93.71% (15.89× fewer)** | `2,641 ms` | **`300 ms`** | **`19.70M/s`** | **8.80× faster (473.5× vs Array)** |
| **English** | **1M TT** | `9,974,305` | **`2,083,260`** | **-79.11% (4.79× fewer)** | `638 ms` | **`174 ms`** | **`11.96M/s`** | **3.67× faster (816.3× vs Array)** |
| **English** | **16M TT** | `9,944,268` | **`2,083,260`** | **-79.05% (4.77× fewer)** | `720 ms` | **`186 ms`** | **`11.16M/s`** | **3.87× faster (763.6× vs Array)** |

### Part B: Master Cumulative Timed Benchmark Progression (Phase 0 → Phase 5, 5.0s Budget, 16M TT, International)

| Position | Category | Phase 0 (Baseline) | Phase 1 (Speed) | Phase 2 (4-Way TT) | Phase 3 (Exact PVS) | Phase 4 (Selective) | **Phase 5 (Root & Time)** | Best Move | **Phase 5 Score** | **Phase 5 Nodes** | **Phase 5 Time** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|---:|---:|
| **Pos #1** | Opening | `17 plies` | `17 plies` | `17 plies` | `18 plies` | `21 plies` | **`21 plies`** *(+d22 root PV adopted)* | `9-14` | **`+2`** *(d22 exact)* | `40,767,488` | `5,000 ms` |
| **Pos #13** | Middlegame | `16 plies` | `17 plies` | `17 plies` | `17 plies` | `21 plies` | **`21 plies (+5 vs P0)`** | `28-24` | **`+7`** | `32,912,269` | **`4,103 ms`** |
| **Pos #20** | Middlegame | `18 plies` | `18 plies` | `18 plies` | `20 plies` | `22 plies` | **`22 plies (+4 vs P0)`** | `12-16` | **`0`** | `35,436,215` | **`4,247 ms`** |
| **Pos #32** | Endgame | `20 plies` | `20 plies` | `20 plies` | `20 plies` | `25 plies` | **`25 plies (+5 vs P0)`** | `18-14` | **`+439`** | `34,374,464` | **`4,158 ms`** |
| **Pos #40** | Blockade/Tension | `18 plies` | `18 plies` | `18 plies` | `19 plies` | `22 plies` | **`22 plies (+4 vs P0, Solved!)`** | `29x8` | **`+Win in 23 plies`** | `20,214,797` | **`2,108 ms`** |

---

## Milestone 8 (Phase 6): Static Evaluation Overhaul & 200-Game Bot-vs-Bot Verification

Detailed architectural documentation: [Chapter 05 – Evaluation](05-evaluation.md).

In **Phase 6**, we preserved the original static evaluator as [`LegacyEvaluationFunction.cs`](../../src/Checkers.Core/AI/LegacyEvaluationFunction.cs), added zero-allocation `int Evaluate(in BitPosition pos)` dispatch to [`IEvaluationFunction.cs`](../../src/Checkers.Core/AI/IEvaluationFunction.cs), and upgraded [`EvaluationFunction.cs`](../../src/Checkers.Core/AI/EvaluationFunction.cs) with:
1. **English Checkers (`EvaluateEnglish`, 1:1 `board_eval.c` Port at $2\times$ Centipawn Scale):**
   - `Man = 100 cp`, `English King = 140 cp` (`1.40×` Man).
   - Hardware `POPCNT`-vectorized Man and King Piece-Square Tables (`WhiteManPst1/2/3Mask`, `BlackManPst1/2/3Mask`, `EnglishKingPst4/5Mask`).
   - Late-game Man advancement (`totalPieces <= 12`: `+2 cp × rank`).
   - Precomputed 64-entry forward promotion cones (`ConeWhite[64]`, `ConeBlack[64]`) awarding `+(40 + 6 × advance) cp` for unstoppable Runaway Checkers when `enemyKings == 0`.
   - Parallel bitshift King Tail Pins (`+10 cp`), single-corner trapped King penalty (`-40 cp` on `(0,7)`/`(7,0)`), non-linear simplification bonus (`CalculatePieceBonus`: `+20` to `+400 cp`), and five classical Checkers structural patterns (`Right Lock +40`, `Bridge +30`, `Triangle +20`, `Oreo +20`, `Dog +10`).
2. **International Draughts (`EvaluateInternational`, Flying Kings & Backward-Capture Adaptation):**
   - `Man = 100 cp`, `Flying King = 300 cp` (`3.00×` Man), continuous Man advancement (`+5 cp/rank` $\to$ `+6 cp/rank` when `totalPieces <= 12`), full 4-square back-rank defense (`+15 cp`), center control (`+12 cp`), `WhiteManPst`/`BlackManPst`, `ConeWhite`/`ConeBlack` Runaway Checkers, `tail_pins` (`+10 cp`), Flying King Main Long Diagonal PST (`+12 cp`), Short-Corner penalty (`-20 cp`), back-rank formations (`Bridge +18`, `Triangle +12`, `Oreo +12`), and a **material-gated simplification bonus** (`CalculateInternationalTradeBonus`, active only when ahead by $\ge 80\text{ cp}$ in material).

### Part A: Raw Static Evaluation Microbenchmark (`10,000,000` Evaluations / Variant)

Evaluating all 40 benchmark positions in a tight 10-million-call loop (`250,000` passes × `40` positions) demonstrates that `BitOperations.PopCount` bitmask vectorization keeps raw evaluation latency under **9–13 nanoseconds per call**:

| Variant | Evaluator | Total Evaluations | Elapsed Time | Throughput (`M evals/s`) | Latency (`ns/eval`) | Delta vs. `Legacy` |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: |
| **English Checkers** | `LegacyEvaluationFunction` | `10,000,000` | `82 ms` | **`121.9M evals/s`** | `8.2 ns/eval` | Baseline |
| **English Checkers** | **Retained `EvaluationFunction` (`board_eval.c`)** | `10,000,000` | **`88 ms`** | **`113.0M evals/s`** | **`8.8 ns/eval`** | **`+0.6 ns` (`+7.3%`)** |
| **International Draughts** | `LegacyEvaluationFunction` | `10,000,000` | `76 ms` | **`130.3M evals/s`** | `7.7 ns/eval` | Baseline |
| **International Draughts** | **Retained `EvaluationFunction` (`New`)** | `10,000,000` | **`131 ms`** | **`76.1M evals/s`** | **`13.1 ns/eval`** | **`+5.4 ns`** |

### Part B: 40-Position Fixed-Depth Search Speed Comparison (`Legacy` vs. `New`)

When embedded inside `MinimaxPlayer` with the `1M` Transposition Table enabled, the richer positional ordering and sharper pruning bounds of the retained `EvaluationFunction` **reduce total searched nodes (`-0.8%` in English, `-15.2%` in International)**, resulting in **equal or faster wall-clock completion times** despite the richer leaf evaluation:

| Variant | Mode | `Legacy` Nodes | **`New` Nodes** | **Node Delta** | `Legacy` Time | **`New` Time** | **Time Delta** | `Legacy` NPS | **`New` NPS** |
| :--- | :--- | ---: | ---: | :---: | ---: | ---: | :---: | ---: | ---: |
| **English (`board_eval.c`)** | **No-TT** | `5,868,002` | `6,860,691` | `+16.9%` | `401 ms` | `471 ms` | `+70 ms` | `14.63M/s` | `14.54M/s` |
| **English (`board_eval.c`)** | **1M TT** | `2,086,849` | **`2,069,177`** | **`-0.85%`** | `222 ms` | **`224 ms`** | `~0 ms` | `9.40M/s` | `9.20M/s` |
| **International** | **No-TT** | `6,333,182` | **`6,000,494`** | **`-5.25%`** | `362 ms` | `391 ms` | `+29 ms` | `17.46M/s` | `15.33M/s` |
| **International** | **1M TT** | `2,295,742` | **`1,947,323`** | **`-15.18%`** | `207 ms` | **`193 ms`** | **`-6.76%` (Faster!)** | `11.06M/s` | `10.05M/s` |

### Part C: 5-Position Timed Search Comparison (`5.0s` Budget / Position, `16M TT`)

| Variant | Position | Category | `Legacy` Depth | `Legacy` Move & Score | `Legacy` NPS | **`New` Depth** | **`New` Move & Score** | **`New` NPS** |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **English** | **Pos #1** | Opening | `21 plies` | `9-14` (`+3`) | `8.07M/s` | **`21 plies`** | `11-15` (`+10`) | `7.91M/s` |
| **English** | **Pos #13** | Middlegame | `21 plies` | `28-24` (`0`) | `8.23M/s` | **`21 plies`** | `28-24` (`0`) | `7.26M/s` |
| **English** | **Pos #20** | Middlegame | `22 plies` | `5-9` (`+12`) | `7.77M/s` | **`22 plies`** | `12-16` (`-2`) | `7.96M/s` |
| **English** | **Pos #32** | Endgame | `26 plies` | `32-27` (`+Win in 21`) | `9.79M/s` | **`22 plies`** | `18-14` (`+Win in 21`) | **`10.86M/s`** |
| **English** | **Pos #40** | Blockade | `24 plies` | `3-8` (`+275`) | `7.63M/s` | **`25 plies (+1)`** | `7-10` (`+422`) | `6.79M/s` |
| **International** | **Pos #1** | Opening | `21 plies` | `9-14` (`+2`) | `7.95M/s` | **`21 plies`** | `12-16` (`+12`) | `7.57M/s` |
| **International** | **Pos #13** | Middlegame | `21 plies` | `28-24` (`+7`) | `7.86M/s` | **`20 plies`** | `28-24` (`+4`) | `7.59M/s` |
| **International** | **Pos #20** | Middlegame | `22 plies` | `12-16` (`0`) | `8.17M/s` | **`23 plies (+1)`** | `12-16` (`+22`) | `7.23M/s` |
| **International** | **Pos #32** | Endgame | `25 plies` | `18-14` (`+439`) | `8.04M/s` | **`24 plies`** | `18-14` (`+550`) | `7.51M/s` |
| **International** | **Pos #40** | Blockade | `22 plies` | `29x8` (`+Win in 23`) | `8.89M/s` | **`23 plies (+1)`** | `29x8` (**`+Win in 21`**) | `8.31M/s` |

### Part D: 200-Game Bot-vs-Bot Self-Play Match Results (`50` Balanced Ballots × `2` Sides / Variant, `1000 ms/move`)

Using [`EvaluationMatchRunner.cs`](../../src/Checkers.Core/AI/Benchmark/EvaluationMatchRunner.cs), we generated **50 deterministic, unique, balanced opening ballots** per variant (`10` each at `6, 7, 8, 9, and 10 plies` from the initial board, verified at depth 6 to have equal piece counts, `0` Kings, no pending captures, and $|\text{eval}| \le 30\text{ cp}$ under both evaluators). Each ballot was played twice with colors swapped (`100` games per variant at `1000 ms/move`), with complete per-engine isolation (dedicated `MinimaxPlayer` and `32 MiB` `TranspositionTable` per side).

#### 1. English Checkers — 100-Game Match (`NewEval` vs. `LegacyEval`, `1000 ms/move`)

| Metric | **Retained Phase 6 `NewEval` (`board_eval.c` Port)** | `LegacyEval` (`LegacyEvaluationFunction`) |
| :--- | :---: | :---: |
| **Total Score (100 Games)** | **69.5 / 100** (**69.5%**) | 30.5 / 100 (30.5%) |
| **Wins / Draws / Losses** | **+41 = 57 −2** | +2 = 57 −41 |
| **As White (50 Games)** | **+21 = 27 −2** | +0 = 30 −20 |
| **As Black (50 Games)** | **+20 = 30 −0** *(0 losses!)* | +2 = 27 −21 |
| **Paired 50-Ballot Breakdown (`2-0` / `1.5-0.5` / `1-1` / `0.5-1.5` / `0-2`)** | **`7` / `27` / `14` / `2` / `0`** | `0` / `2` / `14` / `27` / `7` |
| **Elo Difference ($\Delta\text{Elo} \pm 95\%\text{ CI}$)** | **+143.1 ± 42.9 Elo** (**LOS: 100.0%**) | −143.1 ± 42.9 Elo |
| **Raw Eval Latency (`10M` Evals)** | **`8.8 ns/eval` (`113.0M/s`)** | `8.2 ns/eval` (`121.9M/s`) |
| **Average Search Depth** | **`22.86 plies`** *(−0.14 vs. Legacy)* | `23.00 plies` |
| **Average Search Speed (NPS)** | **`10.98M nodes/s`** | `11.71M nodes/s` |
| **Average Game Length** | **`120.3 plies`** | `120.3 plies` |

#### 2. International (Flying Kings) Checkers — 100-Game Match & Domain Discovery (`1000 ms/move`)

> [!IMPORTANT]
> **Empirical Domain Discovery (Piece-Count vs. Material-Gated Simplification in Flying Kings):**
> In our first 100-game International match using `board_eval.c`'s raw piece-count condition (`whiteCount > blackCount`) and late-only advancement (`totalPieces <= 12`), the candidate evaluator scored `44.5 / 100` (`+13 = 63 -24`, **`-38.4 ± 41.7 Elo`**). Root-cause analysis revealed why: in English Checkers, a King is worth `1.4 Men` (`140 cp`), so `2 Men (200 cp) > 1 King (140 cp)` and raw piece count aligns with material. In International Draughts, a **Flying King** is worth **`3.0 Men` (`300 cp`)**: when a player had `1 Flying King` (`300 cp`, 1 piece) against `2 Men` (`200 cp`, 2 pieces), `blackCount (2) > whiteCount (1)` awarded **`+400 cp` to the 2-Men side**, causing the engine to misjudge Flying King endgames and avoid crowning sacrifices!
> Gating the International simplification bonus on **material advantage** ($\ge 80\text{ cp}$) and restoring continuous Man advancement produced a **`+83.8 Elo` swing** (from `-38.4 Elo` to **`+45.4 ± 37.8 Elo`**, `99.2% LOS`):

| Metric | Initial Candidate (Raw Piece-Count Bonus) | **Retained Phase 6 `NewEval` (Material-Gated $O(1)$ Masks)** | Phase 6b Experiment (+ Scan 3.1 Ray Mobility / Skew / Draw Scaling) | `LegacyEval` (`LegacyEvaluationFunction`) |
| :--- | :---: | :---: | :---: | :---: |
| **Total Score (100 Games)** | 44.5 / 100 (44.5%) | **56.5 / 100** (**56.5%**) | 56.0 / 100 (56.0%) | 43.5 / 100 (43.5%) |
| **Wins / Draws / Losses** | +13 = 63 −24 | **+22 = 69 −9** | +20 = 72 −8 | +9 = 69 −22 |
| **As White (50 Games)** | +10 = 29 −11 | **+10 = 37 −3** | +13 = 34 −3 | +6 = 32 −12 |
| **As Black (50 Games)** | +3 = 34 −13 | **+12 = 32 −6** | +7 = 38 −5 | +3 = 37 −10 |
| **Paired 50-Ballot Breakdown (`2-0` / `1.5-0.5` / `1-1` / `0.5-1.5` / `0-2`)** | `2` / `4` / `27` / `15` / `2` | **`2` / `14` / `29` / `5` / `0`** | `0` / `18` / `26` / `6` / `0` | `0` / `5` / `29` / `14` / `2` |
| **Elo Difference ($\Delta\text{Elo} \pm 95\%\text{ CI}$)** | −38.4 ± 41.7 Elo (LOS: 3.4%) | **+45.4 ± 37.8 Elo** (**LOS: 99.2%**) | +41.9 ± 35.9 Elo (LOS: 99.0%) | −45.4 ± 37.8 Elo |
| **Raw Eval Latency (`10M` Evals)** | `13.1 ns/eval` (`76.1M/s`) | **`13.1 ns/eval` (`76.1M/s`)** | `26.4–31.3 ns/eval` (`32.0–37.8M/s`) | `7.7 ns/eval` (`130.3M/s`) |
| **Average Search Depth** | `24.51 plies` | **`24.12 plies`** *(+0.18 vs Legacy)* | `22.93 plies` *(−1.19 vs Phase 6)* | `23.94 plies` |
| **Average Search Speed (NPS)** | `10.34M nodes/s` | **`10.34M nodes/s`** | `10.24M nodes/s` | `10.97M nodes/s` |

> [!NOTE]
> **Why Phase 6 Was Retained Over Phase 6b (Scan 3.1 Ray-Mobility Experiment):**
> Adding Scan 3.1's per-King ray reachability (`safe` vs. `deny` mobility against enemy Man jump threats), Left/Right Wing Skew, and endgame draw scaling (`2K vs. 1K` `/ 8`) more than doubled raw leaf evaluation latency (`13.1 ns/eval` $\to$ `26.4–31.3 ns/eval`) and compressed endgame pruning margins, costing **`1.19 plies` of average search depth** (`24.12` $\to$ `22.93 plies`) and **`3.5 Elo`** (`56.5 / 100` $\to$ `56.0 / 100`). Because Phase 6's $O(1)$ bitmasks (`FlyingKingMainDiagonalMask`, `FlyingKingInnerMask`, `FlyingKingShortCornerMask`) already capture the essential $8 \times 8$ Flying-King geometry at half the CPU cost, the simpler, faster, and stronger **Phase 6 `EvaluateInternational`** was retained in [`EvaluationFunction.cs`](../../src/Checkers.Core/AI/EvaluationFunction.cs).

---

## Milestone 9 (Phase 7): 12-Ply Transposition-Aware Opening Book (3 Stages of Innovation)

Detailed architectural documentation: [Chapter 10 – Opening book](10-opening-book.md).

In **Phase 7**, we designed and generated embedded, human-readable opening books ([`OpeningBook.English.txt`](../../src/Checkers.Core/AI/Book/OpeningBook.English.txt) and [`OpeningBook.International.txt`](../../src/Checkers.Core/AI/Book/OpeningBook.International.txt)) across three iterative stages:
1. **Stage 1 — Top-Down Multi-PV ($k = 3$) DAG (`lv 0..8`):** Used `MinimaxPlayer.SearchMultiPv` ($k=3$, $d=14$) to build a compact initial book of `999` English and `995` International positions, proving the Zobrist DAG format and bottom-up Negamax verification (`BookBuilder.CheckBackUp`).
2. **Stage 2 — Full-Width Early Plies (`lv 0..3`):** Expanded all legal moves (`100%` width) across the first 4 plies (`149` core nodes) so the computer never falls out of book on moves 1–2 regardless of which of the 7 legal opening moves (`22-18`, `24-19`, `22-17`, `23-18`, `23-19`, `21-17`, `24-20`) a human opponent chooses.
3. **Stage 3 — Depth-First Drop-Out Expansion (`lv 4..12`, Search Depth $d = 16$):** Replaced breadth-first expansion with Thomas Lincke's priority-driven **Drop-Out Expansion (DOE)** (`BookBuilder.ExpandDropOut`), continuously walking from the root to the highest-priority leaf within ` MaxDelta = 30 cp` (`W_player = 2`, `W_opponent = 1`), evaluating all legal moves at depth $d = 16$ (`32 MiB` TT), and immediately propagating backed-up Negamax scores (`PropagateBackUp`) to guide the next trajectory all the way to **100% completion at `lv 12`**.

| Metric | Stage 1 (Width-3 DAG to `lv 8`) | **Stage 2 + 3 Final Book (`OpeningBook.English.txt`)** | **Stage 2 + 3 Final Book (`OpeningBook.International.txt`)** |
| :--- | :---: | :---: | :---: |
| **Book Horizon (`MaxPly`)** | `8 plies` | **`12 plies` (100% complete)** | **`12 plies` (100% complete)** |
| **Node Evaluation Search Depth** | `14 plies` | **`16 plies` (`32 MiB` 4-Way TT)** | **`16 plies` (`32 MiB` 4-Way TT)** |
| **Total Unique DAG Positions** | `999` (Eng) / `995` (Intl) | **`32,369`** | **`26,367`** |
| **Evaluated Internal Nodes (`lv 0..11`)** | `472` (Eng) / `468` (Intl) | **`14,320`** | **`11,541`** |
| **Evaluated Frontier Leaves** | `527` (at `lv 8`) | **`18,049` (`8,605` at `lv 12`)** | **`14,826` (`4,904` at `lv 12`)** |
| **Backed-Up Root Score (`lv 0`)** | `+2 cp` | **`+2 cp` (`22-18 +2`)** | **`+2 cp` (`22-18 +2`, `21-17 +1`)** |
| **Negamax Back-Up Consistency** | `0 errors` | **`0 errors` (100% exact)** | **`0 errors` (100% exact)** |
| **Embedded File Size** | `~26 KB` | **`1,062,670 bytes` (`1.01 MB`)** | **`875,844 bytes` (`855.3 KB`)** |

---

## Reproducing the Benchmarks

All benchmarks and opening book tools can be executed directly from the command line using `tools/Checkers.Benchmark`:

```powershell
# Run the Phase 0..5 progression benchmark (40-position deep suite + 5-position 5.0s timed suite)
dotnet run --project tools/Checkers.Benchmark/Checkers.Benchmark.csproj -c Release -- phase-bench

# Run the Phase 6 Evaluation Speed Benchmark (10M raw evals + 40-pos fixed depth + 5-pos timed)
dotnet run --project tools/Checkers.Benchmark/Checkers.Benchmark.csproj -c Release -- eval-speed

# Run the Phase 6 200-Game Bot-vs-Bot Self-Play Match (50 ballots × 2 sides per variant, 1000 ms/move, 4 workers)
dotnet run --project tools/Checkers.Benchmark/Checkers.Benchmark.csproj -c Release -- eval-match --variant All --time-ms 1000 --workers 4 --ballots 50

# Verify or expand the Phase 7 12-Ply Drop-Out Expansion Opening Books
dotnet run --project tools/Checkers.Benchmark/Checkers.Benchmark.csproj -c Release -- book verify --variant All
dotnet run --project tools/Checkers.Benchmark/Checkers.Benchmark.csproj -c Release -- book expand-doe --variant English --max-ply 12 --full-width-plies 4 --search-depth 16 --max-delta 30

# Run the 40-position standard and deep TT suites
dotnet run --project tools/Checkers.Benchmark/Checkers.Benchmark.csproj -c Release -- --deep
```

