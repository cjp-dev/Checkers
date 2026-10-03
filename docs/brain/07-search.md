# 07 – Search

[Back to the index](README.md)

## In short

The search engine decides which move the computer will play. [`MinimaxPlayer`](../../src/Checkers.Core/AI/MinimaxPlayer.cs) implements the **Negamax formulation of Alpha-Beta pruning with Iterative Deepening, Transposition Table caching, and Quiescence Search**, powered by an allocation-free 64-bit bitboard copy-make architecture (`BitPosition` and `BitboardMoveGenerator`; see [Chapter 10 – Bitboards](10-bitboards.md)).

Search limits are configured in the **Game -> Settings...** dialog ([Chapter 09](09-time-control.md)):
* **Fixed depth:** $1$ to $20$ plies (default: $8$ plies)
* **Time per move:** $1$ to $60$ seconds (default: $5$ seconds)
* **Time per game:** $1$ to $60$ minutes (default: $5$ minutes)

![Negamax Alpha-Beta Pruning and Quiescence Search Extension](images/alpha-beta-quiescence.svg)

---

## Negamax Alpha-Beta Algorithm

The minimax principle assumes optimal play from both sides: the active player chooses the move that maximizes their score, while the opponent chooses replies that maximize their own score (minimizing the active player's score).

Because our static evaluation ([Chapter 05](05-evaluation.md)) is symmetric with respect to `SideToMove`, $\max(a, b) = -\min(-a, -b)$, and both players share a single recursive **Negamax** recurrence:

$$V(s, d, \alpha, \beta) = \begin{cases} \text{LossScore} + \text{ply} & \text{if } |P_{\text{own}}(s)| = 0 \lor \neg\text{HasAnyLegalMove}(s) \\ 0 & \text{if } \text{HalfMoveClock}(s) \ge 80 \\ \text{Quiescence}(s, \alpha, \beta) & \text{if } d = 0 \\ \displaystyle\max_{m \in \text{Moves}(s)} \Bigl( -V\bigl(\text{Apply}(s, m),\, d - 1,\, -\beta,\, -\alpha\bigr) \Bigr) & \text{if } d > 0 \end{cases}$$

```mermaid
flowchart TD
    Entry["NegaMax(pos, depth, α, β, ply)"] --> Term{"Terminal check:<br/>0 pieces, 0 moves,<br/>or 40-move draw?"}
    Term -- "Loss" --> RetLoss["Return −100,000 + ply"]
    Term -- "Draw" --> RetDraw["Return 0"]
    Term -- "InProgress" --> TTProbe["Probe Transposition Table (pos.Hash)"]
    TTProbe --> TTCut{"TT Cutoff at<br/>depth >= d?"}
    TTCut -- "Yes" --> RetTT["Return cached ttScore"]
    TTCut -- "No" --> Depth0{"Is depth == 0?"}
    Depth0 -- "Yes" --> Quiesce["Run Quiescence(pos, α, β)<br/>(Extend capture chains until quiet)"]
    Depth0 -- "No" --> GenOrder["Generate moves into _moveBuffers[ply]<br/>& OrderBitMoves(TT move, Captures, Promotions)"]
    GenOrder --> Loop["For each move m: nextPos = pos.Apply(in m)<br/>score = −NegaMax(nextPos, d−1, −β, −α, ply+1)"]
    Loop --> UpdateAlpha["α = max(α, score)"]
    UpdateAlpha --> Cutoff{"Is α >= β?"}
    Cutoff -- "Yes (Beta Cutoff)" --> StoreTT["Store LowerBound/Exact/UpperBound in TT<br/>& Return maxScore"]
    Cutoff -- "No (More moves)" --> Loop
    Cutoff -- "All moves searched" --> StoreTT
```

### Alpha ($\alpha$) and Beta ($\beta$) Window Bounds
* **$\alpha$ (Lower Bound):** The minimum score the side to move is already guaranteed to achieve along a previously explored path.
* **$\beta$ (Upper Bound):** The worst score the opponent will tolerate; if the side to move finds a move with $\text{score} \ge \beta$, the opponent will avoid this node entirely by choosing a different branch one ply earlier (**$\beta$-cutoff**).
* When passing bounds to the recursive child call (`SideToMove` flips), the window is negated and swapped: $[\alpha, \beta] \mapsto [-\beta, -\alpha]$.

---

## Quiescence Search & The Horizon Effect

If a fixed-depth search stops abruptly at `depth == 0` and evaluates the board in the middle of a capture exchange—for example, right after White captures a man, when Black has a mandatory recapture on the very next ply—the static evaluator will misjudge the position by $+100$ centipawns. This pathology is known as the **Horizon Effect**.

To eliminate horizon blunders, `MinimaxPlayer` transitions at `depth == 0` into **`Quiescence`** search, which expands only **tactical capture moves** until no captures remain:

$$Q(s, \alpha, \beta) = \max\Biggl(\text{Evaluate}(s),\; \max_{m \in \text{Captures}(s)} \Bigl(-Q\bigl(\text{Apply}(s, m), -\beta, -\alpha\bigr)\Bigr)\Biggr)$$

1. **Stand-Pat Evaluation:** First, compute `int standPat = EvaluatePosition(in pos);` and increment `LeafEvaluations`.
2. **Stand-Pat Beta Cutoff:** If $\text{standPat} \ge \beta$, return $\beta$ immediately.
3. **Alpha Raise:** If $\text{standPat} > \alpha$, set $\alpha = \text{standPat}$.
4. **Capture-Only Expansion:** Call `BitboardMoveGenerator.GenerateCaptures(in pos, _variant, moveBuffer)`. If `captureCount == 0` (the position is *quiet*), return $\alpha$. Otherwise, order the captures by `CaptureCount` and recursively search each capture until a quiet position is reached (capped at safety ply `24`).

---

## Distance-to-Mate Scoring

When a terminal win or loss is detected at search depth `ply` from the root, the terminal score is offset by `ply`:

$$\text{Score}_{\text{win}}(\text{ply}) = +100{,}000 - \text{ply}, \qquad \text{Score}_{\text{loss}}(\text{ply}) = -100{,}000 + \text{ply}$$

Incorporating `ply` guarantees two critical behaviors:
1. **Fastest Win When Ahead:** A forced win in $3$ plies ($+99{,}997$) scores strictly higher than a forced win in $7$ plies ($+99{,}993$), so the AI never toys with a defeated opponent or repeats moves when a direct win exists.
2. **Maximum Resistance When Behind:** Facing a forced loss, the AI prefers $-99{,}990$ (loss in $10$ plies) over $-99{,}996$ (loss in $4$ plies), prolonging the game and maximizing the chance of a human mistake.
3. **Early Iterative Deepening Termination:** If the root search finds a forced win ($\text{bestScoreOverall} \ge 90{,}000$), iterative deepening stops immediately and plays the winning sequence.

---

## Iterative Deepening & Asynchronous Execution

Rather than jumping straight to target depth $D$, `MinimaxPlayer.GetMoveAsync` searches progressively through depths $d = 1, 2, 3, \dots, D$:
1. **Anytime Move Availability:** If the move timer expires mid-search during depth $d$, the engine safely falls back to `bestMoveOverall` from completed depth $d - 1$.
2. **PV & TT Seeding:** Each completed depth $d - 1$ populates the Transposition Table and promotes the best root move to index `0`, making depth $d$ dramatically faster ([Chapter 06](06-move-ordering.md)).
3. **Non-Blocking UI & Cancellation:**
   - **WPF Desktop:** Runs on a background ThreadPool thread via `Task.Run`.
   - **Blazor WebAssembly:** Yields cooperatively to the browser event loop via `await Task.Delay(1, cancellationToken)` after each completed depth and on $\ge 150\text{ ms}$ heartbeats ([Chapter 11](11-app-integration.md)).
   - **Instant Cancellation:** Every $1{,}024$ nodes (`(NodesEvaluated & 1023) == 0`), `cancellationToken.ThrowIfCancellationRequested()` and `sw.ElapsedMilliseconds >= hardLimitMs` are checked.

---

## Live Search Analysis Telemetry

While thinking, `MinimaxPlayer` streams real-time `SearchAnalysis` snapshots through `IProgress<SearchAnalysis>` to the UI Analysis pane:

| Field | Example | Meaning |
|---|---|---|
| **Move** | `23-19` | Candidate root move currently being explored (or completed best move) |
| **Depth** | `8 plies` / `9 plies...` | Highest completed depth (or in-progress depth with ellipsis during heartbeats) |
| **Value** | `+35` / `+Win in 5 plies` | Heuristic score in centipawns, `Forced`, or exact distance-to-win/loss |
| **Best move** | `24-20` | Principal Variation root move from the latest finished iteration |
| **Nodes** | `1,482,910` | Total interior and quiescence nodes visited (`NodesEvaluated`) |
| **Evaluations** | `612,044` | Total static leaf evaluations executed (`LeafEvaluations`) |
| **Time** | `0:01.4` | Elapsed wall-clock time formatted as `m:ss.f` |
