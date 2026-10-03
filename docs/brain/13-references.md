# 13 – References

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
  * Introduction of iterative deepening, transposition tables with bound flags (`Exact`, `LowerBound`, `UpperBound`), and soft/hard time controls ([Chapters 07](07-search.md), [08](08-transposition-table.md), [09](09-time-control.md)).
* **Schaeffer, Jonathan, et al. (1996).** *"Chinook: The World Man-Machine Checkers Champion."* AI Magazine, 17(1), pp. 21–29.
  * Architecture of the first computer program to win a human World Championship in any mind sport.
* **Schaeffer, Jonathan, et al. (2007).** *"Checkers Is Solved."* Science, 317(5844), pp. 1518–1522.
  * Computational proof ($5 \times 10^{20}$ state space) that perfect play by both sides in $8 \times 8$ English Checkers results in a draw.

---

## 3. Bitboards & Hardware Bit Manipulation

* **Knuth, Donald E. (2009).** *The Art of Computer Programming, Volume 4A: Combinatorial Algorithms, Part 1 (Section 7.1.3: Bitwise Tricks and Techniques).* Addison-Wesley.
  * Broadword parallel shift-and-mask operations, population count (`POPCNT`), and least/most significant bit isolation (`x &= x - 1`, `TZCNT`, `LZCNT`) ([Chapter 10](10-bitboards.md)).
* **Hyatt, Robert M. (1999).** *"Rotated Bitboards, a New Twist on an Old Idea."* Journal of the International Computer Chess Association (ICCA), 22(4), pp. 213–222.
  * Classical directional ray-attack bitboard techniques for sliding pieces using leading and trailing zero counts ([Chapter 10](10-bitboards.md)).

---

## 4. .NET 10, WPF & Blazor WebAssembly Architecture

* **Microsoft .NET 10 Runtime & `System.Numerics.BitOperations`:**
  * Hardware intrinsics (`PopCount`, `TrailingZeroCount`, `LeadingZeroCount`), `Span<T>` zero-allocation memory slices, and Blazor WebAssembly Ahead-Of-Time (AOT) compilation ([Chapters 10](10-bitboards.md), [11](11-app-integration.md)).
* **CommunityToolkit.Mvvm:**
  * Source-generated MVVM observables and relay commands in `Checkers.App` ([Chapter 11](11-app-integration.md)).
* **Markdig, KaTeX & Mermaid.js:**
  * Browser-hosted Markdown, LaTeX mathematics, and diagram rendering pipeline in `Checkers.Web` ([Chapter 11](11-app-integration.md)).
