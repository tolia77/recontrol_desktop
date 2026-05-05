using System;
using System.Collections.Generic;

namespace ReControl.Desktop.Services.Clipboard;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ClipboardLoopGate
{
    public const int RingCapacity = 8;
    public const int TtlMs = 2000;

    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly List<RingEntry> _recentApplied = new(RingCapacity);
    private byte[]? _lastSentHash;
    private DateTimeOffset? _lastSentAt;

    private sealed record RingEntry(byte[] Hash8, DateTimeOffset At);

    public ClipboardLoopGate(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool ShouldSuppressOutbound(ReadOnlySpan<byte> hash8)
    {
        ValidateHash(hash8);
        lock (_gate)
        {
            if (_lastSentHash is null || _lastSentAt is null)
                return false;
            if (IsExpired(_lastSentAt.Value))
                return false;
            return hash8.SequenceEqual(_lastSentHash);
        }
    }

    public void RecordSent(ReadOnlySpan<byte> hash8)
    {
        ValidateHash(hash8);
        lock (_gate)
        {
            _lastSentHash = hash8.ToArray();
            _lastSentAt = _clock.UtcNow;
        }
    }

    public bool ShouldSuppressInbound(ReadOnlySpan<byte> hash8)
    {
        ValidateHash(hash8);
        lock (_gate)
        {
            PruneExpired();
            foreach (var entry in _recentApplied)
            {
                if (hash8.SequenceEqual(entry.Hash8))
                    return true;
            }
            return false;
        }
    }

    public void RecordApplied(ReadOnlySpan<byte> hash8)
    {
        ValidateHash(hash8);
        lock (_gate)
        {
            PruneExpired();
            _recentApplied.Add(new RingEntry(hash8.ToArray(), _clock.UtcNow));
            if (_recentApplied.Count > RingCapacity)
                _recentApplied.RemoveAt(0);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastSentHash = null;
            _lastSentAt = null;
            _recentApplied.Clear();
        }
    }

    private bool IsExpired(DateTimeOffset at)
    {
        return (_clock.UtcNow - at).TotalMilliseconds > TtlMs;
    }

    private void PruneExpired()
    {
        var now = _clock.UtcNow;
        _recentApplied.RemoveAll(e => (now - e.At).TotalMilliseconds > TtlMs);
    }

    private static void ValidateHash(ReadOnlySpan<byte> hash8)
    {
        if (hash8.Length != 8)
            throw new ArgumentException("hash must be exactly 8 bytes", nameof(hash8));
    }
}
