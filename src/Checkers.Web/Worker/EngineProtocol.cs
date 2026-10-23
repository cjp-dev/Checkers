using System.Text.Json;
using System.Text.Json.Serialization;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.Core.AI;
using Checkers.Core.Bitboards;
using Checkers.Core.Models;

namespace Checkers.Web.Worker;

internal sealed record MoveRequest(
    ulong WhiteMen,
    ulong BlackMen,
    ulong WhiteKings,
    ulong BlackKings,
    ulong Hash,
    int HalfMoveClock,
    int SideToMove,
    int FullMoveNumber,
    CheckersVariant Variant,
    TimeControlMode Mode,
    int Depth,
    long TimeTicks,
    bool UseOpeningBook,
    int TranspositionTableEntries,
    ulong[] StateHashHistory);

internal sealed record ProgressDto(
    string Move,
    string Depth,
    string Score,
    string BestMove,
    string Nodes,
    string Speed,
    string Time,
    bool FromBook);

internal sealed record MoveResponse(
    string MoveNotation,
    ProgressDto? LastAnalysis);

[JsonSerializable(typeof(MoveRequest))]
[JsonSerializable(typeof(ProgressDto))]
[JsonSerializable(typeof(MoveResponse))]
[JsonSerializable(typeof(AppPreferences))]
internal sealed partial class EngineProtocolJson : JsonSerializerContext;

internal static class EngineProtocol
{
    public static string WriteRequest(
        BoardState state,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory)
    {
        BitPosition bp = state.BitPosition;
        SearchLimits limits = settings.ToLimits(computerTimeLeft);
        var req = new MoveRequest(
            bp.WhiteMen,
            bp.BlackMen,
            bp.WhiteKings,
            bp.BlackKings,
            bp.Hash,
            bp.HalfMoveClock,
            (int)bp.SideToMove,
            state.FullMoveNumber,
            settings.Variant,
            limits.Mode,
            limits.Depth,
            limits.Time.Ticks,
            limits.UseOpeningBook,
            settings.TranspositionTableEntries,
            stateHashHistory.ToArray());
        return JsonSerializer.Serialize(req, EngineProtocolJson.Default.MoveRequest);
    }

    public static (BoardState State, CheckersVariant Variant, SearchLimits Limits, int TtEntries, ulong[] History) ReadRequest(string json)
    {
        MoveRequest req = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.MoveRequest)!;
        var bp = new BitPosition
        {
            WhiteMen = req.WhiteMen,
            BlackMen = req.BlackMen,
            WhiteKings = req.WhiteKings,
            BlackKings = req.BlackKings,
            Hash = req.Hash,
            HalfMoveClock = req.HalfMoveClock,
            SideToMove = (PieceColor)req.SideToMove
        };
        var state = new BoardState(bp, req.FullMoveNumber);
        TimeSpan time = TimeSpan.FromTicks(req.TimeTicks);
        SearchLimits limits = req.Mode switch
        {
            TimeControlMode.FixedDepth => SearchLimits.FixedDepth(req.Depth, req.UseOpeningBook),
            TimeControlMode.TimePerMove => SearchLimits.TimePerMove(time, req.UseOpeningBook),
            _ => SearchLimits.TimePerGame(time > TimeSpan.Zero ? time : TimeSpan.FromMilliseconds(10), req.UseOpeningBook)
        };
        return (state, req.Variant, limits, req.TranspositionTableEntries, req.StateHashHistory);
    }

    public static string WriteProgress(SearchAnalysis info) => JsonSerializer.Serialize(
        new ProgressDto(info.Move, info.Depth, info.Score, info.BestMove, info.Nodes, info.Speed, info.Time, info.FromBook),
        EngineProtocolJson.Default.ProgressDto);

    public static SearchAnalysis ReadProgress(string json)
    {
        ProgressDto dto = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.ProgressDto)!;
        return new SearchAnalysis
        {
            Move = dto.Move,
            Depth = dto.Depth,
            Score = dto.Score,
            BestMove = dto.BestMove,
            Nodes = dto.Nodes,
            Speed = dto.Speed,
            Time = dto.Time,
            FromBook = dto.FromBook
        };
    }

    public static string WriteResponse(Move move, SearchAnalysis? last) => JsonSerializer.Serialize(
        new MoveResponse(
            move.Notation,
            last is null ? null : new ProgressDto(last.Move, last.Depth, last.Score, last.BestMove, last.Nodes, last.Speed, last.Time, last.FromBook)),
        EngineProtocolJson.Default.MoveResponse);

    public static (Move Move, SearchAnalysis? LastAnalysis) ReadResponse(string json, IReadOnlyList<Move> legalMoves)
    {
        MoveResponse res = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.MoveResponse)!;
        Move chosen = legalMoves.FirstOrDefault(m => m.Notation == res.MoveNotation) ?? legalMoves[0];
        SearchAnalysis? last = res.LastAnalysis is { } dto
            ? new SearchAnalysis
            {
                Move = dto.Move,
                Depth = dto.Depth,
                Score = dto.Score,
                BestMove = dto.BestMove,
                Nodes = dto.Nodes,
                Speed = dto.Speed,
                Time = dto.Time,
                FromBook = dto.FromBook
            }
            : null;
        return (chosen, last);
    }
}

