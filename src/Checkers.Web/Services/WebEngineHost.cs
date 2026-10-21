using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.Core.AI;
using Checkers.Core.Models;
using Checkers.Web.Worker;
using Microsoft.JSInterop;

namespace Checkers.Web.Services;

public sealed class WebEngineHost : IEngineHost, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly LocalEngineHost _fallbackHost = new();
    private IJSObjectReference? _clientModule;
    private DotNetObjectReference<WebEngineHost>? _selfRef;
    private IProgress<SearchAnalysis>? _activeProgress;
    private SearchAnalysis? _lastAnalysis;
    private bool _useFallback;
    private bool _isStarted;

    public WebEngineHost(IJSRuntime js)
    {
        _js = js;
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

    private async ValueTask<IJSObjectReference?> EnsureClientAsync()
    {
        if (_useFallback)
        {
            return null;
        }

        if (_clientModule == null)
        {
            try
            {
                _clientModule = await _js.InvokeAsync<IJSObjectReference>("import", "./js/engine-client.js");
                _selfRef = DotNetObjectReference.Create(this);
            }
            catch
            {
                _useFallback = true;
                return null;
            }
        }

        if (!_isStarted)
        {
            try
            {
                await _clientModule.InvokeVoidAsync("start", _selfRef);
                _isStarted = true;
            }
            catch
            {
                _useFallback = true;
                return null;
            }
        }

        return _clientModule;
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
        var client = await EnsureClientAsync();
        if (client == null || _useFallback)
        {
            return await _fallbackHost.ChooseMoveAsync(
                state, legalMoves, settings, computerTimeLeft, stateHashHistory, progress, cancellationToken, moveNowToken);
        }

        _activeProgress = progress;
        _lastAnalysis = null;

        string requestJson = EngineProtocol.WriteRequest(state, settings, computerTimeLeft, stateHashHistory);

        var searchTask = client.InvokeAsync<string>("call", "chooseMove", requestJson).AsTask();

        using var moveNowReg = moveNowToken.Register(async () =>
        {
            try
            {
                bool signaled = await client.InvokeAsync<bool>("signal", 1);
                if (!signaled)
                {
                    await RestartAsync(client);
                }
            }
            catch
            {
                // Ignore signaling failure
            }
        });

        using var cancelReg = cancellationToken.Register(async () =>
        {
            try
            {
                bool signaled = await client.InvokeAsync<bool>("signal", 2);
                if (!signaled)
                {
                    await RestartAsync(client);
                }
            }
            catch
            {
                // Ignore signaling failure
            }
        });

        try
        {
            var completed = await Task.WhenAny(searchTask, Task.Delay(Timeout.Infinite, cancellationToken));
            if (completed != searchTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            string responseJson = await searchTask;
            return EngineProtocol.ReadResponse(responseJson, legalMoves);
        }
        catch (OperationCanceledException)
        {
            await RestartAsync(client);
            throw;
        }
        catch (Exception) when (moveNowToken.IsCancellationRequested)
        {
            await RestartAsync(client);
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

    private async Task RestartAsync(IJSObjectReference client)
    {
        try
        {
            await client.InvokeVoidAsync("terminate");
            _isStarted = false;
            await client.InvokeVoidAsync("start", _selfRef);
            _isStarted = true;
        }
        catch
        {
            _useFallback = true;
        }
    }

    public void ClearTranspositionTable()
    {
        _fallbackHost.ClearTranspositionTable();
        if (_clientModule != null && _isStarted && !_useFallback)
        {
            _ = _clientModule.InvokeVoidAsync("call", "clearTt");
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

