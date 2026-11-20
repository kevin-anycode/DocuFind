using System.Numerics;

namespace DocuFind.Core.Index;

/// <summary>
/// 内存向量索引：单机文档量下（十万级切片）暴力余弦 + SIMD 只要几毫秒，
/// 换来零原生依赖、零召回损失。向量都已归一化，点积即余弦。
/// </summary>
public sealed class VectorIndex
{
    private float[] _data = Array.Empty<float>();
    private long[] _ids = Array.Empty<long>();
    private int _count;
    private readonly HashSet<long> _dead = new();

    public int Dimensions { get; private set; }
    public int Count => _count;

    public void Load(long[] ids, float[] data, int dimensions)
    {
        _ids = ids;
        _data = data;
        Dimensions = dimensions;
        _count = ids.Length;
        _dead.Clear();
    }

    public void Reset()
    {
        _ids = Array.Empty<long>();
        _data = Array.Empty<float>();
        _count = 0;
        _dead.Clear();
    }

    /// <summary>文档被覆盖/删除时，把它的向量标记为失效（避免整体重建索引）。</summary>
    public void MarkDead(IEnumerable<long> ids)
    {
        foreach (var id in ids) _dead.Add(id);
    }

    public void Add(long id, float[] vector)
    {
        if (Dimensions == 0) Dimensions = vector.Length;
        if (vector.Length != Dimensions) return;

        if (_count == _ids.Length)
        {
            var capacity = Math.Max(1024, _ids.Length * 2);
            Array.Resize(ref _ids, capacity);
            Array.Resize(ref _data, capacity * Dimensions);
        }

        _ids[_count] = id;
        vector.CopyTo(_data, _count * Dimensions);
        _count++;
    }

    public List<(long Id, float Score)> TopK(float[] query, int k)
    {
        var results = new List<(long, float)>(k);
        if (_count == 0 || Dimensions == 0 || query.Length != Dimensions) return results;

        var dim = Dimensions;
        var data = _data;
        var ids = _ids;

        // 小顶堆：堆顶是当前 Top-K 里分数最低的
        var heap = new PriorityQueue<int, float>(k + 1);

        var hasDead = _dead.Count > 0;

        for (var i = 0; i < _count; i++)
        {
            if (hasDead && _dead.Contains(ids[i])) continue;

            var score = Dot(data.AsSpan(i * dim, dim), query);
            if (heap.Count < k)
            {
                heap.Enqueue(i, score);
            }
            else if (score > heap.PeekPriority())
            {
                heap.Dequeue();
                heap.Enqueue(i, score);
            }
        }

        while (heap.TryDequeue(out var index, out var score))
            results.Add((ids[index], score));

        results.Reverse();
        return results;
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var width = Vector<float>.Count;
        var acc = Vector<float>.Zero;
        var i = 0;

        for (; i <= a.Length - width; i += width)
            acc += new Vector<float>(a.Slice(i, width)) * new Vector<float>(b.Slice(i, width));

        var sum = Vector.Dot(acc, Vector<float>.One);
        for (; i < a.Length; i++) sum += a[i] * b[i];

        return sum;
    }
}

internal static class PriorityQueueExtensions
{
    public static TPriority PeekPriority<TElement, TPriority>(this PriorityQueue<TElement, TPriority> queue) =>
        queue.TryPeek(out _, out var priority) ? priority : default!;
}
