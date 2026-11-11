using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal sealed partial class AoeNavigator
    {
        private sealed class SlotExchange
        {
            internal int Requester;
            internal int Holder;
            internal int RequesterOldGoal;
            internal int HolderOldGoal;
            internal int Started;
            internal bool HolderArrived;
        }

        private readonly Dictionary<int, SlotExchange> exchanges = new Dictionary<int, SlotExchange>();
        private readonly Dictionary<string, int> exchangeCooldown = new Dictionary<string, int>();

        private void UpdateSlotExchanges(Actor[] ordered)
        {
            foreach (var exchange in exchanges.Values.ToArray())
            {
                if (!actors.TryGetValue(exchange.Requester, out var requester) || !actors.TryGetValue(exchange.Holder, out var holder)
                    || requester.Goal != exchange.HolderOldGoal || holder.Goal != exchange.RequesterOldGoal)
                {
                    EndSlotExchange(exchange, false);
                    continue;
                }
                if (!exchange.HolderArrived && ExactRest(holder))
                {
                    exchange.HolderArrived = true;
                    Add("SlotExchangeHolderArrivals");
                }
                if (exchange.HolderArrived && ExactRest(requester))
                {
                    EndSlotExchange(exchange, true);
                }
                else if (step - exchange.Started > 3000)
                {
                    Add("SlotExchangeTimeouts");
                    EndSlotExchange(exchange, false);
                }
            }
            if (step % SimulationProtocol.Substeps != 0)
            {
                return;
            }
            foreach (var requester in ordered.Where(actor => actor.ExchangePartner == 0 && actor.Request.AssignedGoalCell < 0
                && actor.YieldCell < 0 && State(actor) == MotionState.Moving && actor.Route == null && step - actor.LastProgress >= 250)
                .OrderBy(actor => actor.LastProgress).ThenBy(actor => actor.Request.Id))
            {
                var holders = ordered.Where(holder => holder.Request.Id != requester.Request.Id && holder.ExchangePartner == 0
                    && holder.Request.AssignedGoalCell < 0 && holder.YieldCell < 0 && holder.Request.TargetId == requester.Request.TargetId
                    && holder.Request.Profile.Equals(requester.Request.Profile) && ExactRest(holder))
                    .OrderBy(holder => Point2.Distance(holder.Position, World.Center(requester.Goal))).ThenBy(holder => holder.Request.Id);
                foreach (var holder in holders)
                {
                    string key = ExchangeKey(requester.Request.Id, holder.Request.Id);
                    if (exchangeCooldown.TryGetValue(key, out int last) && step - last < 2000)
                    {
                        continue;
                    }
                    if (!goalOwners.TryGetValue(requester.Goal, out int requesterOwner) || requesterOwner != requester.Request.Id
                        || !goalOwners.TryGetValue(holder.Goal, out int holderOwner) || holderOwner != holder.Request.Id
                        || !geometry.CanHold(requester.Goal, holder.Request.Profile, holder.Request.TargetId)
                        || !geometry.CanHold(holder.Goal, requester.Request.Profile, requester.Request.TargetId))
                    {
                        continue;
                    }
                    bool requesterAttack = geometry.CanAttack(World.Center(requester.Goal), requester.Request.Profile, requester.Request.TargetId);
                    bool holderAttack = geometry.CanAttack(World.Center(holder.Goal), holder.Request.Profile, holder.Request.TargetId);
                    if (requesterAttack != holderAttack
                        || !FullBodySweep(holder, World.Center(requester.Goal), ordered, -1)
                        || !FullBodySweep(requester, World.Center(holder.Goal), ordered, holder.Request.Id))
                    {
                        continue;
                    }
                    var exchange = new SlotExchange
                    {
                        Requester = requester.Request.Id,
                        Holder = holder.Request.Id,
                        RequesterOldGoal = requester.Goal,
                        HolderOldGoal = holder.Goal,
                        Started = step
                    };
                    // Only ownership changes here; both bodies keep their exact current pose.
                    goalOwners.Remove(requester.Goal);
                    goalOwners.Remove(holder.Goal);
                    SetGoal(requester, exchange.HolderOldGoal);
                    SetGoal(holder, exchange.RequesterOldGoal);
                    requester.ExchangePartner = holder.Request.Id;
                    holder.ExchangePartner = requester.Request.Id;
                    exchanges.Add(requester.Request.Id, exchange);
                    exchangeCooldown[key] = step;
                    bodySignature = "slot-exchange-dirty-" + step;
                    Add("SlotExchangesStarted");
                    return;
                }
            }
        }

        private bool HoldSlotRequester(Actor actor)
        {
            return exchanges.TryGetValue(actor.Request.Id, out var exchange) && !exchange.HolderArrived;
        }

        private bool FullBodySweep(Actor actor, Point2 destination, Actor[] ordered, int ignoredId)
        {
            if (!geometry.CanSegment(actor.Position, destination, actor.Request.Profile.RadiusMm))
            {
                return false;
            }
            foreach (var other in ordered)
            {
                if (other.Request.Id == actor.Request.Id || other.Request.Id == ignoredId)
                {
                    continue;
                }
                double combined = actor.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm;
                if (GridGeometry.PointSegmentSquared(other.Position, actor.Position, destination) < combined * combined - 1e-5)
                {
                    return false;
                }
            }
            return true;
        }

        private bool ExactRest(Actor actor)
        {
            return actor.YieldCell < 0 && actor.Velocity.LengthSquared < 1e-8
                && Point2.Distance(actor.Position, World.Center(actor.Goal)) <= 1e-6;
        }

        private void EndSlotExchange(SlotExchange exchange, bool completed)
        {
            if (actors.TryGetValue(exchange.Requester, out var requester))
            {
                requester.ExchangePartner = 0;
            }
            if (actors.TryGetValue(exchange.Holder, out var holder))
            {
                holder.ExchangePartner = 0;
            }
            exchanges.Remove(exchange.Requester);
            Add(completed ? "SlotExchangesCompleted" : "SlotExchangesCancelled");
        }

        private void CancelSlotExchanges(int removedId)
        {
            foreach (var exchange in exchanges.Values.Where(exchange => removedId < 0
                || exchange.Requester == removedId || exchange.Holder == removedId).ToArray())
            {
                EndSlotExchange(exchange, false);
            }
        }

        private static string ExchangeKey(int first, int second)
        {
            return Math.Min(first, second) + ":" + Math.Max(first, second);
        }
    }
}
