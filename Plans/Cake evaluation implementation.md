# Engine Evaluation Improvements Plan for **English Checkers** (Based on `cake-master`)

We want to evaluate a **1-to-1 C# port** of Martin Fierz's **Cake 1.89g** static evaluation function ([`cake-master/cake_eval_parametrized.c`](file:///c:/Udvikling/Spil/Checkers/cake-master/cake_eval_parametrized.c)) for our **English Checkers (1-step Kings)** mode and benchmark it against both `LegacyEvaluationFunction` and our current `board_eval.c` English evaluator (`EvaluationFunction.EvaluateEnglish`).

---

## 1. Analysis of `cake-master` & Surrounding Files

### 1.1 Surrounding Files Examined
1. **[`cake-master/switches.h`](file:///c:/Udvikling/Spil/Checkers/cake-master/switches.h)**:
   - Defines engine compile-time flags for Cake 1.89g:
     - `BR4` (`BRNUM = 4096 * 16 = 65536`): Uses a 16-bit (4-rank) single-color back-rank & development lookup table `backrank[65536]`.
     - `COARSEGRAINING` with `GRAINSIZE = 2`: Quantizes the final score via `eval = (eval / (2 * GRAINSIZE)) * GRAINSIZE` (`(eval / 4) * 2`).
     - `RARELYUSED` is `#undef` and `DONTSUBTRACTTEMPO` is `#define` at the top of `cake_eval_parametrized.c` (`lines 38–39`).
2. **[`cake-master/consts.h`](file:///c:/Udvikling/Spil/Checkers/cake-master/consts.h)**:
   - Defines Cake's 32-bit dark-square bitboard layout (`BIT0..BIT31` and PDN squares `SQ1..SQ32`), diagonal step macros (`leftforward`, `rightforward`, `leftbackward`, `rightbackward`, `forward`, `backward`, `twoleftforward`, `tworightforward`, `twoleftbackward`, `tworightbackward`), jump-attack masks (`LFJ1, LFJ2, RFJ1, RFJ2, LBJ1, LBJ2, RBJ1, RBJ2`), structural region masks (`RANK1..RANK8`, `ROW1..ROW8`, `EDGE`, `CENTER`, `MCENTER`, `ROAMINGBLACKKING`, `ROAMINGWHITEKING`), and runaway promotion cone masks (`RA20..RA23`, `RA8..RA11`, `RA16..RA19`, `RA12..RA15`).
3. **[`cake-master/structs.h`](file:///c:/Udvikling/Spil/Checkers/cake-master/structs.h) & [`cake-master/cake_eval.h`](file:///c:/Udvikling/Spil/Checkers/cake-master/cake_eval.h)**:
   - Defines `POSITION` (`uint32 bm, bk, wm, wk; int color`), `MATERIALCOUNT` (`bm, bk, wm, wk`), `KINGINFO` (`freebk, freewk, untrappedbk, untrappedwk`), `EVALUATION` (`material, selftrap, backrank, men, king, king_man, runaway, cramp, hold, compensation`), and the `enum` of 67 tuned evaluation parameters (`v[man_value]` through `v[edgepressure2]`).
4. **[`cake-master/pattern.c`](file:///c:/Udvikling/Spil/Checkers/cake-master/pattern.c) (Excluded / `#undef PATTERNS`)**:
   - Contains 124,406 lines (`2.4 MB` of C source, `~5.74 MB` of `short[]` arrays `pat1[531441]`, `pat2[781250]`, `pat3[781250]`, `pat4[781250]`).
   - **Why we exclude `pattern.c`:** `cake_eval_parametrized.c` is 100% self-contained when `PATTERNS` is disabled (it already includes the 65,536-entry 4-rank `backrank[BRNUM]` table directly in `cake_eval_parametrized.c:125-2185`). Including `5.74 MB` of random-access pattern arrays would thrash the CPU L3 cache and compete directly with our Transposition Table.

---

### 1.2 Bitboard Coordinate Isomorphism (Our 64-Bit `BitPosition` $\leftrightarrow$ Cake's 32-Bit `POSITION`)

A critical detail discovered while examining [`consts.h`](file:///c:/Udvikling/Spil/Checkers/cake-master/consts.h#L71-L231):
- In our 64-bit `BitPosition`, **Black** starts on `Rows 0..2` (back rank = `Row 0`) and **White** starts on `Rows 5..7` (back rank = `Row 7`).
- On `Row 0` in our 64-bit bitboard:
  - `(0,1)` (bit 1) is in the **Double Corner** (`SQ1` in PDN / `BIT3` in Cake).
  - `(0,3)` (bit 3) is `SQ2` in PDN / `BIT2` in Cake.
  - `(0,5)` (bit 5) is `SQ3` in PDN / `BIT1` in Cake.
  - `(0,7)` (bit 7) is in the **Single Corner** (`SQ4` in PDN / `BIT0` in Cake).
- Therefore, extracting the 32 dark squares with hardware BMI2 `PEXT` (`Bmi2.X64.ParallelBitExtract(bb64, 0x55AA55AA55AA55AAUL)`) and **reversing each 4-bit rank nibble** produces an **exact 1-to-1 graph isomorphism** with Cake's 32-bit bitboard:
  ```csharp
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static uint ToCakeBitboard(ulong bb64)
  {
      uint p = Bmi2.X64.IsSupported
          ? (uint)Bmi2.X64.ParallelBitExtract(bb64, 0x55AA55AA55AA55AAUL)
          : SoftwarePackDarkSquares(bb64);
      // Reverse each 4-bit rank nibble so (0,7)->BIT0(SQ4), (0,5)->BIT1(SQ3), (0,3)->BIT2(SQ2), (0,1)->BIT3(SQ1):
      return ((p & 0x11111111u) << 3)
           | ((p & 0x22222222u) << 1)
           | ((p & 0x44444444u) >> 1)
           | ((p & 0x88888888u) >> 3);
  }
  ```
- Under this mapping, every single square mask (`SQ1..SQ32`, `BIT0..BIT31`), shift macro (`leftforward`, `rightforward`, etc.), and lookup table (`backrank[65536]`, `king_pst[32]`, `blackbackrankpower[256]`, `whitebackrankpower[256]`) in `cake_eval_parametrized.c` works **1-to-1 without modification**.

---

### 1.3 Complete Inventory of Cake's Evaluation (`cake_eval_parametrized.c`)

1. **Scale & Perspective**:
   - In `optimalparams()`, `v[man_value] = 203` and `v[king_value] = 266`.
   - At the end of `evaluation()`, the total score is divided by `2` and quantized to even centipawns (`eval = (eval / 4) * 2`), so **1 Man $\approx 100\text{ cp}$** and **1 King $\approx 133\text{ cp}$**—matching our engine's centipawn scale (`ManValue = 100`).
   - Cake computes `eval` from **Black's perspective** (`+` = Black advantage) and returns `(p->color == BLACK) ? eval : -eval`. In our engine, `IEvaluationFunction.Evaluate` always returns the score from **White's perspective** (`+` = White advantage), so our port returns `-eval_black`.
2. **Precomputed Tables (`initeval()`)**:
   - `materialeval[13][13][13][13]` (`57 KB`): Nonlinear material value table indexed by `[bm][bk][wm][wk]`, incorporating `man_value` (`203`), `king_value` (`266`), `exchangebias` (`1`), 4v5 / 5v6 `piecedown_9` (`42`) & `piecedown_11` (`30`), and equal-piece `twokingbonus_10` (`-14`) & `twokingbonus_12` (`-5`).
   - `blackbackrankeval[65536]` & `whitebackrankeval[65536]` (`128 KB`): 4-rank single-color man configuration table (`BR4`), indexed by `bm & 0xFFFF` for Black and `wm >> 16` (via `reverse16`) for White.
   - `blackbackrankpower[256]` & `whitebackrankpower[256]`: Back-rank defensive power tables for solo-King and man-down compensation.
3. **`fineevaluation()` Positonal Heuristics**:
   - **Phase-Dependent Tempo**: `tempo * tmod[stones]` weighted by row advancement when `bm == wm`.
   - **Men (`e.men`)**:
     - Immobile men (`immobile_mult = 3`, `immobile_mult_kings = 2`).
     - Ungrounded men (`ungroundedpenalty[0..12]` nonlinear lookup table).
     - Left-right balance (`balancemult = 3`) and phase-interpolated edge/center skewness (`skewnessmult = 34`, `skewnessmult_eg = -4`).
     - Dogholes (`dogholeval = 4`, `dogholeval2 = 6`, `dogholemandownval = -16`).
     - Center control by grounded occupation (`mc_occupyval = -4`) and jump attack (`mc_attackval = 4`).
     - Real Dyke (`realdykeval = -5`) & Great Dyke (`greatdykeval = 6`).
   - **Cramps (`e.cramp`)**: `cramp20 = 9`, `nocrampval20 = 1`, phase-interpolated `cramp13 = 42` / `cramp13_eg = 25`, `nocrampval13 = 4`.
   - **Holds / Bad Structures (`e.hold`)**: `badstructure = 11`, `badstructure7 = 26`, `badstructure8 = 4`, `badstructure9 = 17`, `badstructure11 = 22`.
   - **Runaways & Bridges (`e.runaway`)**:
     - Runaway in 1 (`promoteinone = 31`, single-corner trap check, `runaway_destroys_backrank = 7`, increments `potbk`/`potwk`).
     - Runaway in 2 (`promoteintwo = 16` via `RA20..RA23` / `RA8..RA11` cones, increments `potbk`/`potwk`).
     - Runaway in 3 (`promoteinthree = -8` via `RA16..RA19` / `RA12..RA15` cones).
     - Regular Bridge (`SQ23` / `SQ10`) and "Other" Bridge (`SQ22` / `SQ11`) breakouts.
   - **Kings (`e.king`)**:
     - Piece-square table `king_pst[32]` (`bm` vs `31 - m`).
     - Trapped Kings in single corner by 1 man (`kingtrappedinsinglecornerval = -15`) or 2 men (`kingtrappedinsinglecornerbytwoval = -24`), trapped Kings in bridges (`SQ30, SQ31` / `SQ2, SQ3`), trapped Kings in double corner (`kingtrappedindoublecornerval = 23`), decrementing `freebk`/`freewk` and clearing `untrappedbk`/`untrappedwk`.
     - Dominated single King in single corner (`king_blocks_king_and_man = 69`, `dominatedkingval = 5`) and double corner (`dominatedkingindcval = 48`).
     - 3-step safe King mobility flood-fill over non-attacked squares (`kingmobility[0..9]`).
     - King center monopoly (`kingcentermonopoly = 4`).
     - Only-King bonus (`onlykingval = 13` + `backrankpower` + `roamingkingval = 11`).
   - **King-Man Interaction (`e.king_man`)**:
     - Experimental king cramp (`experimental_king_cramp = 94`).
     - Tailhooks (`tailhookval = 25`, `keval = 7` edge relief).
     - 1-step and 2-step King proximity to ungrounded enemy men (`kingproximityval1 = 10`, `kingproximityval2 = 3`).
     - Endangered bridgeheads (`endangeredbridge_kingdown = 12`).
     - 1 King holding 2 edge men in endgames (`kingholdstwomenval = 19` when `pieces <= 14`).
   - **Compensation (`e.compensation`)**:
     - No-king equal-men runaway + strong back rank (`compensation = 118` when `bm == wm <= 7`).
     - No-king man-down runaway + strong back rank (`compensation_mandown = 27` when `stones > 12`).
   - **Side-to-Move Turn & Draw Scaling**:
     - Phase-interpolated `turnval = -4` / `turnval_eg = 4`.
     - `likelydraw` halving (`eval /= 2`) in equal-piece endings (`4v4` or `5v5`) where `wm - potwk <= 1` and `bm - potbk <= 1`.

---

## 2. Implementation & Testing Plan

### Phase 1: 1-to-1 Port of `cake_eval_parametrized.c` & Unit Tests
1. **[NEW] [`src/Checkers.Core/AI/CakeBackRankTable.cs`](file:///c:/Udvikling/Spil/Checkers/src/Checkers.Core/AI/CakeBackRankTable.cs)**:
   - Store the 65,536-entry `backrank` table from `cake_eval_parametrized.c:125-2185` (`sbyte[]` / `short[]`) and precomputed `BlackBackRankEval[65536]` and `WhiteBackRankEval[65536]` (via `Reverse16`).
2. **[NEW] [`src/Checkers.Core/AI/CakeEnglishEvaluator.cs`](file:///c:/Udvikling/Spil/Checkers/src/Checkers.Core/AI/CakeEnglishEvaluator.cs)**:
   - Implement the exact 1-to-1 port of `cake_eval_parametrized.c` (`ToCakeBitboard`, `materialeval[13][13][13][13]`, `blackbackrankpower[256]`, `whitebackrankpower[256]`, and `fineevaluation()`).
3. **[MODIFY] [`src/Checkers.Core/AI/EvaluationFunction.cs`](file:///c:/Udvikling/Spil/Checkers/src/Checkers.Core/AI/EvaluationFunction.cs)**:
   - Route `EvaluateEnglish(in BitPosition pos)` to `CakeEnglishEvaluator.Evaluate(in pos)` while retaining `EvaluateEnglishBoardEval(in BitPosition pos)` (our fast `board_eval.c` port) so both can be benchmarked and compared directly.
4. **[MODIFY] [`tests/Checkers.Core.Tests/EnglishCheckersTests.cs`](file:///c:/Udvikling/Spil/Checkers/tests/Checkers.Core.Tests/EnglishCheckersTests.cs)**:
   - Add unit tests verifying `ToCakeBitboard` coordinate mapping, 180° color/board symmetry (`CakeEnglishEvaluator.Evaluate(pos) == -CakeEnglishEvaluator.Evaluate(Mirror(pos))`), starting position evaluation, runaway bonuses, trapped-king penalties, and search integration.

### Phase 2: Speed Benchmark & 100-Game English Checkers Matches
1. **Raw & Search Speed Benchmark (`eval-speed --variant english`)**:
   - Compare raw evaluation throughput (`ns/eval`, `M evals/s`) and 40-position fixed-depth search speed across:
     - `LegacyEvaluationFunction` (`6.0 ns/eval`)
     - `board_eval.c` (`EvaluateEnglishBoardEval`, `8.9 ns/eval`)
     - `CakeEnglishEvaluator` (`cake_eval_parametrized.c` port)
2. **100-Game Self-Play Matches (`1000 ms/move`, `4` workers, English Checkers)**:
   - **Match 1 (Head-to-Head):** `CakeEnglishEvaluator` vs. `board_eval.c` (`EvaluateEnglishBoardEval`) — 100 games (50 two-game openings with colors swapped) at `1000 ms/move` to directly test whether Cake's deep positional evaluation beats the faster `board_eval.c` port!
   - **Match 2 (vs. Legacy Baseline, if needed):** `CakeEnglishEvaluator` vs. `LegacyEvaluationFunction` at `1000 ms/move` (comparing against `board_eval.c`'s `69.5 / 100`, `+143.1 Elo`).

### Phase 3: Decision & Documentation (✅ COMPLETED — Cake Code Removed, `board_eval.c` Retained)
1. Based on the empirical benchmark and 100-game match results below, `board_eval.c` (`+143.1 Elo`, `8.8 ns/eval`) outperformed the Cake 1.89g port (`+115.2 Elo`, `62.1 ns/eval`) by **`+27.9 Elo`**.
2. Removed `CakeEnglishEvaluator.cs` and `CakeBackRankTable.cs` from the codebase and documented the experiment and reasons for removal in [`docs/brain/05-evaluation.md`](file:///c:/Udvikling/Spil/Checkers/docs/brain/05-evaluation.md) and [`docs/brain/11-engine-improvements.md`](file:///c:/Udvikling/Spil/Checkers/docs/brain/11-engine-improvements.md).

---

## 3. Empirical Results & Reason for Removal

### 3.1 Speed & 100-Game Match Comparison (`1000 ms/move`, `50` Balanced Ballots $\times$ `2` Sides)

| Metric | **Retained Phase 6 (`board_eval.c` Port)** | **Phase 6c (`Cake 1.89g` 1:1 Port — Removed)** | `LegacyEval` (`LegacyEvaluationFunction`) |
| :--- | :---: | :---: | :---: |
| **Raw Eval Latency (`10M` Evals)** | **`8.8 ns/eval` (`113.0M/s`)** | `62.1 ns/eval` (`16.1M/s`) | `8.2–8.4 ns/eval` (`118.6–121.9M/s`) |
| **40-Pos Fixed-Depth (`No-TT`)** | **`6,860,691` nodes (`471 ms`, `14.54M/s`)** | `8,833,088` nodes (`932 ms`, `9.47M/s`) | `5,868,002` nodes (`356 ms`, `16.45M/s`) |
| **40-Pos Fixed-Depth (`1M TT`)** | **`2,069,177` nodes (`224 ms`, `9.20M/s`)** | `2,820,120` nodes (`371 ms`, `7.60M/s`) | `2,086,849` nodes (`202 ms`, `10.30M/s`) |
| **Total Score (100 Games vs. `Legacy`)** | **69.5 / 100** (**69.5%**) | 66.0 / 100 (66.0%) | 30.5–34.0 / 100 |
| **Wins / Draws / Losses** | **+41 = 57 −2** | +36 = 60 −4 | +4 = 60 −36 *(vs. Cake)* |
| **As White / As Black (50 Games each)** | **+21 = 27 −2** / **+20 = 30 −0** | +20 = 29 −1 / +16 = 31 −3 | — |
| **Paired 50-Ballot Breakdown (`2-0` / `1.5-0.5` / `1-1` / `0.5-1.5` / `0-2`)** | **`7` / `27` / `14` / `2` / `0`** | `4` / `26` / `18` / `2` / `0` | — |
| **Elo Difference ($\Delta\text{Elo} \pm 95\%\text{ CI}$)** | **+143.1 ± 42.9 Elo** (**LOS: 100.0%**) | +115.2 ± 41.9 Elo (LOS: 100.0%) | Baseline |
| **Average Search Depth** | **`22.86 plies`** *(−0.14 vs. Legacy)* | `23.07 plies` *(**−1.19 vs. Legacy**)* | `23.00–24.26 plies` |
| **Average Search Speed (NPS)** | **`10.98M nodes/s`** | `7.51M nodes/s` *(**−31.6% vs. Phase 6**)* | `11.71–11.78M nodes/s` |
| **Average Game Length** | **`120.3 plies`** | `130.9 plies` | — |

### 3.2 Why the Cake Evaluation Code Was Removed
1. **$7\times$ Slower Leaf Evaluation & `-1.19 Plies` Search Depth Penalty:** Running all 8 sequential passes of `fineevaluation()` unconditionally at every leaf node increased raw evaluation latency from **`8.8 ns/eval`** to **`62.1 ns/eval`**, reducing full-search node throughput by **`31.6%`** (`10.98M/s` $\to$ `7.51M/s`) and costing **`1.19 plies`** of search depth (`23.07` vs. `24.26 plies`).
2. **Coarse-Graining & Node Inflation (`+36.3%`):** Cake's `COARSEGRAINING` (`eval = (eval / 4) * 2`) rounds evaluations to even steps, increasing evaluation ties across quiet moves and inflating fixed-depth `1M TT` node counts from `2.07M` to `2.82M` (`+36.3%`).
3. **Reliance on Windowed Lazy Evaluation & Endgame Tablebases:** In C, Cake avoids paying the `62 ns` `fineevaluation()` cost on most nodes via two-stage windowed lazy exits (`alpha ± FINEEVALWINDOW`) and uses an 8-piece endgame tablebase (`db_lookup`) to resolve simplified positions where `likelydraw` halves the static evaluation (`eval /= 2`). In our tablebase-free engine, `board_eval.c`'s $O(1)$ hardware `POPCNT` bitmasks (`8.8 ns/eval`) and aggressive simplification bonus (`CalculatePieceBonus`) achieve higher search depth and stronger endgame conversion (**`+143.1 Elo`** vs. **`+115.2 Elo`**).