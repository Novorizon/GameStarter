using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private sealed class CandidateArc
        {
            internal readonly List<Vector2> Points = new();
            private readonly List<int> domainEnds = new();
            private readonly List<float> exactEnds = new();

            internal CandidateArc(Route route)
            {
                int progress = 0;
                Points.Add(CandidateWorld(route.Definition.Points[0]));
                domainEnds.Add(0);
                exactEnds.Add(0);
                for (int i = 0; i < route.SegmentLengths.Length && progress < route.Length; i++)
                {
                    int end = Math.Min(route.Length, progress + route.SegmentLengths[i]);
                    var position = CandidateWorld(route.Position(end));
                    exactEnds.Add(exactEnds[^1] + Vector2.Distance(Points[^1], position));
                    domainEnds.Add(end);
                    Points.Add(position);
                    progress = end;
                }
            }

            internal float ExactAt(int domain)
            {
                for (int i = 1; i < domainEnds.Count; i++)
                {
                    if (domain <= domainEnds[i])
                    {
                        float fraction = (domain - domainEnds[i - 1]) / (float)(domainEnds[i] - domainEnds[i - 1]);
                        return exactEnds[i - 1] + fraction * (exactEnds[i] - exactEnds[i - 1]);
                    }
                }
                return exactEnds[^1];
            }

            internal int DomainAt(float exact)
            {
                for (int i = 1; i < exactEnds.Count; i++)
                {
                    if (exact <= exactEnds[i])
                    {
                        float fraction = (exact - exactEnds[i - 1]) / (exactEnds[i] - exactEnds[i - 1]);
                        return (int)MathF.Floor(domainEnds[i - 1] + fraction * (domainEnds[i] - domainEnds[i - 1]) + .0001f);
                    }
                }
                return domainEnds[^1];
            }
        }

        private RouteConflictCoordinator roadConflictCandidate;
        private Dictionary<int, CandidateArc> roadConflictCandidateArcs;

        private void ScheduleRoadConflicts()
        {
            if (!useAuthoredNavigation || routes.Count < 2)
            {
                return;
            }

            if (roadConflictCandidate == null)
            {
                roadConflictCandidateArcs = routes.ToDictionary(pair => pair.Key, pair => new CandidateArc(pair.Value));
                roadConflictCandidate = new RouteConflictCoordinator(roadConflictCandidateArcs.ToDictionary(
                    pair => pair.Key, pair => (IReadOnlyList<Vector2>)pair.Value.Points));
            }

            var pending = enemies.Where(enemy => enemy.Distance < enemy.Route.Length).Select(enemy =>
            {
                var arc = roadConflictCandidateArcs[enemy.Route.Definition.Id];
                return new RouteTrafficBody(enemy.Id, enemy.Route.Definition.Id, arc.ExactAt(enemy.Distance),
                    arc.ExactAt(Math.Min(enemy.Route.Length, enemy.Distance + enemy.Definition.SpeedPerTick)), enemy.Definition.BodyRadius / 1000f);
            }).ToArray();
            roadConflictCandidate.Schedule(pending, CandidateExitClear);
        }

        private int RoadProgressWithClearance(Enemy enemy)
        {
            int desired = Math.Min(enemy.Route.Length, enemy.Distance + enemy.Definition.SpeedPerTick);
            if (roadConflictCandidate == null || enemy.Distance == enemy.Route.Length)
            {
                return desired;
            }

            var arc = roadConflictCandidateArcs[enemy.Route.Definition.Id];
            float allowed = roadConflictCandidate.AllowedProgress(enemy.Id);
            return allowed >= arc.ExactAt(desired) ? desired : Math.Clamp(arc.DomainAt(allowed), enemy.Distance, desired);
        }

        private bool CandidateExitClear(RouteTrafficBody mover, Vector2 start, Vector2 end)
        {
            var from = CandidateDomain(start);
            var to = CandidateDomain(end);
            int radius = (int)MathF.Round(mover.Radius * 1000);
            if (!IsClearBattleGround(from, to, radius))
            {
                return false;
            }

            foreach (var other in enemies)
            {
                if (other.Id == mover.Id || other.Distance < other.Route.Length)
                {
                    continue;
                }

                int clearance = radius + other.Definition.BodyRadius;
                if (SegmentDistanceSquared(other.Position, from, to) < (long)clearance * clearance
                    || IntersectsAssaultReservation(from, to, other, clearance))
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector2 CandidateWorld(Position2 point) => new(point.X / 1000f, point.Y / 1000f);
        private static Position2 CandidateDomain(Vector2 point) => new((int)MathF.Round(point.X * 1000), (int)MathF.Round(point.Y * 1000));
    }
}
