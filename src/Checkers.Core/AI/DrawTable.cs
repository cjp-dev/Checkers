using System.Runtime.CompilerServices;
using Checkers.Core.Bitboards;

namespace Checkers.Core.AI;

/// <summary>
/// Tracks 64-bit Zobrist position hashes along the active search path and from pre-search game history
/// to detect repetition draws (score = 0) inside the alpha-beta search tree before probing the Transposition Table.
/// </summary>
public sealed class DrawTable
{
    private const int MaxPathPly = 128;
    private const int MaxHistoryHashes = 128;

    private readonly ulong[] _pathHashes = new ulong[MaxPathPly];
    private readonly ulong[] _twoFoldHashes = new ulong[MaxHistoryHashes];
    private readonly ulong[] _oneFoldHashes = new ulong[MaxHistoryHashes];
    private int _twoFoldCount;
    private int _oneFoldCount;

    /// <summary>
    /// Resets the draw table for a new root search, recording the root position at ply 0
    /// and seeding prior game state hashes.
    /// </summary>
    public void Reset(in BitPosition rootPos, IReadOnlyList<ulong>? stateHashHistory = null)
    {
        _pathHashes[0] = rootPos.Hash;
        _twoFoldCount = 0;
        _oneFoldCount = 0;

        if (stateHashHistory == null || stateHashHistory.Count == 0)
            return;

        int historyCount = stateHashHistory.Count;

        // Any position that has already occurred >= 2 times in the game history will trigger
        // an immediate threefold repetition draw on its next occurrence (even at ply 1).
        for (int i = 0; i < historyCount; i++)
        {
            ulong h = stateHashHistory[i];
            int occurrences = 0;
            for (int j = 0; j <= i; j++)
            {
                if (stateHashHistory[j] == h)
                    occurrences++;
            }

            if (occurrences == 2 && _twoFoldCount < MaxHistoryHashes)
            {
                _twoFoldHashes[_twoFoldCount++] = h;
            }
        }

        // Seed single-occurrence positions from the recent reversible window so that
        // cycling back into a recent game state after both players move (ply >= 2) is detected.
        int reversibleWindow = rootPos.HalfMoveClock > 0
            ? Math.Min(historyCount, rootPos.HalfMoveClock + 1)
            : Math.Min(historyCount, 16);
        int startIdx = historyCount - reversibleWindow;

        for (int i = startIdx; i < historyCount; i++)
        {
            ulong h = stateHashHistory[i];
            if (h == rootPos.Hash)
                continue;

            bool alreadyAdded = false;
            for (int j = 0; j < _oneFoldCount; j++)
            {
                if (_oneFoldHashes[j] == h)
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded && _oneFoldCount < MaxHistoryHashes)
            {
                _oneFoldHashes[_oneFoldCount++] = h;
            }
        }
    }

    /// <summary>
    /// Records the Zobrist hash of the position at the given search ply along the active branch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(int ply, ulong hash)
    {
        if ((uint)ply < MaxPathPly)
        {
            _pathHashes[ply] = hash;
        }
    }

    /// <summary>
    /// Returns true if the position at <paramref name="ply"/> repeats an ancestor on the active
    /// search path or completes a repetition from the seeded game history.
    /// Must be checked at <c>ply &gt; 0</c> BEFORE probing the Transposition Table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRepetition(in BitPosition pos, int ply)
    {
        ulong hash = pos.Hash;

        // 1. If this position already occurred twice in the game history, reaching it once
        // in the search (even at ply 1) completes a threefold repetition draw.
        for (int i = 0; i < _twoFoldCount; i++)
        {
            if (_twoFoldHashes[i] == hash)
                return true;
        }

        // Without kings on the board and without seeded history, no reversible cycle can exist.
        if (pos.Kings == 0UL && _oneFoldCount == 0)
            return false;

        // 2. Check active search path ancestors of the same side to move since the last capture/promotion.
        if (ply >= 4 && pos.HalfMoveClock >= 4)
        {
            int maxSearchIndex = Math.Min(ply, MaxPathPly - 1);
            int minPly = ply - pos.HalfMoveClock;
            if (minPly < 0)
                minPly = 0;

            for (int p = maxSearchIndex - 4; p >= minPly; p -= 2)
            {
                if (_pathHashes[p] == hash)
                    return true;
            }
        }

        // 3. Check 1-fold recent game history if at least 2 reversible plies have been played from the root.
        if (_oneFoldCount > 0 && ply >= 2 && pos.HalfMoveClock >= ply)
        {
            for (int i = 0; i < _oneFoldCount; i++)
            {
                if (_oneFoldHashes[i] == hash)
                    return true;
            }
        }

        return false;
    }
}
