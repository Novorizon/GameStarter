using System;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        public int TrafficReplanAttempts { get; private set; }
        public int TrafficReplanCount { get; private set; }

        /// <summary>Removing a blocker must release the obsolete waiting schedule as well as its cell reservations.</summary>
        private void ReplanReleasedTraffic()
        {
            if (RecoveryActive)
            {
                return;
            }

            foreach (var state in occupants.Values.OrderBy(value => value.Agent.ArrivalTick).ThenBy(value => value.Agent.Id))
            {
                if (state.TrafficRevision == trafficRevision)
                {
                    continue;
                }

                if (state.Agent.Status != NavigationAgentStatus.Navigating || state.TerrainRepairPending)
                {
                    state.TrafficRevision = trafficRevision;
                    continue;
                }

                int cell = state.Path[state.Segment].Cell;
                if (!Same(state.Agent.Position, grid.Center(cell)))
                {
                    // A body in transit keeps its exact committed segment; retry at the next cell centre.
                    continue;
                }

                bool scheduledWait = false;
                for (int index = state.Segment + 1; index < state.Path.Count; index++)
                {
                    if (state.Path[index].Departure > Math.Max(CurrentTick, state.Path[index - 1].Arrival))
                    {
                        scheduledWait = true;
                        break;
                    }
                }

                state.TrafficRevision = trafficRevision;
                if (!scheduledWait)
                {
                    continue;
                }

                long arrival = state.Agent.ArrivalTick;
                long departure = state.Path[state.Segment + 1].Departure;
                var priorFailure = LastFailure;
                TrafficReplanAttempts++;
                var plan = FindVacancyPath(state, cell, state.Agent.GoalCell);
                if (plan != null && (plan[plan.Count - 1].Arrival < arrival
                    || (plan[plan.Count - 1].Arrival == arrival && plan.Count > 1 && plan[1].Departure < departure)))
                {
                    AcceptPlan(state, plan);
                    UpdatePosition(state, CurrentTick);
                    TrafficReplanCount++;
                    ReplanCount++;
                }

                LastFailure = priorFailure;
            }
        }
    }
}
