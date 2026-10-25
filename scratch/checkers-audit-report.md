# Checkers Documentation & Code Snippet Audit Report (Phase 1 Baseline)

Generated: 2026-10-25T11:42:37.663Z  
Target: **18 files** (17 chapters in `docs/brain/` + `docs/general-game-gui-specification.md`)  
Total Fenced Code Blocks: **47**  
Detected Discrepancies: **0** (0 Errors, 0 Warnings)

---

## 1. Discrepancy Summary by Category

| Issue ID | Severity | Occurrences | Description | Expected Correction |
|---|:---:|:---:|---|---|
| `METHOD_NAME_NEGAMAX` | **ERROR** | 0 | References 'NegamaxBitboard' instead of active C# method 'NegaMax' | `NegaMax` |
| `TT_GHOST_POINTER` | **ERROR** | 0 | References non-existent cached pointer '_entriesPtr' (production uses MemoryMarshal.GetArrayDataReference) | `ref MemoryMarshal.GetArrayDataReference(_entries)` |
| `TT_BOUND_FLAG_EXACT_1` | **ERROR** | 0 | Cites ancestral C macro 'PV_NODE 1' / 'Exact (1)' instead of active C# enum 'Exact = 0' | `Exact = 0` |
| `TT_BOUND_FLAG_LOWER_2` | **ERROR** | 0 | Cites ancestral C macro 'LOWER_BOUND 2' instead of active C# enum 'LowerBound = 1' | `LowerBound = 1` |
| `TT_BOUND_FLAG_UPPER_3` | **ERROR** | 0 | Cites ancestral C macro 'UPPER_BOUND 3' instead of active C# enum 'UpperBound = 2' | `UpperBound = 2` |
| `TT_FLAG_BIT_2_EVAL` | **ERROR** | 0 | Claims bit 2 (0x04) is HasStaticEval; in production 0x04 is OccupiedBit and 0x10 is HasStaticEvalBit | `OccupiedBit = 0x04, HasStaticEvalBit = 0x10` |
| `ORDERING_FICTITIOUS_CAPTURE_BRANCH` | **ERROR** | 0 | Fictitious 'if (moves[0].IsCapture)' branch in OrderBitMoves snippet (not present in MinimaxPlayer.cs) | `Inline scores[i] capture scoring` |
| `ORDERING_CAPTURE_MOVES_IN_ORDER_BIT_MOVES` | **ERROR** | 0 | Calls OrderCaptureMoves(moves) inside OrderBitMoves snippet; in production it is only called by Quiescence | `Only called in Quiescence` |
| `BITMOVE_RECORD_STRUCT` | **WARNING** | 0 | Describes BitMove as 'readonly record struct'; in BitMove.cs it is 'readonly struct' | `readonly struct` |
| `BITBOARD_UPMOVERS_PSEUDOCODE` | **WARNING** | 0 | Uses pseudocode variable 'upMovers' in bitboard move snippet instead of active C# 'ownMen' | `ownMen` |
| `BITBOARD_BARE_NOTCOL` | **WARNING** | 0 | Uses unqualified 'NotCol*' constant in C# snippet without 'BitboardMasks.' qualifier | `BitboardMasks.NotColA` |
| `OUTDATED_TEST_COUNT_CORE` | **WARNING** | 0 | Cites 123 Core tests, but Checkers.Core.Tests currently has 124 passing tests | `124 unit tests` |
| `OUTDATED_TEST_COUNT_APP` | **WARNING** | 0 | Cites 40 App tests, but Checkers.App.Tests currently has 70 passing tests | `70 unit tests` |

---

## 2. Itemized Discrepancy Register

| # | File | Line | Context | Severity | Detected Text | Description |
|---|---|:---:|:---:|:---:|---|---|


---

## 3. Fenced Code Block Inventory (All 47 Blocks)

| # | File | Line Range | Language | Size | Preview |
|---|---|:---:|:---:|:---:|---|
| 1 | [01-overview.md](../docs/brain/01-overview.md#L25-L55) | L25–L55 | `mermaid` | 31 lines | `flowchart TD     subgraph Presentation ["Presentation Layer"]         WPF["Checkers.Wpf<br/>(Windows` |
| 2 | [01-overview.md](../docs/brain/01-overview.md#L106-L127) | L106–L127 | `mermaid` | 22 lines | `sequenceDiagram     autonumber     actor Player as Human / AI` |
| 3 | [02-board-and-coordinates.md](../docs/brain/02-board-and-coordinates.md#L74-L117) | L74–L117 | `mermaid` | 44 lines | `classDiagram     class Position {         <<readonly record struct>>` |
| 4 | [03-rules-and-move-generation.md](../docs/brain/03-rules-and-move-generation.md#L87-L89) | L87–L89 | `text` | 3 lines | `[Flying King] ──► [Empty]* ──► [Enemy 1] ──► [Landing A] ──► [Landing B] ──► [Blocker / Edge]` |
| 5 | [03-rules-and-move-generation.md](../docs/brain/03-rules-and-move-generation.md#L112-L120) | L112–L120 | `mermaid` | 9 lines | `flowchart TD     Start["Generate Moves for ActivePlayer"] --> CheckCap{"Does any piece have<br/>a le` |
| 6 | [04-game-record.md](../docs/brain/04-game-record.md#L19-L30) | L19–L30 | `csharp` | 12 lines | `public sealed record Move {     public required Position From { get; init; }` |
| 7 | [04-game-record.md](../docs/brain/04-game-record.md#L44-L52) | L44–L52 | `mermaid` | 9 lines | `stateDiagram-v2     [*] --> InProgress : StartNewGame() /CreateInitial()     InProgress --> InProgre` |
| 8 | [04-game-record.md](../docs/brain/04-game-record.md#L99-L109) | L99–L109 | `pdn` | 11 lines | `[Event "Checkers Match"] [Variant "International"] [GameMode "HumanVsComputer"]` |
| 9 | [04-game-record.md](../docs/brain/04-game-record.md#L113-L122) | L113–L122 | `mermaid` | 10 lines | `flowchart LR     File["PDN Text (.pdn / .checkers)"] --> Tags["Parse [Key &quot;Value&quot;] Headers` |
| 10 | [05-evaluation.md](../docs/brain/05-evaluation.md#L27-L57) | L27–L57 | `mermaid` | 31 lines | `flowchart TD     Pos["BitPosition (in pos)<br/>WM, WK, BM, BK (4 × ulong)"] --> Branch{"CheckersVari` |
| 11 | [06-move-ordering.md](../docs/brain/06-move-ordering.md#L23-L30) | L23–L30 | `mermaid` | 8 lines | `flowchart TD     Gen["BitboardMoveGenerator.Generate(in pos, moveBuffer)<br/>Produces Span&lt;BitMov` |
| 12 | [06-move-ordering.md](../docs/brain/06-move-ordering.md#L83-L86) | L83–L86 | `csharp` | 4 lines | `currentOrder.Remove(bestEntryThisDepth.Value); currentOrder.Insert(0, bestEntryThisDepth.Value);` |
| 13 | [06-move-ordering.md](../docs/brain/06-move-ordering.md#L99-L172) | L99–L172 | `csharp` | 74 lines | `private void OrderBitMoves(     Span<BitMove> moves,     ushort ttPackedMove,` |
| 14 | [07-search.md](../docs/brain/07-search.md#L26-L58) | L26–L58 | `mermaid` | 33 lines | `flowchart TD     Entry["NegaMax(pos, depth d, α, β, ply)"] --> Term{"Terminal / Draw check:<br/>0 ow` |
| 15 | [08-transposition-table.md](../docs/brain/08-transposition-table.md#L22-L28) | L22–L28 | `text` | 7 lines | `┌────────────────────┬───────────┬────────────┬───────────┬───────┬──────┬───────┬───────────┐ │    ` |
| 16 | [08-transposition-table.md](../docs/brain/08-transposition-table.md#L67-L80) | L67–L80 | `mermaid` | 14 lines | `flowchart TD     Probe["TryProbe(key, depth, α, β, ply)"] --> Base["baseIndex = (int)(key &amp; _buc` |
| 17 | [08-transposition-table.md](../docs/brain/08-transposition-table.md#L110-L122) | L110–L122 | `csharp` | 13 lines | `[MethodImpl(MethodImplOptions.AggressiveInlining)] public unsafe void Prefetch(ulong key) {` |
| 18 | [09-time-control.md](../docs/brain/09-time-control.md#L47-L70) | L47–L70 | `mermaid` | 24 lines | `flowchart TD     Start["Start Turn"] --> Forced{"Only 1 legal<br/>root move?"}     Forced -- "Yes" -` |
| 19 | [10-opening-book.md](../docs/brain/10-opening-book.md#L19-L26) | L19–L26 | `mermaid` | 8 lines | `flowchart LR     S1["<b>Stage 1: Top-Down Width-3 DAG (lv 0..8)</b><br/>• Goal: Store only the 3 bes` |
| 20 | [10-opening-book.md](../docs/brain/10-opening-book.md#L58-L79) | L58–L79 | `mermaid` | 22 lines | `flowchart TD     subgraph Gen["Offline CLI Generation (BookCommands.cs &amp; BookBuilder.cs)"]      ` |
| 21 | [10-opening-book.md](../docs/brain/10-opening-book.md#L172-L178) | L172–L178 | `text` | 7 lines | `# Checkers Opening Book (English) — depth 12 plies (lv 0..11 evaluated), width 3, 32369 unique posit` |
| 22 | [10-opening-book.md](../docs/brain/10-opening-book.md#L197-L199) | L197–L199 | `powershell` | 3 lines | `dotnet run -c Release --project tools/Checkers.Benchmark -- book <subcommand> [options]` |
| 23 | [10-opening-book.md](../docs/brain/10-opening-book.md#L244-L259) | L244–L259 | `powershell` | 16 lines | `# 1. Stage 1 baseline: Uniform top-down width-3 generation to lv 8 (max-level 7) dotnet run -c Relea` |
| 24 | [11-bitboards.md](../docs/brain/11-bitboards.md#L28-L37) | L28–L37 | `text` | 10 lines | `Row 0 (Black Back Rank):   0   [1]   2   [3]   4   [5]   6   [7] Row 1:                    [8]   9  ` |
| 25 | [11-bitboards.md](../docs/brain/11-bitboards.md#L77-L84) | L77–L84 | `csharp` | 8 lines | `// Quiet 1-step moves for White men (directions 0 and 1: -9, -7) if (((ownMen & BitboardMasks.NotCol` |
| 26 | [11-bitboards.md](../docs/brain/11-bitboards.md#L113-L123) | L113–L123 | `mermaid` | 11 lines | `flowchart TD     RayStart["For each diagonal dir in 0..3:<br/>ulong ray = Rays[dir, sq]"] --> Guard1` |
| 27 | [11-bitboards.md](../docs/brain/11-bitboards.md#L153-L162) | L153–L162 | `mermaid` | 10 lines | `flowchart LR     Root["BoardState (Root)"] --> Convert["state.BitPosition (O(1))"]     Convert --> I` |
| 28 | [12-engine-improvements.md](../docs/brain/12-engine-improvements.md#L25-L36) | L25–L36 | `mermaid` | 12 lines | `flowchart LR     M0["Baseline Prototype<br/>Piece?[8,8] Array Engine<br/>0.65M NPS • 148.8s (40 Deep` |
| 29 | [12-engine-improvements.md](../docs/brain/12-engine-improvements.md#L500-L516) | L500–L516 | `powershell` | 17 lines | `# Run the Phase 0..5 progression benchmark (40-position deep suite + 5-position 5.0s timed suite) do` |
| 30 | [13-app-integration.md](../docs/brain/13-app-integration.md#L17-L52) | L17–L52 | `mermaid` | 36 lines | `flowchart TD     subgraph Presentation ["Presentation Layer"]         WPF["Checkers.Wpf<br/>(WPF Des` |
| 31 | [13-app-integration.md](../docs/brain/13-app-integration.md#L95-L115) | L95–L115 | `mermaid` | 21 lines | `sequenceDiagram     autonumber     participant UI as Browser Event Loop / Home.razor` |
| 32 | [14-gui-and-settings.md](../docs/brain/14-gui-and-settings.md#L17-L31) | L17–L31 | `mermaid` | 15 lines | `graph TD     subgraph UI Layout         MenuBar["Menu Bar<br/>File • Game • Options • Help (Desktop)` |
| 33 | [14-gui-and-settings.md](../docs/brain/14-gui-and-settings.md#L105-L118) | L105–L118 | `text` | 14 lines | `┌──────────────────────────────────────────────────────────────┐ │ STATUS            [Flying Kings] ` |
| 34 | [14-gui-and-settings.md](../docs/brain/14-gui-and-settings.md#L170-L176) | L170–L176 | `mermaid` | 7 lines | `graph TD     Settings["Settings Dialog"]     Settings --> Section1["1. Checkers Rules &amp; Variant<` |
| 35 | [14-gui-and-settings.md](../docs/brain/14-gui-and-settings.md#L208-L220) | L208–L220 | `text` | 13 lines | `┌─────────────────────────────────┐ │ ANALYSIS                        │ ├───────────────────┬───────` |
| 36 | [README.md](../docs/brain/README.md#L22-L45) | L22–L45 | `mermaid` | 24 lines | `flowchart TD     Start["Current BoardState &amp; ActivePlayer<br/>(64-Bit BitPosition + Zobrist Hash` |
| 37 | [README.md](../docs/brain/README.md#L76-L95) | L76–L95 | `mermaid` | 20 lines | `flowchart LR     C01["01 Overview"] --> C02["02 Board &amp; Coords"]     C02 --> C03["03 Rules &amp;` |
| 38 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L28-L46) | L28–L46 | `mermaid` | 19 lines | `graph TD     App["Game Application Window / Page"]     App --> Header["Top Header / Menu System<br/>` |
| 39 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L51-L78) | L51–L78 | `text` | 28 lines | `┌───────────────────────────────────────────────────────────────────────────────────────────────────` |
| 40 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L81-L105) | L81–L105 | `text` | 25 lines | `┌────────────────────────────────────────┐ │ ☰  Checkers .NET                       │  <-- Header wi` |
| 41 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L134-L143) | L134–L143 | `text` | 10 lines | `File                     Game                          Options              Help ├── New Game (Ctrl+` |
| 42 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L181-L202) | L181–L202 | `mermaid` | 22 lines | `sequenceDiagram     autonumber     actor User as Mobile User (Touch)` |
| 43 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L212-L225) | L212–L225 | `text` | 14 lines | `┌────────────────────────────────────────────────────────────────────────┐ │ STATUS              [Va` |
| 44 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L241-L253) | L241–L253 | `mermaid` | 13 lines | `stateDiagram-v2     [*] --> Unmodified: Start New Game vs Computer     Unmodified: STATUS text is Sl` |
| 45 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L265-L272) | L265–L272 | `text` | 8 lines | `┌───────────────────────────────────────────────┐ │ [Glyph] [Player Name]                 [Clock] │ ` |
| 46 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L293-L298) | L293–L298 | `mermaid` | 6 lines | `flowchart TD     Timeout["Human Player Clock Reaches 0:00"] --> Dialog{"Modal Dialog:<br/>'You ran o` |
| 47 | [general-game-gui-specification.md](../docs/general-game-gui-specification.md#L345-L351) | L345–L351 | `mermaid` | 7 lines | `graph TD     SettingsDialog["Modal Settings Dialog"]     SettingsDialog --> Sec1["1. Rules &amp; Gam` |

---

## 4. File-by-File Code Block Distribution

| File | Lines | Code Blocks | Languages Present |
|---|---:|---:|---|
| [01-overview.md](../docs/brain/01-overview.md) | 144 | 2 | mermaid (2) |
| [02-board-and-coordinates.md](../docs/brain/02-board-and-coordinates.md) | 165 | 1 | mermaid (1) |
| [03-rules-and-move-generation.md](../docs/brain/03-rules-and-move-generation.md) | 133 | 2 | text (1), mermaid (1) |
| [04-game-record.md](../docs/brain/04-game-record.md) | 125 | 4 | csharp (1), mermaid (2), pdn (1) |
| [05-evaluation.md](../docs/brain/05-evaluation.md) | 167 | 1 | mermaid (1) |
| [06-move-ordering.md](../docs/brain/06-move-ordering.md) | 186 | 3 | mermaid (1), csharp (2) |
| [07-search.md](../docs/brain/07-search.md) | 225 | 1 | mermaid (1) |
| [08-transposition-table.md](../docs/brain/08-transposition-table.md) | 178 | 3 | text (1), mermaid (1), csharp (1) |
| [09-time-control.md](../docs/brain/09-time-control.md) | 128 | 1 | mermaid (1) |
| [10-opening-book.md](../docs/brain/10-opening-book.md) | 259 | 5 | mermaid (2), text (1), powershell (2) |
| [11-bitboards.md](../docs/brain/11-bitboards.md) | 186 | 4 | text (1), csharp (1), mermaid (2) |
| [12-engine-improvements.md](../docs/brain/12-engine-improvements.md) | 518 | 2 | mermaid (1), powershell (1) |
| [13-app-integration.md](../docs/brain/13-app-integration.md) | 138 | 2 | mermaid (2) |
| [14-gui-and-settings.md](../docs/brain/14-gui-and-settings.md) | 290 | 4 | mermaid (2), text (2) |
| [15-glossary.md](../docs/brain/15-glossary.md) | 66 | 0 | — |
| [16-references.md](../docs/brain/16-references.md) | 79 | 0 | — |
| [README.md](../docs/brain/README.md) | 101 | 2 | mermaid (2) |
| [general-game-gui-specification.md](../docs/general-game-gui-specification.md) | 394 | 10 | mermaid (5), text (5) |
