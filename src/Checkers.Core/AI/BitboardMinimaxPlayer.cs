using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Checkers.Core.Bitboards;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// High-performance Bitboard Minimax AI with Alpha-Beta pruning, iterative deepening, PV and TT move ordering,
/// 64-bit Zobrist transposition table, quiescence search, live progress reporting, and time controls.
/// Uses value-type <see cref="BitPosition"/> copy-make and preallocated <see cref="BitMove"/> buffers
/// to produce 100% identical search trees, node counts, scores, and moves to <see cref="MinimaxPlayer"/>.
/// </summary>
public sealed class BitboardMinimaxPlayer : IPlayer
{
    private const int WinScore = 100_000;
    private const int LossScore = -100_000;
    private const int MaxPly = 64;
    private const int MaxMovesPerNode = 128;

    private readonly CheckersVariant _variant;
    private readonly BitMove[][] _moveBuffers;

    public string Name { get; }
    public SearchLimits Limits { get; }
    public int Depth => Limits.Depth;
    public long NodesEvaluated { get; private set; }
    public long LeafEvaluations { get; private set; }
    public SearchAnalysis? LastAnalysis { get; private set; }

    public TranspositionTable? TranspositionTable { get; }
    public bool UseTranspositionTable => TranspositionTable != null;
    public bool UseQuiescence { get; set; } = true;

    public BitboardMinimaxPlayer(
        SearchLimits limits,
        CheckersVariant variant = CheckersVariant.International,
        string? name = null,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true)
    {
        Limits = limits;
        _variant = variant;
        Name = name ?? limits.Mode switch
        {
            TimeControlMode.FixedDepth => $"Bitboard Minimax (Depth {limits.Depth})",
            TimeControlMode.TimePerMove => $"Bitboard Minimax ({limits.Time.TotalSeconds:0}s/move)",
            TimeControlMode.TimePerGame => "Bitboard Minimax (Clock)",
            _ => "Bitboard Minimax AI"
        };
        TranspositionTable = useTranspositionTable
            ? (transpositionTable ?? new TranspositionTable(megabytes: 32))
            : null;
        UseQuiescence = useQuiescence;

        _moveBuffers = new BitMove[MaxPly][];
        for (int i = 0; i < MaxPly; i++)
        {
            _moveBuffers[i] = new BitMove[MaxMovesPerNode];
        }
    }

    public BitboardMinimaxPlayer(
        int depth = 4,
        CheckersVariant variant = CheckersVariant.International,
        string? name = null,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true)
        : this(SearchLimits.FixedDepth(depth), variant, name, transpositionTable, useTranspositionTable, useQuiescence)
    {
    }

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress: null, cancellationToken);

    public async ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves available.");

        if (legalMoves.Count == 1)
        {
            var singleMove = legalMoves[0];
            LastAnalysis = new SearchAnalysis
            {
                Move = singleMove.Notation,
                Depth = "1 ply (Forced)",
                Value = "Forced",
                BestMove = singleMove.Notation,
                Nodes = "1",
                Evaluations = "0",
                Time = "0:00.0"
            };
            progress?.Report(LastAnalysis);
            if (progress != null)
            {
                await Task.Delay(1, cancellationToken);
            }
            return singleMove;
        }

        var sw = Stopwatch.StartNew();
        NodesEvaluated = 0;
        LeafEvaluations = 0;
        long lastReportMs = 0;

        TranspositionTable?.NewSearch();

        long softLimitMs = long.MaxValue;
        long hardLimitMs = long.MaxValue;
        int maxTargetDepth = Limits.Mode == TimeControlMode.FixedDepth ? Limits.Depth : 20;

        if (Limits.Mode == TimeControlMode.TimePerMove)
        {
            hardLimitMs = (long)Limits.Time.TotalMilliseconds;
            softLimitMs = (hardLimitMs * 2) / 3;
        }
        else if (Limits.Mode == TimeControlMode.TimePerGame)
        {
            int piecesLeft = state.WhitePiecesCount + state.BlackPiecesCount;
            int estimatedMoves = Math.Clamp(piecesLeft, 2, 25);
            long allocatedMs = (long)(Limits.Time.TotalMilliseconds / estimatedMoves);
            hardLimitMs = Math.Max(10, allocatedMs);
            softLimitMs = (hardLimitMs * 2) / 3;
        }

        BitPosition rootPos = BitPosition.FromBoardState(state);

        var currentOrder = legalMoves
            .OrderByDescending(m => m.CapturedPositions.Count)
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)
            .Select(m => (Move: m, BitMove: BitMove.FromMove(m)))
            .ToList();

        Move? bestMoveOverall = null;
        int bestScoreOverall = 0;
        int completedDepth = 1;

        for (int d = 1; d <= maxTargetDepth; d++)
        {
            if (d > 1 && sw.ElapsedMilliseconds >= softLimitMs)
                break;

            (Move Move, BitMove BitMove)? bestEntryThisDepth = null;
            int bestScoreThisDepth = int.MinValue;
            int alpha = -200_000;
            int beta = 200_000;
            bool depthAborted = false;

            for (int i = 0; i < currentOrder.Count; i++)
            {
                var entry = currentOrder[i];
                cancellationToken.ThrowIfCancellationRequested();
                if (sw.ElapsedMilliseconds >= hardLimitMs)
                {
                    depthAborted = true;
                    break;
                }

                BitPosition nextPos = rootPos.Apply(in entry.BitMove);
                int score = -NegaMax(
                    in nextPos,
                    d - 1,
                    -beta,
                    -alpha,
                    ply: 1,
                    sw,
                    hardLimitMs,
                    cancellationToken,
                    progress,
                    ref lastReportMs,
                    d,
                    entry.Move,
                    bestMoveOverall,
                    bestScoreOverall,
                    out bool aborted);

                if (aborted)
                {
                    depthAborted = true;
                    break;
                }

                if (score > bestScoreThisDepth)
                {
                    bestScoreThisDepth = score;
                    bestEntryThisDepth = entry;
                }

                if (score > alpha)
                {
                    alpha = score;
                }

                if (progress != null && sw.ElapsedMilliseconds - lastReportMs >= 150)
                {
                    lastReportMs = sw.ElapsedMilliseconds;
                    var heartbeatAnalysis = new SearchAnalysis
                    {
                        Move = entry.Move.Notation,
                        Depth = $"{d} plies...",
                        Value = FormatValue(bestScoreThisDepth > int.MinValue ? bestScoreThisDepth : bestScoreOverall),
                        BestMove = bestEntryThisDepth?.Move.Notation ?? bestMoveOverall?.Notation ?? entry.Move.Notation,
                        Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                        Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
                        Time = sw.Elapsed.ToString(@"m\:ss\.f")
                    };
                    progress.Report(heartbeatAnalysis);
                    await Task.Delay(1, cancellationToken);
                }
            }

            if (!depthAborted && bestEntryThisDepth.HasValue)
            {
                bestMoveOverall = bestEntryThisDepth.Value.Move;
                bestScoreOverall = bestScoreThisDepth;
                completedDepth = d;

                currentOrder.Remove(bestEntryThisDepth.Value);
                currentOrder.Insert(0, bestEntryThisDepth.Value);

                var iterationAnalysis = new SearchAnalysis
                {
                    Move = bestMoveOverall.Notation,
                    Depth = $"{completedDepth} plies",
                    Value = FormatValue(bestScoreOverall),
                    BestMove = bestMoveOverall.Notation,
                    Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                    Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
                    Time = sw.Elapsed.ToString(@"m\:ss\.f")
                };
                LastAnalysis = iterationAnalysis;
                lastReportMs = sw.ElapsedMilliseconds;
                progress?.Report(iterationAnalysis);

                if (progress != null)
                {
                    await Task.Delay(1, cancellationToken);
                }

                if (bestScoreOverall >= 90_000)
                    break;
            }
            else
            {
                break;
            }
        }

        sw.Stop();

        bestMoveOverall ??= currentOrder[0].Move;

        LastAnalysis = new SearchAnalysis
        {
            Move = bestMoveOverall.Notation,
            Depth = $"{completedDepth} plies",
            Value = FormatValue(bestScoreOverall),
            BestMove = bestMoveOverall.Notation,
            Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
            Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

        progress?.Report(LastAnalysis);

        return bestMoveOverall;
    }

    private int NegaMax(
        in BitPosition pos,
        int depth,
        int alpha,
        int beta,
        int ply,
        Stopwatch sw,
        long hardLimitMs,
        CancellationToken cancellationToken,
        IProgress<SearchAnalysis>? progress,
        ref long lastReportMs,
        int currentIterDepth,
        Move? currentRootMove,
        Move? bestRootMoveOverall,
        int bestScoreOverall,
        out bool aborted)
    {
        aborted = false;

        if ((NodesEvaluated & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds >= hardLimitMs)
            {
                aborted = true;
                return 0;
            }

            if (progress != null && sw.ElapsedMilliseconds - lastReportMs >= 200)
            {
                lastReportMs = sw.ElapsedMilliseconds;
                progress.Report(new SearchAnalysis
                {
                    Move = currentRootMove?.Notation ?? bestRootMoveOverall?.Notation ?? "-",
                    Depth = $"{currentIterDepth} plies...",
                    Value = FormatValue(bestScoreOverall),
                    BestMove = bestRootMoveOverall?.Notation ?? "-",
                    Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                    Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
                    Time = sw.Elapsed.ToString(@"m\:ss\.f")
                });
            }
        }

        NodesEvaluated++;

        // Fast terminal status check matching RuleEngine.EvaluateGameStatus
        ulong ownPieces = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
        if (ownPieces == 0UL || !BitboardMoveGenerator.HasAnyLegalMove(in pos, _variant))
        {
            return LossScore + ply;
        }
        if (pos.HalfMoveClock >= 80)
        {
            return 0;
        }

        int originalAlpha = alpha;
        Position ttFrom = default;
        Position ttTo = default;
        bool hasTtMove = false;
        byte ttFromSq = 0;
        byte ttToSq = 0;

        // Transposition Table Probe
        if (TranspositionTable != null &&
            TranspositionTable.TryProbe(pos.Hash, depth, alpha, beta, ply, out int ttScore, out ttFrom, out ttTo, out bool hasCutoff))
        {
            if (hasCutoff)
            {
                return ttScore;
            }
            if (ttFrom.IsValid && ttTo.IsValid)
            {
                hasTtMove = true;
                ttFromSq = (byte)BitboardMasks.ToSquareIndex(ttFrom);
                ttToSq = (byte)BitboardMasks.ToSquareIndex(ttTo);
            }
        }

        // Quiescence search at leaf nodes
        if (depth == 0)
        {
            if (UseQuiescence)
            {
                return Quiescence(in pos, alpha, beta, ply, sw, hardLimitMs, cancellationToken, out aborted);
            }
            LeafEvaluations++;
            return BitboardEvaluation.Evaluate(in pos, _variant);
        }

        Span<BitMove> moveBuffer = ply < MaxPly ? _moveBuffers[ply] : stackalloc BitMove[MaxMovesPerNode];
        int moveCount = BitboardMoveGenerator.Generate(in pos, _variant, moveBuffer);
        if (moveCount == 0)
        {
            return LossScore + ply;
        }

        Span<BitMove> moves = moveBuffer.Slice(0, moveCount);
        OrderBitMoves(moves, ttFromSq, ttToSq, hasTtMove);

        BitMove bestMoveThisNode = default;
        bool hasBestMove = false;
        int maxScore = int.MinValue;

        for (int i = 0; i < moves.Length; i++)
        {
            BitPosition nextPos = pos.Apply(in moves[i]);
            int score = -NegaMax(
                in nextPos,
                depth - 1,
                -beta,
                -alpha,
                ply + 1,
                sw,
                hardLimitMs,
                cancellationToken,
                progress,
                ref lastReportMs,
                currentIterDepth,
                currentRootMove,
                bestRootMoveOverall,
                bestScoreOverall,
                out aborted);

            if (aborted)
                return 0;

            if (score > maxScore)
            {
                maxScore = score;
                bestMoveThisNode = moves[i];
                hasBestMove = true;
            }

            if (score > alpha)
            {
                alpha = score;
            }

            if (alpha >= beta)
            {
                break;
            }
        }

        // Transposition Table Store
        if (TranspositionTable != null && !aborted)
        {
            TranspositionBound bound;
            if (maxScore <= originalAlpha)
                bound = TranspositionBound.UpperBound;
            else if (maxScore >= beta)
                bound = TranspositionBound.LowerBound;
            else
                bound = TranspositionBound.Exact;

            TranspositionTable.Store(
                pos.Hash,
                depth,
                maxScore,
                bound,
                ply,
                hasBestMove ? BitboardMasks.Positions[bestMoveThisNode.From] : default,
                hasBestMove ? BitboardMasks.Positions[bestMoveThisNode.To] : default);
        }

        return maxScore;
    }

    private int Quiescence(
        in BitPosition pos,
        int alpha,
        int beta,
        int ply,
        Stopwatch sw,
        long hardLimitMs,
        CancellationToken cancellationToken,
        out bool aborted)
    {
        aborted = false;

        if ((NodesEvaluated & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds >= hardLimitMs)
            {
                aborted = true;
                return 0;
            }
        }

        NodesEvaluated++;

        ulong ownPieces = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
        if (ownPieces == 0UL || !BitboardMoveGenerator.HasAnyLegalMove(in pos, _variant))
        {
            return LossScore + ply;
        }
        if (pos.HalfMoveClock >= 80)
        {
            return 0;
        }

        int standPat = BitboardEvaluation.Evaluate(in pos, _variant);
        LeafEvaluations++;

        if (standPat >= beta)
        {
            return beta;
        }

        if (standPat > alpha)
        {
            alpha = standPat;
        }

        if (ply >= 24)
        {
            return alpha;
        }

        Span<BitMove> moveBuffer = ply < MaxPly ? _moveBuffers[ply] : stackalloc BitMove[MaxMovesPerNode];
        int captureCount = BitboardMoveGenerator.GenerateCaptures(in pos, _variant, moveBuffer);
        if (captureCount == 0)
        {
            return alpha;
        }

        Span<BitMove> captures = moveBuffer.Slice(0, captureCount);
        OrderBitMoves(captures, 0, 0, hasTtMove: false);

        for (int i = 0; i < captures.Length; i++)
        {
            BitPosition nextPos = pos.Apply(in captures[i]);
            int score = -Quiescence(in nextPos, -beta, -alpha, ply + 1, sw, hardLimitMs, cancellationToken, out aborted);

            if (aborted)
                return 0;

            if (score >= beta)
            {
                return beta;
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }

        return alpha;
    }

    /// <summary>
    /// In-place stable insertion sort ordering moves by:
    /// 1. TT move (first matching From/To)
    /// 2. CaptureCount descending
    /// 3. IsPromotion descending
    /// Preserves original generation order for ties, matching <see cref="MinimaxPlayer"/> exactly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OrderBitMoves(Span<BitMove> moves, byte ttFrom, byte ttTo, bool hasTtMove)
    {
        for (int i = 1; i < moves.Length; i++)
        {
            BitMove current = moves[i];
            int currentKey = (current.CaptureCount << 1) | (current.IsPromotion ? 1 : 0);
            if (currentKey == 0)
                continue;

            int j = i - 1;
            while (j >= 0)
            {
                int prevKey = (moves[j].CaptureCount << 1) | (moves[j].IsPromotion ? 1 : 0);
                if (prevKey >= currentKey)
                    break;
                moves[j + 1] = moves[j];
                j--;
            }
            moves[j + 1] = current;
        }

        if (hasTtMove)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i].From == ttFrom && moves[i].To == ttTo)
                {
                    if (i > 0)
                    {
                        BitMove ttMove = moves[i];
                        for (int j = i; j > 0; j--)
                        {
                            moves[j] = moves[j - 1];
                        }
                        moves[0] = ttMove;
                    }
                    break;
                }
            }
        }
    }

    private static string FormatValue(int score) => score switch
    {
        >= 90_000 => $"+Win in {WinScore - score} plies",
        <= -90_000 => $"-Loss in {score - LossScore} plies",
        > 0 => $"+{score}",
        _ => score.ToString()
    };
}
