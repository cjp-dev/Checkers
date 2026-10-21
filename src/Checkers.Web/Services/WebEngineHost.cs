using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.Core.AI;
using Checkers.Core.Models;
using Checkers.Web.Worker;
using Microsoft.JSInterop;

namespace Checkers.Web.Services;

public sealed class WebEngineHost : IEngineHost, IAsyncDisposable
{
    private const int CooperativeGracePeriodMs = 300;

    private readonly IJSRuntime _js;
    private readonly LocalEngineHost _fallbackHost = new();
    private IJSObjectReference? _clientModule;
    private DotNetObjectReference<WebEngineHost>? _selfRef;
    private Task<IJSObjectReference?>? _startTask;
    private IProgress<SearchAnalysis>? _activeProgress;
    private SearchAnalysis? _lastAnalysis;
    private bool _useFallback;

    public WebEngineHost(IJSRuntime js)
    {
        _js = js;
        _startTask = StartAsync();
    }

    [JSInvokable]
    public void OnProgress(string json)
    {
        try
        {
            SearchAnalysis analysis = EngineProtocol.ReadProgress(json);
            _lastAnalysis = analysis;
            _activeProgress?.Report(analysis);
        }
        catch
        {
            // Ignore malformed progress packets
        }
    }

    private async Task<IJSObjectReference?> StartAsync()
    {
        if (_useFallback)
        {
            return null;
        }

        try
        {
            _clientModule ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/engine-client.js");
            _selfRef ??= DotNetObjectReference.Create(this);
            await _clientModule.InvokeVoidAsync("start", _selfRef);
            return _clientModule;
        }
        catch
        {
            _useFallback = true;
            return null;
        }
    }

    public async Task<(Move Move, SearchAnalysis? LastAnalysis)> ChooseMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken)
    {
        IJSObjectReference? client = await (_startTask ??= StartAsync());
        if (client is null || _useFallback)
        {
            return await _fallbackHost.ChooseMoveAsync(
                state, legalMoves, settings, computerTimeLeft, stateHashHistory, progress, cancellationToken, moveNowToken);
        }

        _activeProgress = progress;
        _lastAnalysis = null;

        string requestJson = EngineProtocol.WriteRequest(state, settings, computerTimeLeft, stateHashHistory);
        Task<string> searchTask = client.InvokeAsync<string>("call", "chooseMove", requestJson).AsTask();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, moveNowToken);
        var stopSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopReg = linkedCts.Token.Register(() => stopSignal.TrySetResult());

        try
        {
            Task firstCompleted = await Task.WhenAny(searchTask, stopSignal.Task);
            if (firstCompleted != searchTask)
            {
                int signalCode = cancellationToken.IsCancellationRequested ? 2 : 1;
                bool signaled = false;
                try
                {
                    signaled = await client.InvokeAsync<bool>("signal", signalCode);
                }
                catch
                {
                    // Ignore signaling failure
                }

                if (signaled)
                {
                    using var upgradeReg = cancellationToken.Register(() => _ = client.InvokeAsync<bool>("signal", 2));
                    await Task.WhenAny(searchTask, Task.Delay(CooperativeGracePeriodMs));
                }

                if (!searchTask.IsCompleted)
                {
                    Restart(client);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    Move fallbackMove = FindBestMoveFromAnalysis(legalMoves, _lastAnalysis);
                    return (fallbackMove, _lastAnalysis);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            string responseJson = await searchTask;
            return EngineProtocol.ReadResponse(responseJson, legalMoves);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (moveNowToken.IsCancellationRequested)
        {
            Move chosen = FindBestMoveFromAnalysis(legalMoves, _lastAnalysis);
            return (chosen, _lastAnalysis);
        }
        catch (Exception)
        {
            _useFallback = true;
            return await _fallbackHost.ChooseMoveAsync(
                state, legalMoves, settings, computerTimeLeft, stateHashHistory, progress, cancellationToken, moveNowToken);
        }
        finally
        {
            _activeProgress = null;
        }
    }

    private static Move FindBestMoveFromAnalysis(IReadOnlyList<Move> legalMoves, SearchAnalysis? analysis)
    {
        if (analysis != null && !string.IsNullOrWhiteSpace(analysis.BestMove))
        {
            var match = legalMoves.FirstOrDefault(m => m.Notation == analysis.BestMove);
            if (match != null) return match;
        }
        return legalMoves[0];
    }

    private void Restart(IJSObjectReference client)
    {
        _ = client.InvokeVoidAsync("terminate");
        _startTask = StartAsync();
    }

    public void ClearTranspositionTable()
    {
        _fallbackHost.ClearTranspositionTable();
        if (_clientModule != null && !_useFallback)
        {
            _ = ClearWorkerTtAsync();
        }
    }

    private async Task ClearWorkerTtAsync()
    {
        try
        {
            IJSObjectReference? client = await (_startTask ??= StartAsync());
            if (client != null)
            {
                await client.InvokeVoidAsync("call", "clearTt");
            }
        }
        catch
        {
            // Ignore if worker is restarting
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_clientModule != null)
        {
            try
            {
                await _clientModule.InvokeVoidAsync("terminate");
                await _clientModule.DisposeAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
            _clientModule = null;
        }
        _selfRef?.Dispose();
        _selfRef = null;
    }
}
