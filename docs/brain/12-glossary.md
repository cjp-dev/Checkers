# 12 – Glossary

[Back to the index](README.md)

Common Checkers, Draughts, Bitboard, and AI search terms used across these documents:

| Term | Meaning | Chapter |
|---|---|---|
| **Alpha ($\alpha$)** | The minimum score the maximizing side to move is guaranteed to achieve; moves scoring $\le \alpha$ fail low. | [07](07-search.md#negamax-alpha-beta-algorithm) |
| **Beta ($\beta$)** | The maximum score the opponent will allow the active player to achieve; a move reaching $\ge \beta$ triggers an immediate $\beta$-cutoff. | [07](07-search.md#negamax-alpha-beta-algorithm) |
| **Bitboard** | A 64-bit unsigned integer (`ulong`) where each bit `sq = Row * 8 + Col` (`0..63`) represents whether a specific square contains a piece or property. | [02](02-board-and-coordinates.md), [10](10-bitboards.md) |
| **BitMove** | Compact 16-byte value struct (`ulong Captured`, `byte From`, `byte To`, `bool IsPromotion`) representing a move with zero heap allocation. | [04](04-game-record.md), [10](10-bitboards.md) |
| **BitPosition** | Compact 48-byte value struct storing the entire board in four 64-bit bitboards (`WhiteMen`, `BlackMen`, `WhiteKings`, `BlackKings`), Zobrist `Hash`, `HalfMoveClock`, and `SideToMove`. | [02](02-board-and-coordinates.md), [10](10-bitboards.md) |
| **Bound** | A flag (`Exact`, `LowerBound`, `UpperBound`) stored in a Transposition Table entry indicating whether the cached score is an exact minimax value, a $\beta$-cutoff lower bound, or an $\alpha$ fail-low upper bound. | [08](08-transposition-table.md#alpha-beta-search-bounds) |
| **Centipawn** | Unit of heuristic evaluation where $100\text{ points} = 1\text{ regular man}$. | [05](05-evaluation.md) |
| **Checkers Variant** | The active ruleset (`CheckersVariant.International` for Flying Kings or `CheckersVariant.English` for 1-Step Kings). | [01](01-overview.md#where-the-rules-come-from), [03](03-rules-and-move-generation.md) |
| **Continuation Rule** | International Flying King rule requiring a jumping king to choose a landing square that continues a multi-jump sequence whenever one exists. | [03](03-rules-and-move-generation.md#the-mandatory-continuation-requirement) |
| **Copy-Make** | Search state transition technique that copies a 48-byte value-type `BitPosition` on the CPU stack and applies a `BitMove` (`pos.Apply(in move)`) without mutating parent state or allocating heap memory. | [10](10-bitboards.md#value-type-copy-make-search-minimaxplayercs) |
| **Cutoff** | Pruning the remaining sibling moves at a search node because one move has already achieved $\text{score} \ge \beta$. | [07](07-search.md#negamax-alpha-beta-algorithm) |
| **Dark Square** | One of the 32 playable squares on the $8 \times 8$ board satisfying $(\text{Row} + \text{Col}) \bmod 2 = 1$ (`DarkSquares = 0x55AA55AA55AA55AAUL`). | [02](02-board-and-coordinates.md#coordinate-system-and-dark-square-parity) |
| **Distance-to-Mate** | Terminal score adjustment ($\pm 100{,}000 \mp \text{ply}$) that rewards the fastest forced win and delays unavoidable defeat. | [07](07-search.md#distance-to-mate-scoring), [08](08-transposition-table.md#score-normalization-win-distance-invariance) |
| **Draughts Notation** | Standard numbering of the 32 playable dark squares from $1$ to $32$, reading left-to-right, top-to-bottom from Black's home rank. | [02](02-board-and-coordinates.md#standard-draughts-132-numbering), [04](04-game-record.md) |
| **English Checkers** | American Draughts / Straight Checkers variant on an $8 \times 8$ board where crowned Kings move and jump strictly 1 step in all 4 diagonal directions. | [03](03-rules-and-move-generation.md#english-checkers-kings-movement-checkersvariantenglish) |
| **Evaluation** | Static heuristic function (`EvaluationFunction.Evaluate`) scoring material, advancement, center control, king centralization, and back-rank defense. | [05](05-evaluation.md) |
| **Flying King** | International Draughts king capable of sliding across any number of empty diagonal squares and jumping distant enemy pieces along open diagonals. | [03](03-rules-and-move-generation.md#flying-kings-movement-checkersvariantinternational), [10](10-bitboards.md) |
| **Forty-Move Rule** | Draw condition triggered when $80$ consecutive half-moves (`HalfMoveClock >= 80`) elapse without a capture or crown-row promotion. | [04](04-game-record.md#3-forty-move-rule-draw) |
| **Free Choice** | Rule permitting the active player to choose any valid capture branch when multiple captures exist, provided the chosen jump chain is completed. | [03](03-rules-and-move-generation.md#mandatory-captures-and-free-choice) |
| **Hard Limit ($T_{\text{hard}}$)** | Absolute move time ceiling checked every $1{,}024$ nodes; aborts an incomplete depth iteration and falls back to the previous completed depth. | [09](09-time-control.md#soft-and-hard-time-budget-equations) |
| **Hash Move** | The proven best move (`BestMoveFrom`, `BestMoveTo`) retrieved from a Transposition Table hit and searched first at index `0`. | [06](06-move-ordering.md), [08](08-transposition-table.md) |
| **Horizon Effect** | Tactical miscalculation caused by stopping search at `depth == 0` in the middle of an unfinished capture sequence; solved by Quiescence Search. | [07](07-search.md#quiescence-search--the-horizon-effect) |
| **Iterative Deepening** | Searching progressively from depth $d = 1$ up to target depth $D$, using each completed iteration to seed the PV and Transposition Table for depth $d + 1$. | [07](07-search.md#iterative-deepening--asynchronous-execution), [09](09-time-control.md) |
| **Mandatory Capture** | Fundamental Draughts rule: whenever at least one legal capture exists, quiet non-capturing moves are strictly illegal. | [03](03-rules-and-move-generation.md#mandatory-captures-and-free-choice) |
| **Multi-Jump** | A single turn in which a piece jumps two or more enemy pieces consecutively (`29x18x4`). | [03](03-rules-and-move-generation.md#3-recursive-multi-jump-chains) |
| **Negamax** | Symmetric formulation of Minimax using $\max(a, b) = -\min(-a, -b)$, where every node maximizes the score from `SideToMove`'s perspective. | [05](05-evaluation.md#negamax-sign-convention), [07](07-search.md#negamax-alpha-beta-algorithm) |
| **PDN** | Portable Draughts Notation: standard plain-text file format for recording Checkers games with `[Key "Value"]` metadata headers and numbered moves. | [04](04-game-record.md#game-record-persistence--portable-draughts-notation-pdn) |
| **Ply** | One half-move (a single turn played by either White or Black). | [07](07-search.md) |
| **PopCount (`POPCNT`)** | Hardware CPU instruction (`BitOperations.PopCount`) that counts the number of `1` bits in a 64-bit integer in a single clock cycle. | [05](05-evaluation.md), [10](10-bitboards.md) |
| **Principal Variation (PV)** | The sequence of best moves for both sides discovered by the search; its root move is searched first at the next iterative deepening depth. | [06](06-move-ordering.md#principal-variation-pv-ordering-at-the-root) |
| **Quiescence Search** | Tactical search extension at `depth == 0` that expands only capture moves until a quiet position is reached. | [07](07-search.md#quiescence-search--the-horizon-effect) |
| **Soft Limit ($T_{\text{soft}}$)** | Time threshold ($\frac{2}{3}\,T_{\text{hard}}$) checked between depth iterations; prevents starting a new depth when less than $\frac{1}{3}$ of the budget remains. | [09](09-time-control.md#soft-and-hard-time-budget-equations) |
| **Threefold Repetition** | Draw condition triggered when the exact same 64-bit Zobrist hash (piece configuration + side to move) occurs 3 times in a match. | [04](04-game-record.md#4-threefold-repetition-draw) |
| **Transposition Table** | Power-of-two array of 16-byte `TranspositionEntry` structs indexed by `ZobristHash & mask`, caching evaluated subtrees across branches and turns. | [08](08-transposition-table.md) |
| **TZCNT / LZCNT** | Hardware bit-scan instructions (`BitOperations.TrailingZeroCount` and `LeadingZeroCount`) used to locate set bits and the first blocker along Flying King diagonal rays in $O(1)$ time. | [10](10-bitboards.md#3-international-flying-kings-trailingzerocount--leadingzerocount) |
| **Zobrist Hashing** | Incremental 64-bit XOR hashing scheme mapping board states to pseudo-random 64-bit keys (`ulong`). | [02](02-board-and-coordinates.md#64-bit-zobrist-hashing), [08](08-transposition-table.md) |
