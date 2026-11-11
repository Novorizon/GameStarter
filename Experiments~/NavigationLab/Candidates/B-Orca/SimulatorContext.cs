using System;
using System.Linq;
using System.Reflection;
using RVO;

namespace NavigationLab.Candidates.BOrca
{
    /// <summary>
    /// The pinned upstream Agent/KdTree refer directly to Simulator.Instance. A complete state swap under a shared lock
    /// isolates interleaved battles without editing any upstream source. This deliberately serializes ORCA operations.
    /// </summary>
    internal sealed class SimulatorContext
    {
        private static readonly object Gate = new object();
        private static readonly FieldInfo[] Fields = typeof(Simulator).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
        private object?[] state;

        internal SimulatorContext()
        {
            lock (Gate)
            {
                var simulator = Simulator.Instance;
                var previous = Capture(simulator);
                try
                {
                    simulator.Clear();
                    simulator.NumWorkers = 1;
                    state = Capture(simulator);
                }
                finally
                {
                    Restore(simulator, previous);
                }
            }
        }

        internal void Use(Action<Simulator> action)
        {
            Use(simulator =>
            {
                action(simulator);
                return 0;
            });
        }

        internal T Use<T>(Func<Simulator, T> action)
        {
            lock (Gate)
            {
                var simulator = Simulator.Instance;
                var previous = Capture(simulator);
                Restore(simulator, state);
                try
                {
                    return action(simulator);
                }
                finally
                {
                    state = Capture(simulator);
                    Restore(simulator, previous);
                }
            }
        }

        private static object?[] Capture(Simulator simulator)
        {
            return Fields.Select(field => field.GetValue(simulator)).ToArray();
        }

        private static void Restore(Simulator simulator, object?[] values)
        {
            for (int index = 0; index < Fields.Length; index++)
            {
                Fields[index].SetValue(simulator, values[index]);
            }
        }
    }
}
