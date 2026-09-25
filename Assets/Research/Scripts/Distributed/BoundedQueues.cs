using System.Collections.Concurrent;
using System.Threading;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed class LatestFirstBoundedQueue<T>
    {
        readonly ConcurrentQueue<T> m_Queue = new ConcurrentQueue<T>(); readonly int m_Capacity;
        int m_Count; long m_Dropped;
        public LatestFirstBoundedQueue(int capacity) { m_Capacity = capacity < 1 ? 1 : capacity; }
        public int Count => Volatile.Read(ref m_Count); public long Dropped => Interlocked.Read(ref m_Dropped);
        public void Enqueue(T item) { m_Queue.Enqueue(item); Interlocked.Increment(ref m_Count); while (Count > m_Capacity && m_Queue.TryDequeue(out _)) { Interlocked.Decrement(ref m_Count); Interlocked.Increment(ref m_Dropped); } }
        public bool TryDequeue(out T item) { if (!m_Queue.TryDequeue(out item)) return false; Interlocked.Decrement(ref m_Count); return true; }
        public void Clear() { while (TryDequeue(out _)) { } }
    }
    public sealed class RejectingBoundedQueue<T>
    {
        readonly ConcurrentQueue<T> m_Queue = new ConcurrentQueue<T>(); readonly int m_Capacity;
        int m_Count; long m_Rejected;
        public RejectingBoundedQueue(int capacity) { m_Capacity = capacity < 1 ? 1 : capacity; }
        public int Count => Volatile.Read(ref m_Count); public long Rejected => Interlocked.Read(ref m_Rejected);
        public bool TryEnqueue(T item) { if (Interlocked.Increment(ref m_Count) > m_Capacity) { Interlocked.Decrement(ref m_Count); Interlocked.Increment(ref m_Rejected); return false; } m_Queue.Enqueue(item); return true; }
        public bool TryDequeue(out T item) { if (!m_Queue.TryDequeue(out item)) return false; Interlocked.Decrement(ref m_Count); return true; }
        public void Clear() { while (TryDequeue(out _)) { } }
    }
}
