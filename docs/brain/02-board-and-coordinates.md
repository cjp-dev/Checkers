# 02 – Board and coordinates

[Back to the index](README.md)

## In short

In Checkers, pieces only move and capture on the **dark squares** of an 8x8 board. That means exactly 32 of the 64 squares are active, while the other 32 light squares remain empty throughout the entire game.

This chapter explains how coordinates are represented internally, how they map bidirectionally to standard Draughts 1–32 square numbers, and how deterministic 64-bit Zobrist hashing identifies unique board states.

---

## Coordinate system and dark squares

Internally, every square is addressed by a zero-indexed `Position(Row, Col)`:
* `Row` ranges from `0` to `7` (`0` is Black's home rank; `7` is White's home rank).
* `Col` ranges from `0` to `7` (`0` is file A; `7` is file H).

A square is an active dark square if and only if the sum of its row and column is odd:

$$(\text{Row} + \text{Col}) \pmod 2 \neq 0$$

```
   Col:  0   1   2   3   4   5   6   7
Row 0:  [ ] [•] [ ] [•] [ ] [•] [ ] [•]   <- Black Home Row
Row 1:  [•] [ ] [•] [ ] [•] [ ] [•] [ ]
Row 2:  [ ] [•] [ ] [•] [ ] [•] [ ] [•]
Row 3:  [•] [ ] [•] [ ] [•] [ ] [•] [ ]
Row 4:  [ ] [•] [ ] [•] [ ] [•] [ ] [•]
Row 5:  [•] [ ] [•] [ ] [•] [ ] [•] [ ]
Row 6:  [ ] [•] [ ] [•] [ ] [•] [ ] [•]
Row 7:  [•] [ ] [•] [ ] [•] [ ] [•] [ ]   <- White Home Row
        ([•] = Playable Dark Square, [ ] = Inactive Light Square)
```

White pieces move **upward** (decreasing `Row` toward `0`).  
Black pieces move **downward** (increasing `Row` toward `7`).

---

## Standard Draughts 1–32 Numbering

Official Draughts literature, match records, and Portable Draughts Notation (PDN) number only the 32 playable dark squares from **1 to 32**, reading left-to-right, top-to-bottom starting from Black's back rank:

| Row | Col 0 | Col 1 | Col 2 | Col 3 | Col 4 | Col 5 | Col 6 | Col 7 |
|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **0** | — | **1** | — | **2** | — | **3** | — | **4** |
| **1** | **5** | — | **6** | — | **7** | — | **8** | — |
| **2** | — | **9** | — | **10** | — | **11** | — | **12** |
| **3** | **13** | — | **14** | — | **15** | — | **16** | — |
| **4** | — | **17** | — | **18** | — | **19** | — | **20** |
| **5** | **21** | — | **22** | — | **23** | — | **24** | — |
| **6** | — | **25** | — | **26** | — | **27** | — | **28** |
| **7** | **29** | — | **30** | — | **31** | — | **32** | — |

### Mathematical Mapping Formulas

The mapping implemented in `Position.cs` computes this conversion in $O(1)$ time without lookup tables:

#### 1. From `(Row, Col)` to Draughts Index (1..32):
Because there are 4 dark squares per row:
$$\text{Index} = 4 \cdot \text{Row} + \lfloor\text{Col} / 2\rfloor + 1$$

#### 2. From Draughts Index (1..32) to `(Row, Col)`:
Let $Z = \text{Index} - 1$ (zero-indexed, $0 \le Z \le 31$):
$$\text{Row} = \lfloor Z / 4 \rfloor$$
$$\text{ColInRow} = Z \bmod 4$$
$$\text{Col} = \begin{cases} 2 \cdot \text{ColInRow} + 1 & \text{if } \text{Row is even} \\ 2 \cdot \text{ColInRow} & \text{if } \text{Row is odd} \end{cases}$$

---

## Data structures

### `Position`
```csharp
public readonly record struct Position(int Row, int Col)
{
    public bool IsValid => Row is >= 0 and < 8 && Col is >= 0 and < 8;
    public bool IsDarkSquare => IsValid && (Row + Col) % 2 != 0;

    public Position Offset(int dRow, int dCol) => new(Row + dRow, Col + dCol);

    public int? ToDraughtsIndex();
    public static Position FromDraughtsIndex(int index);
    public static bool TryFromDraughtsIndex(int index, out Position position);
}
```
`Position` is a `readonly record struct`, meaning value comparisons (`==`), dictionary hashing, and stack allocations are fast and generate zero garbage collection overhead.

### `Piece`
```csharp
public readonly record struct Piece(PieceColor Color, PieceType Type)
{
    public bool IsKing => Type == PieceType.King;
    public bool IsMan => Type == PieceType.Man;

    public Piece Crown() => new(Color, PieceType.King);
}
```
* `PieceColor`: `White` (`0`) or `Black` (`1`).
* `PieceType`: `Man` (`0`) or `King` (`1`).

### `BoardState`
The `BoardState` class represents an instantaneous snapshot of the entire game:
* `Piece?[,] _grid`: 8x8 nullable array storing pieces on dark squares.
* `PieceColor ActivePlayer`: Player whose turn it is (`White` moves first).
* `int HalfMoveClock`: Counter tracking half-moves since the last capture or promotion (resets to 0 on captures/promotions; triggers draw at 80).
* `int FullMoveNumber`: Incremented after each move completed by Black.
* `int WhitePiecesCount`, `BlackPiecesCount`: Fast $O(1)$ piece totals.
* `int WhiteKingsCount`, `BlackKingsCount`: Fast $O(1)$ crowned king totals.
* `ulong ZobristHash`: Cached 64-bit hash.

### `BitPosition` (64-Bit Bitboard Representation)
For high-speed move generation and Alpha-Beta tree search, `BitPosition` (`Checkers.Core.Bitboards`) packs the 8×8 board into four 64-bit unsigned integers (`WhiteMen`, `BlackMen`, `WhiteKings`, `BlackKings`), where bit `sq = Row * 8 + Col` (`0..63`) corresponds to `(Row, Col)`. See [Chapter 15 – Bitboards](15-bitboards.md) for the complete architecture and benchmarks.

---

## Initial setup

The game begins via `BoardState.CreateInitial()`:
- **Black:** 12 men on squares **1 through 12** (rows 0, 1, 2 on dark squares).
- **Empty:** Squares **13 through 20** (rows 3 and 4) are vacant.
- **White:** 12 men on squares **21 through 32** (rows 5, 6, 7 on dark squares).
- **Turn:** `White` to move.
- `HalfMoveClock = 0`, `FullMoveNumber = 1`.

---

## Zobrist hashing

Zobrist hashing maps any given board configuration and active turn to a single pseudo-random 64-bit integer (`ulong`).

### Why Zobrist hashing is essential
1. **Threefold Repetition Detection:** Rapidly checking if the current position has occurred 3 times in the move history without comparing arrays.
2. **Transposition Table (Phase 4):** Enables the Minimax AI to cache evaluated sub-trees in a fixed-size hash table.

### Key Generation and XOR Math
At startup, `Zobrist.cs` initializes a fixed random table using a deterministic seed:
* `PieceTable[64, 4]`: One 64-bit random number for each combination of 64 board squares and 4 piece configurations (White Man, White King, Black Man, Black King).
* `BlackToMoveKey`: A 64-bit key XORed when it is Black's turn (0 if White's turn).

The state hash $H$ is computed as:

$$H = K_{\text{turn}} \oplus \bigoplus_{(r,c)} K_{\text{piece}}(r, c, \text{piece})$$

Because XOR ($\oplus$) is associative and self-inverting ($A \oplus B \oplus B = A$), updating the hash when a piece moves or is removed requires only a few XOR operations rather than rescanning the whole board.
