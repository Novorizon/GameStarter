using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int RoadLookahead = 220;
        private const int MaximumRoadOffset = 100;
        private const int FinalCenterlineDistance = 450;
        private const int RoadGuideSweepPadding = 3;
        private readonly Dictionary<(int Route, int Radius), RoadCorner?[]> candidateRoadGuides = new();

        private readonly struct RoadCorner
        {
            internal readonly int Arc;
            internal readonly int Cut;
            internal readonly Position2 Enter;
            internal readonly Position2 Vertex;
            internal readonly Position2 Exit;

            internal RoadCorner(int arc, int cut, Position2 enter, Position2 vertex, Position2 exit)
            {
                Arc = arc;
                Cut = cut;
                Enter = enter;
                Vertex = vertex;
                Exit = exit;
            }

            internal bool Contains(int arc) => arc > Arc - Cut && arc < Arc + Cut;

            internal Position2 Position(int arc)
            {
                double t = (arc - Arc + Cut) / (2d * Cut);
                double before = (1 - t) * (1 - t);
                double middle = 2 * t * (1 - t);
                double after = t * t;
                return new Position2(
                    (int)Math.Round(before * Enter.X + middle * Vertex.X + after * Exit.X),
                    (int)Math.Round(before * Enter.Y + middle * Vertex.Y + after * Exit.Y));
            }
        }

        private void AdvanceCorridorRoad(Enemy enemy)
        {
            if (siegeReflow?.Gate == enemy)
            {
                return;
            }

            var route = enemy.Route;
            int target = RoadProgressWithClearance(enemy);
            var expected = RoadGuidePosition(enemy, enemy.Distance);
            if (DistanceSquared(expected, enemy.Position) > 0)
            {
                // An obstruction can invalidate a preferred corner after entry. Rejoin at the current arc.
                var recenter = MoveRoadStep(enemy.Position, expected, enemy.Definition.SpeedPerTick);
                if (CanSweepAuthoredRoad(enemy, recenter))
                {
                    enemy.Position = recenter;
                }
                return;
            }

            while (enemy.Distance < target)
            {
                int segmentEnd = enemy.RoadSegmentStart + route.SegmentLengths[enemy.RoadSegmentIndex];
                int next = Math.Min(target, Math.Min(segmentEnd, enemy.Distance + EnemyMoveStepMillimetres));
                var position = RoadGuidePosition(enemy, next);
                if (!CanSweepAuthoredRoad(enemy, position))
                {
                    int clear = enemy.Distance;
                    int blocked = next;
                    while (blocked - clear > 1)
                    {
                        int probe = clear + (blocked - clear) / 2;
                        if (CanSweepAuthoredRoad(enemy, RoadGuidePosition(enemy, probe)))
                        {
                            clear = probe;
                        }
                        else
                        {
                            blocked = probe;
                        }
                    }

                    SetRoadProgress(clear, RoadGuidePosition(enemy, clear));
                    return;
                }

                SetRoadProgress(next, position);
                if (next == segmentEnd && next < route.Length)
                {
                    enemy.RoadSegmentStart = segmentEnd;
                    enemy.RoadSegmentIndex++;
                }
            }

            void SetRoadProgress(int progress, Position2 position)
            {
                enemy.Distance = progress;
                enemy.Position = position;
            }
        }

        private Position2 RoadGuidePosition(Enemy enemy, int progress)
        {
            var route = enemy.Route;
            int radius = enemy.Definition.BodyRadius;
            var key = (route.Definition.Id, radius);
            if (!candidateRoadGuides.TryGetValue(key, out var guide))
            {
                guide = CreateRoadGuide(route, radius);
                candidateRoadGuides.Add(key, guide);
            }

            int index = enemy.RoadSegmentIndex;
            if (index > 0 && guide[index - 1] is RoadCorner incoming && CanFollow(incoming))
            {
                return incoming.Position(progress);
            }

            return index < guide.Length && guide[index] is RoadCorner outgoing && CanFollow(outgoing)
                ? outgoing.Position(progress) : route.Position(progress);

            bool CanFollow(RoadCorner corner) =>
                corner.Contains(progress) && IsClearOfTowers(corner.Enter, corner.Exit, radius + MaximumRoadOffset);
        }

        private RoadCorner?[] CreateRoadGuide(Route route, int radius)
        {
            var guide = new RoadCorner?[route.SegmentLengths.Length - 1];
            var points = route.Definition.Points;
            int arc = 0;
            for (int i = 0; i < guide.Length; i++)
            {
                arc += route.SegmentLengths[i];
                int cut = Math.Min(RoadLookahead, Math.Min(route.SegmentLengths[i], route.SegmentLengths[i + 1]) / 3);
                if (cut < 24 || arc + cut > route.Length - FinalCenterlineDistance)
                {
                    continue;
                }

                Vector2 incoming = CandidateWorld(points[i + 1]) - CandidateWorld(points[i]);
                Vector2 outgoing = CandidateWorld(points[i + 2]) - CandidateWorld(points[i + 1]);
                float turn = Vector2.Dot(Vector2.Normalize(incoming), Vector2.Normalize(outgoing));
                if (turn < -0.01f || turn > 0.9995f)
                {
                    continue;
                }

                var corner = new RoadCorner(arc, cut, route.Position(arc - cut), points[i + 1], route.Position(arc + cut));
                var midpoint = corner.Position(arc);
                int offset = MaximumRoadOffset - RoadGuideSweepPadding;
                if (SegmentDistanceSquared(midpoint, corner.Enter, corner.Vertex) > offset * offset
                    || SegmentDistanceSquared(midpoint, corner.Vertex, corner.Exit) > offset * offset
                    || NearOtherRoute(route, corner, radius) || !ClearRoadCorner(corner, radius))
                {
                    continue;
                }

                guide[i] = corner;
            }
            return guide;
        }

        // The quadratic splits at the authored vertex arc. Each half and its sweeps fit in one convex segment capsule.
        private bool ClearRoadCorner(RoadCorner corner, int radius)
        {
            if (!Clear(corner.Enter, corner.Exit, radius))
            {
                return false;
            }

            const int subdivisions = 8;
            var previous = corner.Enter;
            for (int i = 1; i <= subdivisions; i++)
            {
                var next = corner.Position(corner.Arc - corner.Cut + 2 * corner.Cut * i / subdivisions);
                if (!Clear(previous, next, radius + RoadGuideSweepPadding))
                {
                    return false;
                }
                previous = next;
            }
            return true;

            bool Clear(Position2 from, Position2 to, int clearance)
            {
                var engagement = Configuration.Objective.BaseAttack?.Engagement;
                return Configuration.Terrain?.CanMove(from, to, clearance) != false
                    && (engagement == null || !CrossesBaseFootprint(from, to, engagement, clearance));
            }
        }

        private bool NearOtherRoute(Route route, RoadCorner corner, int radius)
        {
            foreach (var other in routes.Values)
            {
                if (other == route || other.LargestBodyRadius <= 0)
                {
                    continue;
                }

                int progress = 0;
                int clearance = radius + other.LargestBodyRadius + 2 * MaximumRoadOffset + RoadGuideSweepPadding;
                for (int i = 0; i < other.SegmentLengths.Length && progress < other.Length; i++)
                {
                    int end = Math.Min(other.Length, progress + other.SegmentLengths[i]);
                    var from = other.Definition.Points[i];
                    var to = end == progress + other.SegmentLengths[i] ? other.Definition.Points[i + 1] : other.Position(end);
                    if (Near(corner.Enter, corner.Vertex, from, to, clearance) || Near(corner.Vertex, corner.Exit, from, to, clearance))
                    {
                        return true;
                    }
                    progress = end;
                }
            }
            return false;

            static bool Near(Position2 a, Position2 b, Position2 c, Position2 d, int clearance)
            {
                long square = (long)clearance * clearance;
                return CrossesReservationInterior(a, b, c, d) || SegmentDistanceSquared(a, c, d) < square
                    || SegmentDistanceSquared(b, c, d) < square || SegmentDistanceSquared(c, a, b) < square
                    || SegmentDistanceSquared(d, a, b) < square;
            }
        }

        private static Position2 MoveRoadStep(Position2 from, Position2 to, int budget)
        {
            double distance = Math.Sqrt(DistanceSquared(from, to));
            if (distance <= budget)
            {
                return to;
            }
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            int stepX = (int)(dx * (double)budget / distance);
            int stepY = (int)(dy * (double)budget / distance);
            if (stepX == 0 && stepY == 0 && budget > 0)
            {
                // A one-millimetre budget can still make progress on a diagonal after quantization.
                if (Math.Abs(dx) >= Math.Abs(dy))
                {
                    stepX = Math.Sign(dx);
                }
                else
                {
                    stepY = Math.Sign(dy);
                }
            }
            return new Position2(from.X + stepX, from.Y + stepY);
        }
    }
}
