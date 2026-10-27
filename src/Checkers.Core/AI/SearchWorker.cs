using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Checkers.Core.Bitboards;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Shared stop request for the helper threads of a Lazy SMP search. Only the main thread sets it.
/// </summary>
internal sealed class SearchStop
{
    public volatile bool Requested;
}

/// <summary>
/// The per-thread part of <see cref="MinimaxPlayer"/>'s search: the alpha-beta and quiescence search together with
/// everything they mutate (move buffers, killers, history, the draw table and the node counters).
/// The main search runs on worker 0; with Lazy SMP each helper thread gets its own worker,
/// and the workers share only the transposition table.
/// </summary>
internal sealed class SearchWorker
{
    private const int LossScore = MinimaxPlayer.LossScore;
    private const int MaxPly = 64;
    private const int MaxMovesPerNode = 128;

    private const int RfpMaxDepth = 6;
    private const int RfpMargin = 40;
    private const int FutilityMaxDepth = 3;
    private const int FutilityMargin = 60;
    private const int LmrMinDepth = 3;
    private const int LmrMinPly = 2;
    private const int LmrMinMoveIndex = 3;
    private const int LmrLateMoveIndex = 8;

    private readonly MinimaxPlayer _owner;
    private readonly CheckersVariant _variant;
    private readonly IEvaluationFunction? _customEvaluator;
    private readonly TranspositionTable? _tt;
    private readonly BitMove[][] _moveBuffers;
    private readonly ushort[] _killers = new ushort[MaxPly * 2];
    private readonly int[] _history = new int[2 * 64 * 64];
    private readonly DrawTable _drawTable = new();

    // Copied from the owner at the start of each search.
    private bool _useQuiescence;
    private bool _useSelectivePruning;

    // Main-thread search limits (helpers only watch _stop).
    private SearchStop? _stop;
    private Stopwatch _sw = new();
    private long _hardLimitMs;
    private CancellationToken _cancellationToken;
    private CancellationToken _moveNowToken;
    private IProgress<SearchAnalysis>? _progress;

    // Node counters, written only by this worker's thread. Each worker allocates its arrays right after itself,
    // so the counters of two workers never share a cache line.
    public long Nodes;
    public long Leaves;

    // Progress context for the in-search heartbeat (main thread only), maintained by the root loop.
    public long LastReportMs;
    public int CurrentIterDepth;
    public Move? CurrentRootMove;
    public Move? BestRootMoveOverall;
    public int BestScoreOverall;

    public SearchWorker(MinimaxPlayer owner, CheckersVariant variant, IEvaluationFunction? customEvaluator, TranspositionTable? tt)
    {
        _owner = owner;
        _variant = variant;
        _customEvaluator = customEvaluator;
        _tt = tt;
        _moveBuffers = new BitMove[MaxPly][];
        for (int i = 0; i < MaxPly; i++)
        {
            _moveBuffers[i] = new BitMove[MaxMovesPerNode];
        }
    }

    /// <summary>
    /// Prepares the worker for a new root search. <paramref name="stop"/> is null for the main thread,
    /// which stops on its own clock and tokens; helpers stop only when the main thread asks them to.
    /// </summary>
    public void BeginSearch(
        in BitPosition rootPos,
        IReadOnlyList<ulong>? stateHashHistory,
        Stopwatch sw,
        long hardLimitMs,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken,
        IProgress<SearchAnalysis>? progress,
        SearchStop? stop)
    {
        _useQuiescence = _owner.UseQuiescence;
        _useSelectivePruning = _owner.UseSelectivePruning;
        _sw = sw;
        _hardLimitMs = hardLimitMs;
        _cancellationToken = cancellationToken;
        _moveNowToken = moveNowToken;
        _progress = progress;
        _stop = stop;

        Nodes = 0;
        Leaves = 0;
        LastReportMs = 0;
        CurrentIterDepth = 0;
        CurrentRootMove = null;
        BestRootMoveOverall = null;
        BestScoreOverall = 0;

        Array.Clear(_killers);
        Array.Clear(_history);
        _drawTable.Reset(in rootPos, stateHashHistory);
    }

    /// <summary>
    /// A Lazy SMP helper: iterative deepening over the root moves with a full window, like the main thread.
    /// Its results are discarded; it only fills the shared transposition table. Never throws.
    /// </summary>
    public void RunHelper(BitPosition rootPos, BitMove[] rootMoves, int startDepth, int maxDepth)
    {
        SearchStop stop = _stop!;
        try
        {
            for (int d = startDepth; d <= maxDepth && !stop.Requested; d++)
            {
                int alpha = -200_000;
                int beta = 200_000;
                int bestScore = int.MinValue;
                int bestIndex = 0;

                for (int i = 0; i < rootMoves.Length; i++)
                {
                    BitPosition nextPos = rootPos.Apply(in rootMoves[i]);
                    _tt?.Prefetch(nextPos.Hash);
                    int score = -NegaMax(in nextPos, d - 1, -beta, -alpha, ply: 1, out bool aborted);
                    if (aborted)
                    {
                        return;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }

                    if (score > alpha)
                    {
                        alpha = score;
                    }
                }

                if (bestIndex > 0)
                {
                    BitMove best = rootMoves[bestIndex];
                    Array.Copy(rootMoves, 0, rootMoves, 1, bestIndex);
                    rootMoves[0] = best;
                }
            }
        }
        catch (Exception)
        {
            // A helper only feeds the transposition table; whatever goes wrong, the main thread's result stands.
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int EvaluatePosition(in BitPosition pos) =>
        _customEvaluator != null
            ? _customEvaluator.Evaluate(in pos)
            : EvaluationFunction.Evaluate(in pos, _variant);

    /// <summary>
    /// The 4096-node poll. Helpers check the stop request; the main thread checks cancellation, move-now
    /// and the hard time limit, and sends the progress heartbeat.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool PollStop(bool reportProgress)
    {
        if (_stop != null)
        {
            return _stop.Requested;
        }

        _cancellationToken.ThrowIfCancellationRequested();
        if (_moveNowToken.IsCancellationRequested || _sw.ElapsedMilliseconds >= _hardLimitMs)
        {
            return true;
        }

        if (reportProgress && _progress != null && _sw.ElapsedMilliseconds - LastReportMs >= 100)
        {
            LastReportMs = _sw.ElapsedMilliseconds;
            long nodes = _owner.NodesEvaluated;
            _progress.Report(new SearchAnalysis
            {
                Move = CurrentRootMove?.Notation ?? BestRootMoveOverall?.Notation ?? "-",
                Depth = $"{CurrentIterDepth} plies...",
                Score = MinimaxPlayer.FormatValue(BestScoreOverall),
                BestMove = BestRootMoveOverall?.Notation ?? "-",
                Nodes = nodes.ToString("N0", CultureInfo.InvariantCulture),
                Speed = MinimaxPlayer.FormatSpeed(nodes, _sw.ElapsedMilliseconds),
                Time = _sw.Elapsed.ToString(@"m\:ss\.f")
            });
        }

        return false;
    }

    internal int NegaMax(
        in BitPosition pos,
        int depth,
        int alpha,
        int beta,
        int ply,
        out bool aborted)
    {
        aborted = false;

        if ((Nodes & 4095) == 0 && PollStop(reportProgress: true))
        {
            aborted = true;
            return 0;
        }

        Nodes++;

        // Fast terminal status check matching RuleEngine.EvaluateGameStatus.
        // At depth > 0 with HalfMoveClock < 80, BitboardMoveGenerator.Generate below will catch 0 legal moves,
        // avoiding a redundant HasAnyLegalMove call on every interior node.
        ulong ownPieces = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
        if (ownPieces == 0UL || ((depth == 0 || pos.HalfMoveClock >= 80) && !BitboardMoveGenerator.HasAnyLegalMove(in pos, _variant)))
        {
            return LossScore + ply;
        }
        if (pos.HalfMoveClock >= 80)
        {
            return 0;
        }

        // In-search repetition check BEFORE probing the Transposition Table so cached
        // static/subtree scores never mask a repetition draw along the active search line.
        if (_drawTable.IsRepetition(in pos, ply))
        {
            return 0;
        }

        int originalAlpha = alpha;
        bool hasTtMove = false;
        ushort ttPackedMove = 0;
        int cachedStaticEval = TranspositionEntry.NoStaticEval;

        // 4-Way Bucket Transposition Table Probe
        if (_tt != null &&
            _tt.TryProbe(
                pos.Hash,
                depth,
                alpha,
                beta,
                ply,
                out int ttScore,
                out ttPackedMove,
                out hasTtMove,
                out int probedStaticEval,
                out bool hasStaticEval,
                out bool hasCutoff))
        {
            if (hasCutoff)
            {
                return ttScore;
            }
            if (hasStaticEval)
            {
                cachedStaticEval = probedStaticEval;
            }
        }

        // Quiescence search at leaf nodes
        if (depth == 0)
        {
            if (_useQuiescence)
            {
                return Quiescence(in pos, alpha, beta, ply, legalMovesVerified: true, cachedStaticEval, out aborted);
            }
            if (cachedStaticEval != TranspositionEntry.NoStaticEval)
            {
                return cachedStaticEval;
            }
            Leaves++;
            return EvaluatePosition(in pos);
        }

        Span<BitMove> moveBuffer = ply < MaxPly ? _moveBuffers[ply] : stackalloc BitMove[MaxMovesPerNode];
        int moveCount = BitboardMoveGenerator.Generate(in pos, _variant, moveBuffer);
        if (moveCount == 0)
        {
            return LossScore + ply;
        }

        Span<BitMove> moves = moveBuffer.Slice(0, moveCount);
        bool isCaptureNode = moves[0].IsCapture;
        bool isNullWindow = beta <= alpha + 1;
        bool futilityPrune = false;

        // Stage B Selective Pruning (RFP & FP setup) at non-PV quiet nodes
        if (_useSelectivePruning && !isCaptureNode && isNullWindow && depth <= RfpMaxDepth)
        {
            if (cachedStaticEval == TranspositionEntry.NoStaticEval)
            {
                Leaves++;
                cachedStaticEval = EvaluatePosition(in pos);
            }

            // 4.2 Reverse Futility Pruning (RFP): if static eval is far above beta at a quiet non-PV node, return immediately
            if (beta < TranspositionTable.WinThreshold &&
                beta > -TranspositionTable.WinThreshold &&
                cachedStaticEval - (RfpMargin * depth) >= beta)
            {
                BitPosition oppPos = pos;
                oppPos.SideToMove = pos.SideToMove == PieceColor.White ? PieceColor.Black : PieceColor.White;
                if (!BitboardMoveGenerator.HasAnyCapture(in oppPos, _variant))
                {
                    return cachedStaticEval;
                }
            }

            // 4.3 Futility Pruning (FP) flag for shallow non-PV quiet nodes far below alpha
            if (depth <= FutilityMaxDepth &&
                alpha > -TranspositionTable.WinThreshold &&
                alpha < TranspositionTable.WinThreshold &&
                cachedStaticEval + (FutilityMargin * depth) <= alpha)
            {
                futilityPrune = true;
            }
        }

        OrderBitMoves(moves, ttPackedMove, hasTtMove, ply, pos.SideToMove);
        _drawTable.Record(ply, pos.Hash);

        BitMove bestMoveThisNode = default;
        bool hasBestMove = false;
        int maxScore = int.MinValue;

        for (int i = 0; i < moves.Length; i++)
        {
            BitPosition nextPos = pos.Apply(in moves[i]);
            bool isQuietMove = !isCaptureNode && !moves[i].IsPromotion;
            bool oppHasCaptureAfterMove = false;
            bool oppCaptureChecked = false;

            // 4.3 Futility Pruning (FP): skip quiet non-promoting moves after move 0 that do not leave a tactical capture
            if (futilityPrune && i > 0 && isQuietMove)
            {
                oppHasCaptureAfterMove = BitboardMoveGenerator.HasAnyCapture(in nextPos, _variant);
                oppCaptureChecked = true;
                if (!oppHasCaptureAfterMove)
                {
                    continue;
                }
            }

            _tt?.Prefetch(nextPos.Hash);

            int score;
            if (i == 0)
            {
                score = -NegaMax(in nextPos, depth - 1, -beta, -alpha, ply + 1, out aborted);
            }
            else
            {
                // 4.1 Verified Late Move Reductions (LMR)
                int reduction = 0;
                if (_useSelectivePruning && isQuietMove && depth >= LmrMinDepth && ply >= LmrMinPly && i >= LmrMinMoveIndex)
                {
                    if (!oppCaptureChecked)
                    {
                        oppHasCaptureAfterMove = BitboardMoveGenerator.HasAnyCapture(in nextPos, _variant);
                    }
                    if (!oppHasCaptureAfterMove)
                    {
                        reduction = 1 + (i >= LmrLateMoveIndex ? 1 : 0);
                        if (reduction > depth - 2)
                        {
                            reduction = depth - 2;
                        }
                    }
                }

                // Principal Variation Search (PVS): test subsequent moves with a zero-width window first (at reduced depth if LMR applies)
                score = -NegaMax(in nextPos, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, out aborted);

                // Verified LMR re-search: if reduced search beat alpha, verify at full depth (depth - 1) on the null window
                if (!aborted && reduction > 0 && score > alpha)
                {
                    score = -NegaMax(in nextPos, depth - 1, -alpha - 1, -alpha, ply + 1, out aborted);
                }

                // PVS full-window re-search: if null-window search beat alpha inside a PV node, re-search with full window (-beta, -alpha)
                if (!aborted && !isNullWindow && score > alpha && score < beta)
                {
                    score = -NegaMax(in nextPos, depth - 1, -beta, -alpha, ply + 1, out aborted);
                }
            }

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
                if (!moves[i].IsCapture && !moves[i].IsPromotion)
                {
                    RecordKillerAndHistory(in moves[i], ply, depth, pos.SideToMove);
                }
                break;
            }
        }

        // Transposition Table Store
        if (_tt != null && !aborted)
        {
            TranspositionBound bound;
            if (maxScore <= originalAlpha)
                bound = TranspositionBound.UpperBound;
            else if (maxScore >= beta)
                bound = TranspositionBound.LowerBound;
            else
                bound = TranspositionBound.Exact;

            _tt.Store(
                pos.Hash,
                depth,
                maxScore,
                bound,
                ply,
                hasBestMove ? bestMoveThisNode.PackedMove : (ushort)0,
                hasBestMove,
                cachedStaticEval);
        }

        return maxScore;
    }

    private int Quiescence(
        in BitPosition pos,
        int alpha,
        int beta,
        int ply,
        bool legalMovesVerified,
        int cachedStaticEval,
        out bool aborted)
    {
        aborted = false;

        if ((Nodes & 4095) == 0 && PollStop(reportProgress: false))
        {
            aborted = true;
            return 0;
        }

        Nodes++;

        if (!legalMovesVerified)
        {
            ulong ownPieces = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
            if (ownPieces == 0UL || !BitboardMoveGenerator.HasAnyLegalMove(in pos, _variant))
            {
                return LossScore + ply;
            }
            if (pos.HalfMoveClock >= 80)
            {
                return 0;
            }
        }

        int standPat;
        if (cachedStaticEval != TranspositionEntry.NoStaticEval)
        {
            standPat = cachedStaticEval;
        }
        else
        {
            standPat = EvaluatePosition(in pos);
            Leaves++;
        }

        if (standPat >= beta)
        {
            return beta;
        }

        if (standPat > alpha)
        {
            alpha = standPat;
        }

        if (ply >= 24 || !BitboardMoveGenerator.HasAnyCapture(in pos, _variant))
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
        OrderCaptureMoves(captures);

        for (int i = 0; i < captures.Length; i++)
        {
            BitPosition nextPos = pos.Apply(in captures[i]);
            int score = -Quiescence(in nextPos, -beta, -alpha, ply + 1, legalMovesVerified: false, TranspositionEntry.NoStaticEval, out aborted);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordKillerAndHistory(in BitMove move, int ply, int depth, PieceColor sideToMove)
    {
        if ((uint)ply < MaxPly)
        {
            ushort packed = move.PackedMove;
            int killerIdx = ply << 1;
            if (_killers[killerIdx] != packed)
            {
                _killers[killerIdx + 1] = _killers[killerIdx];
                _killers[killerIdx] = packed;
            }
        }

        int histIdx = ((int)sideToMove << 12) | (move.From << 6) | move.To;
        int updated = _history[histIdx] + (depth * depth);
        _history[histIdx] = updated > 70_000 ? 70_000 : updated;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OrderBitMoves(
        Span<BitMove> moves,
        ushort ttPackedMove,
        bool hasTtMove,
        int ply,
        PieceColor sideToMove)
    {
        int count = moves.Length;
        if (count <= 1)
            return;

        Span<int> scores = stackalloc int[count];
        ushort killer1 = 0;
        ushort killer2 = 0;
        if ((uint)ply < MaxPly)
        {
            int killerIdx = ply << 1;
            killer1 = _killers[killerIdx];
            killer2 = _killers[killerIdx + 1];
        }
        int sideOffset = (int)sideToMove << 12;

        for (int i = 0; i < count; i++)
        {
            ref readonly BitMove m = ref moves[i];
            ushort packed = m.PackedMove;
            int score;
            if (hasTtMove && packed == ttPackedMove)
            {
                score = 10_000_000;
            }
            else if (m.IsCapture)
            {
                score = 1_000_000 + (m.CaptureCount * 10_000) + (m.IsPromotion ? 1_000 : 0);
            }
            else if (m.IsPromotion)
            {
                score = 500_000;
            }
            else if (packed == killer1)
            {
                score = 90_000;
            }
            else if (packed == killer2)
            {
                score = 80_000;
            }
            else
            {
                score = _history[sideOffset | (m.From << 6) | m.To];
            }
            scores[i] = score;
        }

        for (int i = 1; i < count; i++)
        {
            int currentScore = scores[i];
            if (currentScore == 0)
                continue;

            BitMove currentMove = moves[i];
            int j = i - 1;
            while (j >= 0 && scores[j] < currentScore)
            {
                moves[j + 1] = moves[j];
                scores[j + 1] = scores[j];
                j--;
            }
            moves[j + 1] = currentMove;
            scores[j + 1] = currentScore;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OrderCaptureMoves(Span<BitMove> captures)
    {
        for (int i = 1; i < captures.Length; i++)
        {
            BitMove current = captures[i];
            int currentKey = (current.CaptureCount << 1) | (current.IsPromotion ? 1 : 0);
            int j = i - 1;
            while (j >= 0)
            {
                int prevKey = (captures[j].CaptureCount << 1) | (captures[j].IsPromotion ? 1 : 0);
                if (prevKey >= currentKey)
                    break;
                captures[j + 1] = captures[j];
                j--;
            }
            captures[j + 1] = current;
        }
    }
}
