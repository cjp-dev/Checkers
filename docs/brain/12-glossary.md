# 13 – Glossary

[Back to the index](README.md)

Common Checkers and engine terms used across these documents:

| Term | Meaning | Chapter |
|---|---|---|
| **Alpha ($\alpha$)** | The minimum score the maximizing player is guaranteed to achieve; branches failing below $\alpha$ cannot affect the outcome. | [07](07-search.md#negamax-alpha-beta-algorithm) |
| **Beta ($\beta$)** | The maximum score the opponent will permit the maximizing player to achieve; a move reaching $\beta$ causes a cutoff. | [07](07-search.md#negamax-alpha-beta-algorithm) |
| **Bound** | A search score stored in the Transposition Table that represents an exact value, lower bound ($\beta$-cutoff), or upper bound ($\alpha$-fail-low). | [08](08-transposition-table.md#search-bounds) |
| **Centipawn** | Unit of heuristic evaluation score where 100 points $\approx$ 1 regular man. | [05](05-evaluation.md) |
| **Checkers Variant** | The set of movement rules governing kings and men: `International` (Flying Kings) or `English` (1-step Kings). | [01](01-overview.md#where-the-rules-come-from), [03](03-rules-and-move-generation.md) |
| **Cutoff** | Halting search exploration along a tree branch because one move has already proven to meet or exceed $\beta$. | [07](07-search.md#negamax-alpha-beta-algorithm) |
| **Dark Square** | One of the 32 playable squares on the $8 \times 8$ board where $(\text{Row} + \text{Col}) \bmod 2 \neq 0$. | [02](02-board-and-coordinates.md#coordinate-system-and-dark-squares) |
| **Distance-to-Mate** | Scoring bonus/penalty ($100,000 - \text{ply}$) encouraging the AI to choose the fastest win or delay loss. | [07](07-search.md#distance-to-mate-scoring) |
| **Draughts Notation** | Official numbering of the 32 playable dark squares from 1 to 32, reading left-to-right, top-to-bottom. | [02](02-board-and-coordinates.md#standard-draughts-132-numbering) |
| **English Checkers** | American Draughts variant where Kings move strictly 1 square in all 4 diagonal directions and men only jump forward. | [03](03-rules-and-move-generation.md#english-checkers-kings-movement) |
| **Evaluation** | Static heuristic function calculating the positional and material worth of a board state from the active player's perspective. | [05](05-evaluation.md) |
| **Flying King** | International Draughts king able to slide across any number of open diagonal squares and capture over open diagonal spans. | [03](03-rules-and-move-generation.md#flying-kings-movement-international-variant) |
| **Forty-Move Rule** | Draw condition triggered when 80 consecutive half-moves elapse without any capture or promotion. | [04](04-game-record.md#3-forty-move-rule-draw) |
| **Free Choice** | Rule allowing a player to choose freely among any available capture branches, as long as the chosen jump sequence is finished. | [03](03-rules-and-move-generation.md#mandatory-captures-and-free-choice) |
| **Hard Limit** | Absolute time budget ceiling that halts an in-progress search iteration, returning the move from the previous completed depth. | [10](10-time-control.md#soft-and-hard-time-limits) |
| **Hash Move** | The best move retrieved from the Transposition Table for a position, tried first in move ordering. | [06](06-move-ordering.md#1-hash-move-from-transposition-table) |
| **Iterative Deepening** | Searching progressively from depth 1 up to the target depth; each completed iteration seeds move ordering for the next. | [07](07-search.md#negamax-alpha-beta-algorithm), [10](10-time-control.md#iterative-deepening-time-lifecycle) |
| **Mandatory Capture** | Compulsory jumping rule: if one or more captures exist anywhere on the board, non-capturing quiet moves are strictly illegal. | [03](03-rules-and-move-generation.md#mandatory-captures-and-free-choice) |
| **Multi-Jump** | A single turn in which a piece executes two or more jumps consecutively; recorded as a multi-square path (`29x18x4`). | [03](03-rules-and-move-generation.md#multi-jump-chains) |
| **Negamax** | Symmetric minimax variant where $\max(A, B) = -\min(-A, -B)$, evaluating every node from the viewpoint of the side to move. | [05](05-evaluation.md#negamax-formulation), [07](07-search.md#negamax-alpha-beta-algorithm) |
| **PDN** | Portable Draughts Notation: standard plain-text format for recording checkers games with metadata tag headers. | [04](04-game-record.md#game-record-persistence--portable-draughts-notation-pdn) |
| **Ply** | One half-move (a single turn played by White or Black). | [07](07-search.md#distance-to-mate-scoring) |
| **Principal Variation (PV)** | The predicted sequence of optimal moves for both sides discovered by the search. | [06](06-move-ordering.md#6-principal-variation-pv-ordering-at-root) |
| **Quiescence Search** | Tactical search extension at leaf nodes that evaluates only capture moves to eliminate the horizon effect. | [08](08-transposition-table.md) |
| **Soft Limit** | Time budget threshold ($\frac{2}{3}$ of allotted move time) after which no new depth iteration will be initiated. | [10](10-time-control.md#soft-and-hard-time-limits) |
| **Threefold Repetition** | Draw condition triggered when the exact same board configuration (pieces and turn) occurs 3 times in a game. | [04](04-game-record.md#4-threefold-repetition-draw) |
| **Transposition Table** | 64-bit Zobrist hash table caching position evaluations, search depths, bound flags, and best moves. | [08](08-transposition-table.md) |
| **Zobrist Hashing** | Fast, incremental 64-bit pseudo-random XOR hashing technique mapping board states to 64-bit keys. | [02](02-board-and-coordinates.md#zobrist-hashing), [08](08-transposition-table.md) |
