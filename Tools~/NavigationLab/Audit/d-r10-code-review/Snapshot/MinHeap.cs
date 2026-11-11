using System.Collections.Generic;

namespace NavigationLab.DAoe4
{
    internal sealed class MinHeap
    {
        private readonly List<Entry> entries = new List<Entry>();
        private long serial;

        private struct Entry
        {
            internal int Cell;
            internal double Priority;
            internal long Serial;
        }

        internal void Push(int cell, double priority)
        {
            var entry = new Entry { Cell = cell, Priority = priority, Serial = serial++ };
            entries.Add(entry);
            int child = entries.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (!Before(entry, entries[parent]))
                {
                    break;
                }
                entries[child] = entries[parent];
                child = parent;
            }
            entries[child] = entry;
        }

        internal bool TryPop(out int cell, out double priority)
        {
            if (entries.Count == 0)
            {
                cell = -1;
                priority = 0;
                return false;
            }
            var first = entries[0];
            var last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            int parent = 0;
            while (parent * 2 + 1 < entries.Count)
            {
                int child = parent * 2 + 1;
                if (child + 1 < entries.Count && Before(entries[child + 1], entries[child]))
                {
                    child++;
                }
                if (!Before(entries[child], last))
                {
                    break;
                }
                entries[parent] = entries[child];
                parent = child;
            }
            if (entries.Count > 0)
            {
                entries[parent] = last;
            }
            cell = first.Cell;
            priority = first.Priority;
            return true;
        }

        private static bool Before(Entry a, Entry b)
        {
            return a.Priority < b.Priority || (a.Priority == b.Priority && a.Serial < b.Serial);
        }
    }
}
