using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Checkers.Core.AI;
using Checkers.Core.AI.Book;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Web.Worker;

[SupportedOSPlatform("browser")]
public static partial class EngineWorker
{
    private static TranspositionTable s_tt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
    private static CheckersVariant s_lastVariant = CheckersVariant.International;

    [JSExport]
    public static void Init()
    {
        // Pre-warm default opening book on worker startup
        _ = OpeningBook.GetDefault(CheckersVariant.International);
    }

    [JSExport]
    public static void ClearTranspositionTable() => s_tt.Clear();

    [JSExport]
    public static string ChooseMove(string requestJson)
    {
        (BoardState state, CheckersVariant variant, SearchLimits limits, int ttEntries, ulong[] history) =
            EngineProtocol.ReadRequest(requestJson);

        if (s_tt.Capacity != ttEntries)
        {
            s_tt = TranspositionTable.FromEntries(ttEntries);
        }
        else if (s_lastVariant != variant)
        {
            s_tt.Clear();
        }
        s_lastVariant = variant;

        var ruleEngine = new RuleEngine(variant);
        IReadOnlyList<Move> legalMoves = ruleEngine.GetLegalMoves(state);

        using var cancelCts = new CancellationTokenSource();
        using var moveNowCts = new CancellationTokenSource();
        var poster = new ProgressPoster(cancelCts, moveNowCts);

        var ai = new MinimaxPlayer(
            limits,
            ruleEngine: ruleEngine,
            evaluator: new EvaluationFunction(variant),
            transpositionTable: s_tt);

        Move move = ai.GetMoveAsync(state, legalMoves, poster, history, cancelCts.Token, moveNowCts.Token)
            .GetAwaiter().GetResult();

        return EngineProtocol.WriteResponse(move, ai.LastAnalysis);
    }

    [JSImport("postProgress", "engine-worker")]
    private static partial void PostProgress(string json);

    [JSImport("pollSignal", "engine-worker")]
    private static partial int PollSignal();

    private sealed class ProgressPoster(CancellationTokenSource cancelCts, CancellationTokenSource moveNowCts) : IProgress<SearchAnalysis>
    {
        private const long IntervalMs = 100;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastPostMs = -IntervalMs;
        private string _lastDepth = "";
        private string _lastBest = "";

        public void Report(SearchAnalysis info)
        {
            int signal = PollSignal();
            if (signal == 2)
            {
                cancelCts.Cancel();
                return;
            }
            if (signal == 1)
            {
                moveNowCts.Cancel();
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            if (info.Depth == _lastDepth && info.BestMove == _lastBest && now - _lastPostMs < IntervalMs)
            {
                return;
            }

            _lastDepth = info.Depth;
            _lastBest = info.BestMove;
            _lastPostMs = now;
            PostProgress(EngineProtocol.WriteProgress(info));
        }
    }
}

