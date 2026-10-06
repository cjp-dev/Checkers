**Drop-Out Expansion (DOE)** is an offline game-tree exploration algorithm designed to automatically generate high-quality opening books for two-player, zero-sum, perfect-information board games. Introduced by **Thomas R. Lincke** in his 2002 ETH Zurich doctoral dissertation (*Exploration of Large Game Trees*), the algorithm balances two competing requirements in opening book generation:

1. **Depth:** Searching deep enough down the most critical, high-quality variations (avoiding shallow blunders).
2. **Breadth:** Maintaining sufficient coverage of viable alternative moves so the book remains robust against opponent deviations, while avoiding the "depth trap" (tunnel-visioning down a single corridor) and the "breadth trap" (wasting compute exploring refuted moves).

---

## How Drop-Out Expansion Works

In classic game-tree search, standard best-first search expands the highest-scoring leaf, but small evaluation fluctuations can cause it to oscillate erratically between distant subtrees. Conversely, fixed-depth minimax wastes enormous effort evaluating lines where one player already has an overwhelming advantage.

DOE solves this by traversing the tree from the root to the frontier while dynamically filtering out suboptimal moves using a tolerance margin.

### 1. Tree Representation

The opening book is stored as an in-memory game tree (or directed acyclic graph). Each node $p$ stores:

* Its minimax value $V(p)$.
* Pointers to child nodes (legal moves).
* The expansion state (expanded vs. unexpanded leaf).
* Optional search metadata (e.g., node visits, search depth, or evaluation bounds).

### 2. The Drop-Out Criterion

When descending from the root to find the next leaf to expand, the algorithm looks at all legal children $c \in \text{Children}(p)$.

Let $c^*$ be the current best child according to the minimax value:


$$V(c^*) = \max_{c \in \text{Children}(p)} V(c) \quad \text{(for a MAX node)}$$

A candidate child $c$ **drops out** (is disqualified from further exploration) if its score falls below the best move by more than a drop-out threshold $\delta$:


$$V(c^*) - V(c) > \delta$$

* **If $V(c^*) - V(c) \le \delta$:** The move is considered *viable* and remains in the active candidate pool.
* **If $V(c^*) - V(c) > \delta$:** The move has dropped out. No search resources are wasted exploring subtrees beneath it unless later backups bring its value closer to $c^*$.

The threshold $\delta$ can be static (e.g., 50 centipawns in chess or 2–4 discs in Othello) or dynamic (narrowing as depth increases to focus deeply on primary lines).

### 3. Candidate Selection

Among the children that have **not** dropped out, DOE selects which branch to descend. Rather than always picking $c^*$ (which would cause tunnel vision), DOE typically chooses the candidate with the **least accumulated search effort** (e.g., lowest visit count or lowest evaluation depth). This forces balanced exploration across all competitive moves.

### 4. Leaf Expansion & Backpropagation

1. **Descent:** The process recurses until an unexpanded leaf node is reached.
2. **Evaluation:** An external engine (e.g., an alpha-beta search) evaluates the leaf to a modest fixed depth (e.g., 8–14 plies).
3. **Expansion:** The leaf's legal child moves are generated and added to the tree as unexpanded frontier nodes.
4. **Backup:** The new score updates the parent's minimax value, and the update propagates all the way back up to the root, updating which moves qualify or drop out along the path.

---

## Reference Implementation

Because full production game engines bundle DOE with game-specific bitboards and board representations, the self-contained Python implementation below illustrates the exact control flow, drop-out filtering, least-effort selection, and minimax backup logic:

```python
from dataclasses import dataclass, field
from typing import List, Optional

@dataclass
class BookNode:
    state: str                          # Board representation (FEN, string, etc.)
    is_max: bool                        # True if root player's turn, False for opponent
    children: List['BookNode'] = field(default_factory=list)
    value: float = 0.0                  # Backed-up minimax score
    visits: int = 0                     # Effort tracker
    is_expanded: bool = False

def evaluate_leaf(state: str) -> float:
    """Mock external engine evaluation (e.g., alpha-beta search to fixed depth)."""
    # Replace with engine.search(state, depth=10)
    import hashlib
    h = int(hashlib.md5(state.encode()).hexdigest(), 16)
    return ((h % 2000) - 1000) / 100.0  # Returns score between -10.0 and +10.0

def get_legal_moves(state: str) -> List[str]:
    """Mock legal move generator."""
    return [f"{state}_{m}" for m in ["a", "b", "c"]]

class DropOutExpansionBook:
    def __init__(self, root_state: str, delta: float = 1.0):
        self.root = BookNode(state=root_state, is_max=True)
        self.delta = delta  # Drop-out tolerance window

    def select_active_child(self, node: BookNode) -> Optional[BookNode]:
        """Filters children by drop-out threshold, then picks the least-explored."""
        if not node.children:
            return None

        # 1. Identify current best value among children
        if node.is_max:
            best_val = max(c.value for c in node.children)
            active = [c for c in node.children if (best_val - c.value) <= self.delta]
        else:
            best_val = min(c.value for c in node.children)
            active = [c for c in node.children if (c.value - best_val) <= self.delta]

        # 2. Pick active child with lowest visit count (least effort explored)
        return min(active, key=lambda c: c.visits)

    def step(self):
        """Executes one iteration: descent, leaf expansion, and backpropagation."""
        path = [self.root]
        curr = self.root

        # Descent phase
        while curr.is_expanded and curr.children:
            next_node = self.select_active_child(curr)
            if not next_node:
                break
            curr = next_node
            path.append(curr)

        # Expansion phase
        if not curr.is_expanded:
            curr.value = evaluate_leaf(curr.state)
            next_states = get_legal_moves(curr.state)
            curr.children = [
                BookNode(state=s, is_max=not curr.is_max, value=curr.value)
                for s in next_states
            ]
            curr.is_expanded = True

        # Backpropagation phase (Minimax + visit updates)
        for node in reversed(path):
            node.visits += 1
            if node.children:
                if node.is_max:
                    node.value = max(c.value for c in node.children)
                else:
                    node.value = min(c.value for c in node.children)

    def generate(self, iterations: int = 1000):
        for _ in range(iterations):
            self.step()

```

---

## Locating Existing Implementations & Source Code

While DOE is less ubiquitous in mainstream chess (which often favors retrograde tablebases or bulk self-play PGN aggregation), it is prominent in academic combinatorial game solving and specialized board engines:

### 1. Thomas Lincke's Original Work (*Proteus* / ETH Zurich)

* **Context:** Lincke created the Othello/Reversi program *Proteus*, which used DOE to generate large opening books that rivaled Michael Buro's *Logistello*.
* **Reference:** Lincke's doctoral thesis, *Exploration of Large Game Trees* (ETH Zurich Dissertation No. 14553, 2002). Chapter 4 contains the formal algorithm specifications, convergence theorems, and pseudocode. It is openly indexed on the [ETH Zurich Research Collection](https://www.google.com/search?q=https://www.research-collection.ethz.ch/).

### 2. University of Alberta Games Group (*Benzene* / *Wolve* / *MoHex*)

* **Repository / Source:** The University of Alberta's Hex research suite (`Benzene` framework, hosted on GitHub under `ualberta-benzene` or via the Alberta Computer Hex Project).
* **Usage:** Researchers Philip Henderson, Broderick Arneson, and Ryan Hayward adapted Drop-Out Expansion to build opening books for 8×8 and 9×9 Hex. Their opening book builder implements DOE variants to navigate the wide branching factor of Hex boards without exploring demonstrably dead cells.

### 3. Open-Source Reversi Engines (e.g., *Edax*)

* **Repository:** [Edax-reversi on GitHub](https://github.com/abulmo/edax-reversi) by Richard Delorme.
* **Relevant Files:** `src/book.c` and `src/book.h`.
* **Details:** While Edax implements its own hybrid book builder with alpha-beta and iterative deepening updates, its pruning thresholds and selective book widening directly adapt Lincke’s drop-out principles to cut unpromising lines.

### 4. Connect Four & Checkers Engines

* Implementations of selective book generation in game engines (such as variants of John Tromp's *Fhourstones* Connect Four solver or checkers opening book builders) often reference Lincke's method when constructing depth-bounded opening trees from fixed-depth searchers.