# Engine Evaluation Improvements Plan (Based on `Checkers-Engine-main`)

We want to improve the static evaluation of the Checkers engine, inspired by [`board_eval.c`](Checkers-Engine-main/src/engine/board_eval.c) from [`Stermere/Checkers-Engine`](https://github.com/Stermere/Checkers-Engine).

The evaluation in [`board_eval.c`](Checkers-Engine-main/src/engine/board_eval.c) is designed for the **English Checkers** (1-step Kings) variant and can be adapted **1-to-1** for our English Checkers mode, while serving as the foundation for the **International Draughts (Flying Kings)** mode (retaining the Men and structural evaluation from `board_eval.c` and updating the King evaluation for long-range Flying Kings).

---

## 1. Key Technical Findings & Agreed Design Decisions

### 1.1 Bitboard Coordinate Compatibility
- The 64-bit bitboard square layout in `board_eval.c` (`sq = (row << 3) | col`, dark squares `(row + col) % 2 == 1`, `p1` = White moving toward Row 0, `p2` = Black moving toward Row 7) is **100% bit-for-bit identical** to `BitPosition` and `BitboardMasks` in `Checkers.Core`.
- All bitmasks and Piece-Square Tables (PSTs) in `board_eval.c` map directly to our bitboards without bit reversal.

### 1.2 $2\times$ Centipawn Scale Calibration (`Man = 100 cp`)
- In `board_eval.c`, a Man is worth `50` and an English King is worth `70` (with search pruning margins `RFP_MARGIN = 20` and `FUTILITY_MARGIN = 30`).
- In `MinimaxPlayer.cs`, our search pruning margins (`RfpMargin = 40`, `FutilityMargin = 60`, `DominantMoveMargin = 150`) are calibrated to `ManValue = 100` (exactly $2\times$ the scale of `board_eval.c`).
- **Decision:** All evaluation weights from `board_eval.c` are scaled by **$2\times$** (`Man = 100`, `English King = 140`, etc.), preserving exact 1:1 relative weights with `board_eval.c` while keeping all search pruning thresholds in exact calibration.

### 1.3 Bitboard POPCNT Vectorization
- Because `piece_pos_map_p1` only uses weights `{1, 2, 3}` and `king_pos_map` only uses weights `{4, 5}`, we can evaluate the Piece-Square Tables and late-game advancement (`king_dist`) across all pieces simultaneously in $O(1)$ using hardware `BitOperations.PopCount` masks, iterating individual bits (`BitOperations.TrailingZeroCount`) only when checking Runaway Checkers (`enemyKings == 0`).

---

## 2. Variant-Specific Evaluation Specifications (Scaled $2\times$ to Centipawns)

### 2.1 Shared Men & Structural Evaluation (Both English & Flying Kings)
1. **Man Material:** `+100 cp` per Man (`50 × 2`).
2. **Man Piece-Square Table (`piece_pos_map_p1` / `piece_pos_map_p2`):**
   - Back-rank defense squares `(7,2), (7,4), (7,6)` for White (`(0,1), (0,3), (0,5)` for Black): `+6 cp` (`3 × 2`).
   - Advanced outpost squares `(2,3)` (`+6 cp`) and `(2,5)` (`+4 cp`) for White (mirrored `(5,4)` and `(5,2)` for Black).
   - Central control squares on rows `3..5`, cols `2..5` + tempo square `(6,7)` / `(1,0)`: `+2 cp` (`1 × 2`).
3. **Late-Game Man Advancement (`king_dist`):**
   - Active **only when `totalPieces <= 12`** (prevents premature opening over-extension that weakens the back rank):
   - White Man on `row`: `+(8 - row) × 2 cp`; Black Man on `row`: `+row × 2 cp`.
4. **Runaway Checker / Unstoppable Passer (`compute_runaway_cones`):**
   - Precomputed 64-entry forward diagonal promotion cone bitmasks (`ConeWhite[64]`, `ConeBlack[64]`).
   - When the opponent has **no Kings** (`enemyKings == 0`), the promotion cone ahead of a Man is empty (`(Cone[sq] & allPieces) == 0`), and no enemy Men sit on the rows ahead of `sq`, awards **`+(40 + 6 × advanceSteps) cp`** (`(20 + 3 × advanceSteps) × 2`).
5. **Piece-Advantage Simplification / Trade Bonus (`calculatePieceBonus`):**
   - Awarded to the player with strictly more total pieces (`whiteCount > blackCount` or `blackCount > whiteCount`) to incentivize trading down when ahead:
     - `> 6` pieces: `+20 cp` (`10 × 2`)
     - `6` pieces: `+60 cp` (`30 × 2`)
     - `5` pieces: `+120 cp` (`60 × 2`)
     - `4` pieces: `+200 cp` (`100 × 2`)
     - `3` pieces: `+300 cp` (`150 × 2`)
     - `<= 2` pieces: `+400 cp` (`200 × 2`)
6. **Classical Checkers Structural Patterns:**
   - **Right Lock Pattern:** `+40 cp` (`20 × 2`) — Man on `(4,7)` pinning enemy Man on `(3,6)` (mirrored `(3,0)` pinning `(4,1)`).
   - **Bridge Pattern:** `+30 cp` (`15 × 2`) — Back-rank bridge on `(7,2) & (7,6)` (mirrored `(0,1) & (0,5)`).
   - **Triangle Pattern:** `+20 cp` (`10 × 2`) — Formation on `(7,4), (7,6), (6,5)` (mirrored `(0,1), (0,3), (1,2)`).
   - **Oreo Pattern:** `+20 cp` (`10 × 2`) — Formation on `(7,2), (7,4), (6,3)` (mirrored `(0,3), (0,5), (1,4)`).
   - **Dog Pattern:** `+10 cp` (`5 × 2`) — Man on `(7,6)` holding enemy Man on `(6,7)` (mirrored `(0,1)` holding `(1,0)`).

### 2.2 English Checkers (1-Step Kings) — 1:1 with `board_eval.c`
1. **English King Material:** `+140 cp` (`70 × 2`, `1.40×` Man).
2. **English King PST (`king_pos_map`):** Central and inner-ring squares (`rows 1..6, cols 1..6`) receive `+8 cp` (`4 × 2`) or `+10 cp` (`5 × 2`).
3. **King Tail Pins (`tail_pins`):** `+10 cp` (`5 × 2`) per King standing directly behind two consecutive enemy Men along the same diagonal.
4. **Trapped Single-Corner King Penalty:** `-40 cp` (`-20 × 2`) when a White King sits on `(0,7)` (`0x80`) or a Black King sits on `(7,0)` (`0x0100000000000000`).

### 2.3 International Draughts (Flying Kings) — Adapted King Evaluation
1. **Flying King Material:** `+280 cp` (`140 × 2`, `2.80×` Man) reflecting long-range diagonal slide and jump power.
2. **Flying King Diagonal & Center PST:**
   - Rewards squares on the **8-square Main Long Diagonal** (`(7,0)–(0,7)`): `+12 cp`, plus central and **6-square Double-Diagonal** squares: `+8` to `+10 cp`.
   - **Corner Adjustment:** Unlike 1-step English Kings, a Flying King on `(0,7)` or `(7,0)` is **not** trapped—it commands the entire 8-square main diagonal in a single move. Instead, `-20 cp` is applied to the 2-square short corners (`(0,1)/(1,0)` and `(6,7)/(7,6)`).
3. **Tail Pins & Open-Ray Diagonal Mobility:**
   - Includes `tail_pins` (`+10 cp` per pin) plus **`+2 cp` per open diagonal ray** (unblocked by friendly pieces) controlled by a Flying King.

---

## 3. Bot-vs-Bot Self-Play Match Specification (50 Positions × 2 Sides = 100 Games / Variant)

1. **50 Even Starting Positions (5–10 Plies from Start):**
   - Generate 50 deterministic, unique opening positions at `6–10 plies` from the initial board (separately for English and International rules).
   - Filter each candidate position with a shallow search (`depth 6`) so that:
     - Material is strictly equal (`12v12` or `11v11` Men, `0` Kings).
     - No mandatory capture is immediately pending (`!HasAnyCapture`).
     - The evaluation is balanced within `[-30 cp, +30 cp]`.
2. **Color-Swapped Paired Games (100 Games per Variant):**
   - Each of the 50 positions is played **twice**:
     - Game A: `NewEvaluation` (White) vs. `LegacyEvaluation` (Black)
     - Game B: `LegacyEvaluation` (White) vs. `NewEvaluation` (Black)
3. **Complete Per-Engine Data Structure Isolation:**
   - Each engine opponent in every game has its own isolated:
     - `MinimaxPlayer` instance (isolating `_killers`, `_history`, `_moveBuffers`, and `_drawTable`).
     - `TranspositionTable` instance (`32 MiB`, cleared via `.Clear()` before each game, and aged via `.NewSearch()` across turns within the game).
   - `IEvaluationFunction` will expose a zero-allocation `int Evaluate(in BitPosition pos)` overload so neither engine incurs `BoardState` heap allocations during search.
4. **Time Control & Parallel Execution:**
   - **Default Time Limit:** **`1000 ms` (`1.0 s`) per move** (`--time-ms 1000`, configurable via CLI).
   - **Parallel Workers:** Runs **`4` isolated games in parallel** (`--workers 4`, configurable via CLI) to keep CPU cores unthrottled while finishing each 100-game match ~4× faster.
   - **Reported Metrics:** Wins, Losses, Draws, White/Black breakdown, paired ballot outcomes (`2-0`, `1.5-0.5`, `1-1`, `0.5-1.5`, `0-2`), and **Elo difference ($\Delta\text{Elo}$) with 95% confidence interval**.

---

## 4. Evaluation Speed & Search Throughput Benchmark (`Legacy` vs. `New`)

To quantify the exact performance impact of the richer evaluation functions on both raw evaluation latency and full search speed, we will include an `eval-speed` benchmark mode in `tools/Checkers.Benchmark` covering both **English** and **International (Flying Kings)** variants:

1. **Part A — Raw Static Evaluation Microbenchmark (`M evals/s` & `ns/eval`):**
   - Evaluates all 40 benchmark positions (`Openings`, `Middlegames`, `Endgames`, `Blockades`) in a tight loop (`10,000,000` evaluations per variant) to measure raw `LegacyEvaluationFunction.Evaluate(in pos, variant)` vs. `EvaluationFunction.Evaluate(in pos, variant)` throughput (`million evals/sec` and `nanoseconds/eval`).
2. **Part B — 40-Position Fixed-Depth Search Speed & Node Comparison (`No-TT` and `1M TT`):**
   - Runs the 40-position suite at identical fixed depths comparing `LegacyEvaluationFunction` vs. `EvaluationFunction` to measure:
     - **Search Throughput (`M nodes/s` and `M leaf-evals/s`)**
     - **Total Evaluated Nodes & Wall-Clock Time (`ms`)** (measuring how the richer evaluation affects alpha-beta cutoff efficiency as well as per-node speed).
3. **Part C — 5-Position Timed Depth Comparison (`5.0s` / Position, `16M TT`):**
   - Compares completed iterative deepening depth (`plies`), `NodesEvaluated`, `LeafEvaluations`, and `NPS` between the old and new evaluators under a fixed 5-second clock.

---

## 5. Empirical Results Summary (Completed)

1. **Raw Static Evaluation Speed (`10,000,000` Evaluations / Variant):**
   - **English Checkers:** `113.0M evals/s` (`8.8 ns/eval`) vs. `121.9M evals/s` (`8.2 ns/eval`) for `Legacy` (`+0.6 ns` overhead).
   - **International (Flying Kings):** `76.1M evals/s` (`13.1 ns/eval`) vs. `130.3M evals/s` (`7.7 ns/eval`) for `Legacy`.
2. **40-Position Fixed-Depth Search Speed (`1M TT`):**
   - **English:** `2,069,177` nodes (`-0.85%` fewer nodes) in `224 ms` (`9.20M nodes/s`) vs. `2,086,849` nodes in `222 ms` (`Legacy`).
   - **International:** `1,947,323` nodes (**`-15.18%` fewer nodes**) in **`193 ms`** (**`-6.76%` faster wall-clock time**) vs. `2,295,742` nodes in `207 ms` (`Legacy`).
3. **200-Game Bot-vs-Bot Self-Play Verification (`1000 ms/move`, 50 Balanced Ballots × 2 Sides per Variant):**
   - **English Checkers (100 Games):** **`69.5 / 100` (`69.5%`, `+41 = 57 −2`, `+143.1 ± 42.9 Elo`, `100.0% LOS`)** — `0` losses as Black (`+20 = 30 −0`) and `7` `2-0` ballot sweeps vs. `0` for `Legacy`.
   - **International Checkers (100 Games):** **`56.5 / 100` (`56.5%`, `+22 = 69 −9`, `+45.4 ± 37.8 Elo`, `99.2% LOS`)** after gating the Flying Kings simplification bonus on material advantage ($\ge 80\text{ cp}$, which produced a **`+83.8 Elo` swing** over raw piece-count comparison `-38.4 Elo`).
   - See [docs/brain/05-evaluation.md](docs/brain/05-evaluation.md) and [docs/brain/11-engine-improvements.md](docs/brain/11-engine-improvements.md#milestone-8-phase-6-static-evaluation-overhaul--200-game-bot-vs-bot-verification) for full documentation.