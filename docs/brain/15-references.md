# 15 – References

[Back to the index](README.md)

Official rulesets, foundational artificial intelligence research, bitboard engineering literature, and platform documentation referenced in the design and implementation of the Checkers engine:

---

## 1. Official Game Rulesets & Notation

* **World Checkers/Draughts Federation (WCDF):**
  * *Official Rules of Checkers (English Draughts / American Checkers).*
  * Specifications for the $8 \times 8$ dark-square board, $1\text{–}32$ standard numbering, 12 men per side, 1-step King movement, 4-direction single-hop capture chains, mandatory captures, and turn-ending crown-row promotion ([Chapters 02](02-board-and-coordinates.md), [03](03-rules-and-move-generation.md)).
* **Fédération Mondiale du Jeu de Dames (FMJD):**
  * *Official Rules of International Draughts & Portable Draughts Notation (PDN) 3.0 Standard.*
  * Specifications for Flying Kings (multi-square diagonal sliding, long-distance jump captures, and mandatory continuation along capturing rays) and PDN tag-pair / move serialization ([Chapters 03](03-rules-and-move-generation.md), [04](04-game-record.md)).

---

## 2. Artificial Intelligence & Game-Tree Search

* **Shannon, Claude E. (1950).** *"Programming a Computer for Playing Chess."* Philosophical Magazine, Series 7, Vol. 41, No. 314, pp. 256–275.
  * Foundational formulation of Minimax search, static evaluation functions, and tactical instability at fixed depth horizons ([Chapters 05](05-evaluation.md), [07](07-search.md)).
* **Samuel, Arthur L. (1959).** *"Some Studies in Machine Learning Using the Game of Checkers."* IBM Journal of Research and Development, 3(3), pp. 210–229.
  * Pioneering work on computer Checkers heuristics, piece advancement, center control, back-rank defense, and alpha-beta tree pruning ([Chapter 05](05-evaluation.md)).
* **Knuth, Donald E., & Moore, Ronald W. (1975).** *"An Analysis of Alpha-Beta Pruning."* Artificial Intelligence, 6(4), pp. 293–326.
  * Formal analysis of Negamax Alpha-Beta pruning and proof that optimal move ordering reduces tree complexity from $O(b^d)$ to $O(b^{\lceil d/2 \rceil} + b^{\lfloor d/2 \rfloor} - 1)$ ([Chapters 06](06-move-ordering.md), [07](07-search.md)).
* **Zobrist, Albert L. (1970).** *"A New Hashing Method with Applications for Game Playing."* Technical Report #88, Computer Sciences Department, University of Wisconsin, Madison.
  * Incremental 64-bit XOR hashing of board states for repetition detection and hash table indexing ([Chapters 02](02-board-and-coordinates.md), [08](08-transposition-table.md)).
* **Slate, David J., & Atkin, Lawrence R. (1977).** *"CHESS 4.5 — The Northwestern University Chess Program."* In *Chess Skill in Man and Machine*, Springer, pp. 82–118.
  * Introduction of iterative deepening, killer move slots, transposition tables with bound flags (`Exact`, `LowerBound`, `UpperBound`), and soft/hard time controls ([Chapters 06](06-move-ordering.md), [07](07-search.md), [08](08-transposition-table.md), [09](09-time-control.md)).
* **Marsland, T. Anthony (1983) & Reinefeld, Alexander (1983).** *"Relative Efficiency of Alpha-Beta Implementations"* (IJCAI-83) & *"An Improvement to the Scout Tree-Search Algorithm"* (ICCA Journal, 6(4)).
  * Principal Variation Search (PVS / NegaScout) using zero-width null-window probes $[-\alpha-1, -\alpha]$ to verify move inferiority before full-window re-search ([Chapters 07](07-search.md), [12](12-engine-improvements.md)).
* **Schaeffer, Jonathan (1989).** *"The History Heuristic and Alpha-Beta Search Enhancements in Practice."* IEEE Transactions on Pattern Analysis and Machine Intelligence, 11(11), pp. 1203–1212.
  * History heuristic tables indexed by `[from][to]` for ordering quiet moves across the game tree ([Chapter 06](06-move-ordering.md)).
* **Heinz, Ernst A. (1998).** *"Extended Futility Pruning."* ICGA Journal, 21(2), pp. 75–83.
  * Frontier and pre-frontier futility pruning and static null-move (reverse futility) pruning margins ([Chapter 07](07-search.md)).
* **Romstad, Tord, Costalba, Marco, & Kiiski, Joona (2004–present).** *Glaurung & Stockfish Open-Source Engines.*
  * Late Move Reductions (LMR) with two-stage null-window verification and multi-way set-associative cache-line transposition buckets ([Chapters 07](07-search.md), [08](08-transposition-table.md), [12](12-engine-improvements.md)).
* **Schaeffer, Jonathan, et al. (1996, 2007).** *"Chinook: The World Man-Machine Checkers Champion"* (AI Magazine, 17(1)) & *"Checkers Is Solved"* (Science, 317(5844), pp. 1518–1522).
  * Architecture of the World Champion Checkers program and computational proof ($5 \times 10^{20}$ state space) that perfect play in $8 \times 8$ English Checkers results in a draw.
* **Lincke, Thomas R. (2000, 2001).** *"Strategies for the Automatic Construction of Opening Books"* (Computers and Games — CG 2000, Springer LNCS, Vol. 2063, pp. 74–86) & *"Exploring the Computational Limits of Large Exhaustive Search Problems"* (ETH Zürich Ph.D. Dissertation #14099).
  * Priority-driven Drop-Out Expansion (DOE) algorithm (`W_player * delta_player + W_opponent * delta_opponent + depth`) and transposition-aware Directed Acyclic Graph (DAG) Negamax back-up for autonomous opening book construction ([Chapter 10](10-opening-book.md)).
* **Fierz, Martin (2000–2021).** *Cake Checkers Engine & CheckerBoard Interface.*
  * Classical $8 \times 8$ Checkers static evaluation heuristics, runaway checker promotion cones, king tail pins, structural back-rank patterns (`Bridge`, `Oreo`, `Triangle`, `Dog`, `Right Lock`), and parameterized evaluation tables ([Chapters 05](05-evaluation.md), [12](12-engine-improvements.md)).
* **Letouzey, Fabien (2015–2020).** *Scan 3.1 Open-Source International Draughts Engine.*
  * Bitboard move generation and Flying King evaluation experiments ([Chapters 05](05-evaluation.md), [12](12-engine-improvements.md)).

---

## 3. Bitboards & Hardware Bit Manipulation

* **Knuth, Donald E. (2009).** *The Art of Computer Programming, Volume 4A: Combinatorial Algorithms, Part 1 (Section 7.1.3: Bitwise Tricks and Techniques).* Addison-Wesley.
  * Broadword parallel shift-and-mask operations, population count (`POPCNT`), and least/most significant bit isolation (`x &= x - 1`, `TZCNT`, `LZCNT`) ([Chapter 11](11-bitboards.md)).
* **Hyatt, Robert M. (1999).** *"Rotated Bitboards, a New Twist on an Old Idea."* Journal of the International Computer Chess Association (ICCA), 22(4), pp. 213–222.
  * Classical directional ray-attack bitboard techniques for sliding pieces using leading and trailing zero counts ([Chapter 11](11-bitboards.md)).

---

## 4. .NET 10, WPF & Blazor WebAssembly Architecture

* **Microsoft .NET 10 Runtime, `System.Numerics.BitOperations` & `System.Runtime.Intrinsics.X86.Sse`:**
  * Hardware intrinsics (`PopCount`, `TrailingZeroCount`, `LeadingZeroCount`, `Sse.Prefetch0`), Pinned Object Heap (`GC.AllocateArray`), `Span<T>` zero-allocation memory slices, and Blazor WebAssembly Ahead-Of-Time (AOT) compilation ([Chapters 08](08-transposition-table.md), [11](11-bitboards.md), [13](13-app-integration.md)).
* **CommunityToolkit.Mvvm:**
  * Source-generated MVVM observables and relay commands in `Checkers.App` ([Chapter 13](13-app-integration.md)).
* **Markdig, KaTeX & Mermaid.js:**
  * Browser-hosted Markdown, LaTeX mathematics, and diagram rendering pipeline in `Checkers.Web` ([Chapter 13](13-app-integration.md)).
