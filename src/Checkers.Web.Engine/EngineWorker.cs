using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Checkers.Core.AI;
using Checkers.Core.AI.Book;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using Checkers.Web.Worker;

namespace Checkers.Web.Engine;

/// <summary>
/// The multithreaded (Lazy SMP) engine, booted by engine-client.js on the page when it is cross-origin isolated.
/// </summary>
/// <remarks>
/// Every export is async: the runtime's JS thread must never block, so the search runs inside Task.Run and
/// search threads never call into JavaScript. Stop requests set the tokens directly, and progress is queued
/// for JS to drain.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class EngineWorker
{
    private const int DefaultAutoThreads = 4;
    private const int MaxThreads = 8;

    private static readonly SemaphoreSlim s_searchLock = new(1, 1);
    private static TranspositionTable? s_tt;
    private static CheckersVariant s_lastVariant = CheckersVariant.International;

    private static volatile int s_signal; // 0 = none, 1 = moveNow, 2 = cancel
    private static volatile CancellationTokenSource? s_activeCancelCts;
    private static volatile CancellationTokenSource? s_activeMoveNowCts;
    private static readonly ConcurrentQueue<string> s_progressQueue = new();

    [JSExport]
    public static Task InitAsync()
    {
        // Without this the thread pool injects threads slowly (hill climbing), delaying the first move.
        ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
        if (minWorkers < 12)
        {
            ThreadPool.SetMinThreads(12, Math.Max(minIo, 4));
        }

        _ = OpeningBook.GetDefault(CheckersVariant.International);
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task ClearTranspositionTableAsync()
    {
        s_tt?.Clear();
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task SignalAsync(int code)
    {
        s_signal = code;
        try
        {
            if (code == 2)
            {
                s_activeCancelCts?.Cancel();
            }
            else if (code == 1)
            {
                s_activeMoveNowCts?.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // The search already finished and disposed its token source.
        }
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task<string?> DrainProgressAsync()
    {
        if (!s_progressQueue.TryDequeue(out string? first))
        {
            return Task.FromResult<string?>(null);
        }

        var sb = new StringBuilder(first);
        while (s_progressQueue.TryDequeue(out string? next))
        {
            sb.Append('\n').Append(next);
        }
        return Task.FromResult<string?>(sb.ToString());
    }

    [JSExport]
    public static async Task<string> ChooseMoveAsync(string requestJson)
    {
        // Stop a lingering previous search before taking the search lock.
        try { s_activeCancelCts?.Cancel(); } catch (ObjectDisposedException) { }

        await s_searchLock.WaitAsync();
        try
        {
            s_signal = 0;
            while (s_progressQueue.TryDequeue(out _)) { }

            (BoardState state, CheckersVariant variant, SearchLimits limits, int ttEntries, ulong[] history, int threadCount) =
                EngineProtocol.ReadRequest(requestJson);

            int threads = threadCount > 0
                ? Math.Clamp(threadCount, 1, MaxThreads)
                : Math.Clamp(Environment.ProcessorCount, 1, DefaultAutoThreads);

            if (s_tt is null || s_tt.Capacity != ttEntries)
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
            s_activeCancelCts = cancelCts;
            s_activeMoveNowCts = moveNowCts;

            // A signal may have arrived just before the tokens were armed.
            int earlySignal = s_signal;
            if (earlySignal == 2) cancelCts.Cancel();
            else if (earlySignal == 1) moveNowCts.Cancel();

            var ai = new MinimaxPlayer(
                limits,
                ruleEngine: ruleEngine,
                evaluator: new EvaluationFunction(variant),
                transpositionTable: s_tt)
            {
                Threads = threads
            };
            var poster = new QueuedProgressPoster();

            try
            {
                Move move = await Task.Run(() =>
                    ai.GetMoveAsync(state, legalMoves, poster, history, cancelCts.Token, moveNowCts.Token).AsTask());
                return EngineProtocol.WriteResponse(move, ai.LastAnalysis);
            }
            catch (OperationCanceledException)
            {
                // Cancelled: answer anyway so the runtime and its transposition table stay alive.
                return EngineProtocol.WriteResponse(legalMoves[0], ai.LastAnalysis);
            }
            finally
            {
                s_activeCancelCts = null;
                s_activeMoveNowCts = null;
            }
        }
        finally
        {
            s_searchLock.Release();
        }
    }

    private sealed class QueuedProgressPoster : IProgress<SearchAnalysis>
    {
        private const long IntervalMs = 100;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastPostMs = -IntervalMs;
        private string _lastDepth = "";
        private string _lastBest = "";

        public void Report(SearchAnalysis info)
        {
            long now = _clock.ElapsedMilliseconds;
            if (info.Depth == _lastDepth && info.BestMove == _lastBest && now - _lastPostMs < IntervalMs)
            {
                return;
            }

            _lastDepth = info.Depth;
            _lastBest = info.BestMove;
            _lastPostMs = now;
            s_progressQueue.Enqueue(EngineProtocol.WriteProgress(info));
        }
    }
}
