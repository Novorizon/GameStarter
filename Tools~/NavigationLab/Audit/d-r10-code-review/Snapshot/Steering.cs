using System;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal sealed partial class AoeNavigator
    {
        private Point2 SampleVelocity(Actor actor, Point2 direction, Point2 destination, Actor[] ordered)
        {
            double max = actor.Request.Profile.SpeedMmPerTick / (double)SimulationProtocol.Substeps;
            double error = Point2.Distance(actor.Position, destination);
            double length = Math.Min(max, error);
            if (direction.LengthSquared < 1e-12 || length <= 0)
            {
                return default;
            }
            var neighbours = ordered.Where(other => other.Request.Id != actor.Request.Id)
                .OrderBy(other => (other.Position - actor.Position).LengthSquared).Take(24).ToArray();
            Point2 desired = ClipStatic(actor, direction * length);
            double desiredScore = Score(actor, desired, direction, destination, neighbours, out bool risk);
            if (!risk && desiredScore > 0)
            {
                return desired;
            }
            Point2 best = default;
            double bestScore = -.2;
            int hand = (actor.Request.Id & 1) == 0 ? 1 : -1;
            for (int i = 0; i < 25; i++)
            {
                double angle = i == 0 ? 0 : ((i + 1) / 2) * Math.PI / 12 * ((i & 1) == 0 ? -hand : hand);
                double cosine = Math.Cos(angle);
                double sine = Math.Sin(angle);
                var delta = ClipStatic(actor, new Point2(direction.X * cosine - direction.Y * sine, direction.X * sine + direction.Y * cosine) * length);
                double score = Score(actor, delta, direction, destination, neighbours, out _);
                if (score > bestScore + 1e-9)
                {
                    best = delta;
                    bestScore = score;
                }
            }
            if (best.LengthSquared < 1e-12)
            {
                Add("SteeringWaits");
            }
            return best;
        }

        private double Score(Actor actor, Point2 delta, Point2 direction, Point2 destination, Actor[] neighbours, out bool risk)
        {
            Add("SteeringSamples");
            risk = false;
            if (delta.LengthSquared < 1e-12)
            {
                return double.NegativeInfinity;
            }
            Point2 next = actor.Position + delta;
            if (!geometry.CanSegment(actor.Position, next, actor.Request.Profile.RadiusMm))
            {
                Add("StaticRejectedProposals");
                risk = true;
                return double.NegativeInfinity;
            }
            double length = Math.Max(1e-9, Math.Min(actor.Request.Profile.SpeedMmPerTick / 10.0, Point2.Distance(actor.Position, destination)));
            double score = Point2.Dot(delta, direction) / length;
            double remaining = Point2.Distance(actor.Position, destination);
            int horizon = (int)Math.Max(2, Math.Min(40, World.CellSizeMm * .5 / Math.Max(.1, actor.Request.Profile.SpeedMmPerTick / 10.0)));
            Point2 forecast = Forecast(actor.Position, delta, destination, horizon);
            double futurePenalty = 0;
            foreach (var other in neighbours)
            {
                double radius = actor.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm;
                Point2 otherMove = other.Planned ? other.Proposal : default;
                Point2 otherNext = other.Position + otherMove;
                if (GridGeometry.SweptGapSquared(actor.Position, next, other.Position, otherNext) < radius * radius - 1e-5)
                {
                    risk = true;
                    return double.NegativeInfinity;
                }
                Point2 otherDestination = other.PreciseWaypoint >= 0 ? World.Center(other.PreciseWaypoint) : Destination(other);
                Point2 otherForecast = Forecast(other.Position, otherMove, otherDestination, horizon);
                double gap = Math.Sqrt(GridGeometry.SweptGapSquared(actor.Position, forecast, other.Position, otherForecast)) - radius;
                double guard = 0;
                double initialGap = Point2.Distance(actor.Position, other.Position) - radius;
                double threshold = Math.Min(guard, Math.Max(0, initialGap));
                if (gap < threshold - 1e-5)
                {
                    risk = true;
                    futurePenalty = Math.Max(futurePenalty, .6 + (threshold - gap) / Math.Max(1, radius) * .8);
                }
                // Small deterministic lateral preference breaks perfectly mirrored head-on steering choices.
                Point2 offset = other.Position - actor.Position;
                if (offset.Length < radius + World.CellSizeMm && Point2.Dot(offset.Normalized, direction) > .2)
                {
                    double cross = direction.X * delta.Y - direction.Y * delta.X;
                    score += .015 * Math.Sign(cross);
                }
            }
            score -= futurePenalty;
            score -= .025 * (delta - actor.Velocity).Length / Math.Max(1, actor.Request.Profile.SpeedMmPerTick / 10.0);
            return score;
        }

        private Point2 ClipStatic(Actor actor, Point2 movement)
        {
            if (geometry.CanSegment(actor.Position, actor.Position + movement, actor.Request.Profile.RadiusMm))
            {
                return movement;
            }
            double low = 0;
            double high = 1;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                double middle = (low + high) * .5;
                if (geometry.CanSegment(actor.Position, actor.Position + movement * middle, actor.Request.Profile.RadiusMm))
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            Add("StaticShortenedProposals");
            return movement * low;
        }

        private static Point2 Forecast(Point2 position, Point2 movement, Point2 destination, int horizon)
        {
            Point2 delta = movement * horizon;
            Point2 toGoal = destination - position;
            if (toGoal.Length < delta.Length && Point2.Dot(movement.Normalized, toGoal.Normalized) > .95)
            {
                return destination;
            }
            return position + delta;
        }

        private void UpdateYields(Actor[] ordered)
        {
            foreach (var actor in ordered.Where(actor => actor.YieldCell >= 0))
            {
                if (!actors.TryGetValue(actor.YieldRequester, out var requester) || State(requester) != MotionState.Moving)
                {
                    EndYield(actor);
                    continue;
                }
                if (Point2.Distance(actor.Position, World.Center(actor.YieldCell)) <= SimulationProtocol.GoalToleranceMm)
                {
                    if (actor.YieldHoldStarted == 0)
                    {
                        actor.YieldHoldStarted = step;
                    }
                    double clearance = actor.Request.Profile.RadiusMm + requester.Request.Profile.RadiusMm + World.CellSizeMm;
                    bool passed = Point2.Distance(requester.Position, World.Center(actor.Goal)) > clearance
                        && Point2.Distance(requester.Position, World.Center(requester.Goal))
                            < Point2.Distance(World.Center(actor.Goal), World.Center(requester.Goal));
                    if (passed && step - actor.YieldHoldStarted >= 10)
                    {
                        EndYield(actor);
                    }
                }
                if (step - actor.YieldStarted > 4000)
                {
                    Add("YieldTimeouts");
                    EndYield(actor);
                }
            }
            foreach (var requester in ordered.Where(actor => State(actor) == MotionState.Moving && step - actor.LastProgress >= 250)
                .OrderByDescending(actor => step - actor.LastProgress).ThenBy(actor => actor.Request.Id))
            {
                if (requester.YieldCell >= 0)
                {
                    continue;
                }
                Point2 target = World.Center(requester.Goal);
                var blockers = ordered.Where(other => other.Request.Id != requester.Request.Id && other.YieldCell < 0
                    && (State(other) != MotionState.Moving || step - other.LastProgress >= 250))
                    .Where(other => Point2.Distance(other.Position, requester.Position) < World.CellSizeMm * 3
                        || GridGeometry.PointSegmentSquared(other.Position, requester.Position, target)
                            < Math.Pow(other.Request.Profile.RadiusMm + requester.Request.Profile.RadiusMm + 50, 2))
                    .OrderBy(other => Point2.Distance(other.Position, requester.Position)).ThenBy(other => other.Request.Id).Take(4).ToArray();
                foreach (var blocker in blockers)
                {
                    int radius = blocker.Request.Profile.RadiusMm;
                    int start = World.CellAt(blocker.Position);
                    int[] distances = Reachability(start, radius);
                    int buffer = Enumerable.Range(0, World.CellCount)
                        .Where(cell => geometry.Clearance(radius)[cell] && distances[cell] > 0 && distances[cell] <= 8
                            && !goalOwners.ContainsKey(cell) && !World.SpawnCells.Contains(cell)
                            && !ordered.Any(other => other.YieldCell == cell)
                            && GridGeometry.PointSegmentSquared(World.Center(cell), requester.Position, target)
                                >= Math.Pow(radius + requester.Request.Profile.RadiusMm + 50, 2)
                            && ordered.All(other => other.Request.Id == blocker.Request.Id
                                || Point2.Distance(World.Center(cell), other.Position) >= radius + other.Request.Profile.RadiusMm + .1))
                        .OrderBy(cell => distances[cell]).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
                    if (buffer < 0)
                    {
                        continue;
                    }
                    blocker.YieldCell = buffer;
                    blocker.YieldRequester = requester.Request.Id;
                    blocker.YieldStarted = step;
                    blocker.YieldHoldStarted = 0;
                    blocker.Route = null;
                    blocker.PreciseWaypoint = -1;
                    blocker.BestGoalDistance = double.MaxValue;
                    blocker.LastProgress = step;
                    requester.LastProgress = step;
                    Add("YieldRequests");
                }
            }
        }

        private void EndYield(Actor actor)
        {
            actor.YieldCell = -1;
            actor.YieldRequester = 0;
            actor.YieldHoldStarted = 0;
            actor.Route = null;
            actor.PreciseWaypoint = -1;
            actor.BestGoalDistance = double.MaxValue;
            actor.LastProgress = step;
            Add("YieldReturns");
        }
    }
}
