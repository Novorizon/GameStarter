using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal sealed partial class AoeNavigator
    {
        private sealed class RouteProgress
        {
            internal string Intent = string.Empty;
            internal int TargetId;
            internal int Radius;
            internal int DestinationCell;
            internal bool TemporaryYield;
            internal string WorldShape = string.Empty;
            internal string LastBodySignature = string.Empty;
            internal string LastCorridor = string.Empty;
            internal string HistoryKey = string.Empty;
            internal readonly Dictionary<string, double> BestRemaining = new Dictionary<string, double>();
            internal HierarchicalFlow.Route? LastRoute;
            internal Point2 WindowAnchor;
            internal int WindowStarted;
            internal int LastQualified;
            internal int LastFinite;
            internal int StationaryWindows;
            internal bool WindowImproved;
            internal bool Stalled;
            internal double LastRemaining = double.PositiveInfinity;
            internal double LastWindowDisplacement;
        }

        private bool SlotRequesterStalled(Actor actor)
        {
            if (!progressAware)
            {
                return step - actor.LastProgress >= 250;
            }
            return actor.Progress != null && MatchesProgressIntent(actor, actor.Progress) && actor.Progress.Stalled;
        }

        private bool MatchesProgressIntent(Actor actor, RouteProgress progress)
        {
            bool temporary = actor.YieldCell >= 0;
            int destination = temporary ? actor.YieldCell : actor.Goal;
            return progress.TargetId == actor.Request.TargetId && progress.Radius == actor.Request.Profile.RadiusMm
                && progress.DestinationCell == destination && progress.TemporaryYield == temporary && progress.WorldShape == progressWorldShape;
        }

        private string ProgressIntent(Actor actor)
        {
            string destination = actor.YieldCell >= 0 ? "yield:" + actor.YieldCell : "goal:" + actor.Goal;
            return actor.Request.TargetId + ":" + actor.Request.Profile.RadiusMm + ":" + destination + ":" + progressWorldShape;
        }

        private void ObserveRouteProgress(Actor actor, HierarchicalFlow.Flow? field, int anchor, bool directGoal)
        {
            Add("RouteProgressObservations");
            if (actor.Progress == null || !MatchesProgressIntent(actor, actor.Progress))
            {
                actor.Progress = new RouteProgress
                {
                    Intent = ProgressIntent(actor),
                    TargetId = actor.Request.TargetId,
                    Radius = actor.Request.Profile.RadiusMm,
                    DestinationCell = actor.YieldCell >= 0 ? actor.YieldCell : actor.Goal,
                    TemporaryYield = actor.YieldCell >= 0,
                    WorldShape = progressWorldShape,
                    WindowAnchor = actor.Position,
                    WindowStarted = step,
                    LastQualified = step,
                    LastFinite = step - 10000
                };
                Add("RouteProgressIntentChanges");
            }
            var progress = actor.Progress;
            string corridor = actor.Route == null ? "no-route" : actor.Route.ProgressKey;
            if (progress.LastBodySignature != bodySignature || progress.LastCorridor != corridor)
            {
                progress.LastBodySignature = bodySignature;
                progress.LastCorridor = corridor;
                progress.HistoryKey = bodySignature + "|" + corridor;
            }
            string key = progress.HistoryKey;
            double remaining = RemainingRouteProgress(actor, field, anchor, directGoal);
            progress.LastRemaining = remaining;
            if (!double.IsInfinity(remaining) && !double.IsNaN(remaining))
            {
                double epsilon = Math.Max(.5, actor.Request.Profile.RadiusMm * .05);
                if (!progress.BestRemaining.TryGetValue(key, out double best))
                {
                    if (progress.BestRemaining.Count < 1024)
                    {
                        progress.BestRemaining.Add(key, remaining);
                        progress.LastFinite = step;
                    }
                    else
                    {
                        Add("RouteProgressHistoryLimitSamples");
                    }
                    Add("RouteProgressRouteBaselines");
                }
                else
                {
                    progress.LastFinite = step;
                    if (!ReferenceEquals(progress.LastRoute, actor.Route))
                    {
                        Add("RouteProgressSameIntentRetries");
                    }
                    if (remaining < best - epsilon)
                    {
                        progress.BestRemaining[key] = remaining;
                        progress.WindowImproved = true;
                        Add("RouteProgressHistoricalBestImprovements");
                    }
                }
            }
            else
            {
                Add("RouteProgressUnprojectableSamples");
            }
            progress.LastRoute = actor.Route;
            double threshold = Math.Max(.5, Math.Min(actor.Request.Profile.RadiusMm * .5, actor.Request.Profile.SpeedMmPerTick * 8.0));
            if (step - progress.WindowStarted >= 400)
            {
                double displacement = Point2.Distance(actor.Position, progress.WindowAnchor);
                progress.LastWindowDisplacement = displacement;
                bool moved = displacement >= threshold;
                if (progress.WindowImproved && moved)
                {
                    progress.LastQualified = step;
                    Add("RouteProgressQualifiedWindows");
                }
                else if (moved)
                {
                    Add("RouteProgressMotionOnlyWindowsRejected");
                }
                else if (progress.WindowImproved)
                {
                    Add("RouteProgressPotentialOnlyWindowsRejected");
                }
                progress.StationaryWindows = moved ? 0 : progress.StationaryWindows + 1;
                progress.WindowAnchor = actor.Position;
                progress.WindowStarted = step;
                progress.WindowImproved = false;
                Add("RouteProgressFixedWindows");
            }
            double speed = Math.Max(.1, actor.Request.Profile.SpeedMmPerTick / 10.0);
            int deadline = Math.Max(1000, (int)Math.Ceiling(actor.Request.Profile.RadiusMm * 8 / speed));
            bool observable = step - progress.LastFinite <= 400 || progress.StationaryWindows >= 2;
            bool stalled = observable && step - progress.LastQualified >= deadline;
            if (stalled && !progress.Stalled)
            {
                Add("RouteProgressStallDetections");
                if (step - actor.LastProgress < 250)
                {
                    Add("RouteProgressLegacyMotionMaskDetected");
                }
            }
            progress.Stalled = stalled;
        }

        private double RemainingRouteProgress(Actor actor, HierarchicalFlow.Flow? field, int anchor, bool directGoal)
        {
            if (directGoal)
            {
                return Point2.Distance(actor.Position, Destination(actor));
            }
            var route = actor.Route;
            if (route == null)
            {
                return double.PositiveInfinity;
            }
            if (field != null)
            {
                int end = Math.Min(route.Exits.Length - 1, route.CurrentIndex + 2);
                if (end >= 0 && end < route.RemainingAfterExits.Length && route.RemainingAfterExits[end] >= -1e-6)
                {
                    double suffix = Math.Max(0, route.RemainingAfterExits[end]);
                    Point2 exit = World.Center(field.Exit);
                    if (planningGeometry.CanSegment(actor.Position, exit, actor.Request.Profile.RadiusMm))
                    {
                        return Point2.Distance(actor.Position, exit) + suffix;
                    }
                    int cell = World.CellAt(actor.Position);
                    if (cell >= 0)
                    {
                        double best = double.PositiveInfinity;
                        foreach (int probe in planningGeometry.Neighbours(cell).Prepend(cell))
                        {
                            if (field.Included[probe] && !double.IsInfinity(field.Distance[probe])
                                && planningGeometry.CanSegment(actor.Position, World.Center(probe), actor.Request.Profile.RadiusMm))
                            {
                                best = Math.Min(best, Point2.Distance(actor.Position, World.Center(probe)) + field.Distance[probe] + suffix);
                            }
                        }
                        if (!double.IsInfinity(best))
                        {
                            return best;
                        }
                    }
                }
            }
            if (anchor == route.StartCell && anchor >= 0
                && planningGeometry.CanSegment(actor.Position, World.Center(anchor), actor.Request.Profile.RadiusMm))
            {
                return Point2.Distance(actor.Position, World.Center(anchor)) + route.TotalCost;
            }
            return double.PositiveInfinity;
        }

        private string WorldShapeIdentity()
        {
            ulong hash = 14695981039346656037UL;
            void Mix(long value)
            {
                unchecked
                {
                    for (int i = 0; i < 8; i++)
                    {
                        hash ^= (byte)(value >> (i * 8));
                        hash *= 1099511628211UL;
                    }
                }
            }
            Mix(World.Width);
            Mix(World.Height);
            Mix(World.CellSizeMm);
            Mix(BitConverter.DoubleToInt64Bits(World.Origin.X));
            Mix(BitConverter.DoubleToInt64Bits(World.Origin.Y));
            for (int cell = 0; cell < World.CellCount; cell++)
            {
                Mix((World.IsWalkable(cell) ? 1 : 0) | (World.HoldingAllowed(cell) ? 2 : 0));
            }
            foreach (var target in World.Targets.OrderBy(target => target.Id))
            {
                Mix(target.Id);
                Mix(BitConverter.DoubleToInt64Bits(target.Center.X));
                Mix(BitConverter.DoubleToInt64Bits(target.Center.Y));
                Mix(target.RadiusMm);
            }
            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }
    }
}
