# 14 – References

[Back to the index](README.md)

Literature, official rulesets, and algorithmic research referenced in the design and implementation of the Checkers engine:

---

## 1. Official Game Rulesets

* **World Checkers/Draughts Federation (WCDF):**
  * Official Rules of English Checkers (American Draughts / Straight Checkers).
  * Specifications for standard $8 \times 8$ board, 12 pieces per player, 1-step King movement, single-hop 4-direction jumping, mandatory captures with free choice, and turn-ending crown-row promotion.
* **Fédération Mondiale du Jeu de Dames (FMJD):**
  * Official Rules of International Draughts.
  * Specifications for Flying Kings (multi-square diagonal sliding and long-distance jump landing choices).

---

## 2. Artificial Intelligence & Game Search Algorithms

* **Alpha-Beta Pruning & Minimax:**
  * **Knuth, Donald E., & Moore, Ronald W. (1975).** *"An Analysis of Alpha-Beta Pruning."* Artificial Intelligence, 6(4), 293–326.
    * Mathematical proof of optimal move ordering achieving $O(b^{d/2})$ search complexity.
  * **Shannon, Claude E. (1950).** *"Programming a Computer for Playing Chess."* Philosophical Magazine, Ser.7, Vol. 41, No. 314.
    * Minimax formulation and static heuristic evaluation functions.

* **Zobrist Hashing & Transposition Tables:**
  * **Zobrist, Albert L. (1970).** *"A New Hashing Method with Applications for Game Playing."* Technical Report #88, Computer Sciences Department, University of Wisconsin, Madison.
    * Incremental pseudo-random 64-bit XOR keys for game board states.
  * **Slate, David J., & Atkin, Lawrence R. (1977).** *"CHESS 4.5 — The Northwestern University Chess Program."* In Chess Skill in Man and Machine, Springer, 82–118.
    * Practical implementation of power-of-two transposition tables, bound flags, and iterative deepening time controls.

* **Checkers Search & Historical Landmarks:**
  * **Schaeffer, Jonathan, et al. (1996).** *"Chinook: The World Man-Machine Checkers Champion."* AI Magazine, 17(1), 21–29.
    * The architecture of the first computer program to win the human World Checkers Championship.
  * **Schaeffer, Jonathan, et al. (2007).** *"Checkers Is Solved."* Science, 317(5844), 1518–1522.
    * The computational proof that perfect play by both sides in 8x8 English Checkers leads to a draw.

---

## 3. Technology & Architecture References

* **Microsoft .NET 10 Documentation:**
  * Blazor WebAssembly Ahead-Of-Time (AOT) compilation and cooperative threading model.
  * WPF desktop graphics pipeline and `System.Windows.Threading.Dispatcher`.
* **CommunityToolkit.Mvvm:**
  * Source generator MVVM patterns (`[ObservableProperty]`, `[RelayCommand]`).
* **Markdig:**
  * Markdown parsing and HTML rendering pipeline for browser-hosted documentation.
