using System.Collections.Generic;

/// <summary>
/// 区块加载优先级队列（最小堆实现 + 字典去重）。
///
/// 设计动机：
/// - Unity 2022.3 + C# 9 环境下没有 <c>System.Collections.Generic.PriorityQueue</c>（.NET 6+ 才有），
///   而 SortedSet/SortedDictionary 都不允许相同 priority 共存，且 Remove 复杂度差
/// - R=33 时单帧入队上限 ~3421 条，需要 O(log N) 入队 + O(log N) 出队，最小堆是最朴素也最稳定的选择
///
/// 不做的事：
/// - 不做线程安全（仅在主线程 LateUpdate 用）
/// - 不做"修改优先级"的 decrease-key（玩家移动时整体重建即可）
/// </summary>
public sealed class ChunkLoadQueue
{
    private readonly struct Entry
    {
        public readonly ChunkCoord Coord;
        public readonly float Priority; // 越小越优先

        public Entry(ChunkCoord coord, float priority)
        {
            Coord = coord;
            Priority = priority;
        }
    }

    private readonly List<Entry> _heap = new List<Entry>(256);

    /// <summary>已在堆中的坐标集合，避免重复入队。同坐标更小优先级时不会替换（玩家移动一格的影响有限，等下次 Refresh 整体重建）。</summary>
    private readonly HashSet<ChunkCoord> _members = new HashSet<ChunkCoord>();

    public int Count => _heap.Count;

    public void Clear()
    {
        _heap.Clear();
        _members.Clear();
    }

    public bool Contains(ChunkCoord coord) => _members.Contains(coord);

    /// <summary>入队。已存在则跳过（不替换优先级）。</summary>
    public void Enqueue(ChunkCoord coord, float priority)
    {
        if (!_members.Add(coord))
        {
            return;
        }

        _heap.Add(new Entry(coord, priority));
        SiftUp(_heap.Count - 1);
    }

    /// <summary>出队最小优先级元素。</summary>
    public bool TryDequeue(out ChunkCoord coord)
    {
        if (_heap.Count == 0)
        {
            coord = default;
            return false;
        }

        Entry top = _heap[0];
        coord = top.Coord;
        _members.Remove(top.Coord);

        int last = _heap.Count - 1;
        if (last > 0)
        {
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            SiftDown(0);
        }
        else
        {
            _heap.RemoveAt(last);
        }

        return true;
    }

    /// <summary>移除指定坐标。复杂度 O(N)（线性扫堆），仅在"目标集合 shrink"时调用——R=33 一次 Refresh 最多触发几十次，可接受。</summary>
    public bool Remove(ChunkCoord coord)
    {
        if (!_members.Remove(coord))
        {
            return false;
        }

        int idx = -1;
        for (int i = 0; i < _heap.Count; i++)
        {
            if (_heap[i].Coord.Equals(coord))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            return false;
        }

        int last = _heap.Count - 1;
        if (idx == last)
        {
            _heap.RemoveAt(last);
            return true;
        }

        _heap[idx] = _heap[last];
        _heap.RemoveAt(last);
        // 上下都试一次，因为新放进来的元素可能比父大也可能比父小
        SiftDown(idx);
        SiftUp(idx);
        return true;
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_heap[i].Priority >= _heap[parent].Priority)
            {
                break;
            }
            (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
            i = parent;
        }
    }

    private void SiftDown(int i)
    {
        int n = _heap.Count;
        while (true)
        {
            int l = (i << 1) + 1;
            int r = l + 1;
            int smallest = i;
            if (l < n && _heap[l].Priority < _heap[smallest].Priority) smallest = l;
            if (r < n && _heap[r].Priority < _heap[smallest].Priority) smallest = r;
            if (smallest == i)
            {
                break;
            }
            (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
            i = smallest;
        }
    }
}
