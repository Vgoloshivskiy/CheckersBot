using System.Diagnostics.CodeAnalysis;

namespace Checkers.Application;

/// <summary>Thread-safe LRU cache whose entries also expire after a fixed time.</summary>
public sealed class LruTtlCache<TKey, TValue> where TKey : notnull
{
    private sealed record Entry(TKey Key, TValue Payload, DateTimeOffset ExpiresAt);

    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();   // most recently used first
    private readonly object _gate = new();

    public LruTtlCache(int capacity, TimeSpan ttl, TimeProvider time)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _ttl = ttl;
        _time = time;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.ExpiresAt > _time.GetUtcNow())
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    value = node.Value.Payload;
                    return true;
                }

                _order.Remove(node);
                _map.Remove(key);
            }

            value = default;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value, _time.GetUtcNow() + _ttl));
            _order.AddFirst(node);
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
