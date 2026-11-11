using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private sealed class BaseAttackOrder
        {
            internal BaseAttackPath Layout;
            internal Route Path;
            internal int[] Stops;
            internal int Slot;
            internal int Distance;
            internal int Stop => Layout != null ? Stops[Slot] : Path.Length;
            internal Position2 Destination => Layout != null ? Layout.Points[Slot] : Path.Definition.Points[^1];
        }

        private void ReserveBaseAttackCorridors()
        {
            var layout = Configuration.Objective.BaseAttack;
            if (layout == null || layout.LocalArea != null)
            {
                return;
            }

            foreach (var slot in Configuration.BuildSlots)
            {
                foreach (var path in layout.Paths)
                {
                    if (IntersectsAttackCorridor(slot.Position, path))
                    {
                        slots.Remove(slot.Id);
                        break;
                    }
                }
            }
        }

        private bool IntersectsAttackCorridor(Position2 position, BaseAttackPath path)
        {
            int clearance = TowerFoundationRadiusMillimetres + path.MaxBodyRadius;
            long required = (long)clearance * clearance;
            for (int i = 1; i < path.Points.Count; i++)
            {
                if (SegmentDistanceSquared(position, path.Points[i - 1], path.Points[i]) < required)
                {
                    return true;
                }
            }

            foreach (var route in routes.Values)
            {
                for (int lane = -1; lane <= 1; lane++)
                {
                    var entrance = route.Position(route.Length, lane);
                    if (SegmentDistanceSquared(position, entrance, path.Points[0]) < required)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ReachedObjective(Enemy enemy)
        {
            if (enemy.Distance < enemy.Route.Length)
            {
                return false;
            }

            var order = enemy.BaseAttack;
            var layout = Configuration.Objective.BaseAttack;
            if (layout?.LocalArea != null)
            {
                return enemy.AssaultOrder is { Arrived: true, Staging: false } && CanStrikeFrom(enemy.Position, enemy, layout);
            }

            return layout == null || (order != null && order.Distance >= order.Stop
                && (layout.Engagement == null || layout.Engagement.CanAttackFrom(enemy.Position, enemy.Definition.BodyRadius)));
        }

        private void AssignBaseAttack(Enemy enemy)
        {
            var layout = Configuration.Objective.BaseAttack;
            if (layout == null)
            {
                return;
            }

            if (layout.Engagement != null)
            {
                AssignReachableAttackPosition(enemy, layout);
                return;
            }

            BaseAttackPath chosen = null;
            int chosenSlot = -1;
            int bestPreference = int.MaxValue;
            foreach (var path in layout.Paths)
            {
                int preference = path.Side == enemy.Lane ? 0 : path.Side == 0 ? 2 : 1;
                if (enemy.Definition.BodyRadius > path.MaxBodyRadius || preference >= bestPreference)
                {
                    continue;
                }

                // Enter behind the rearmost reservation. That unit will move up as outer slots open.
                int before = path.Points.Count;
                foreach (var other in enemies)
                {
                    if (other.BaseAttack?.Layout == path && other.BaseAttack.Slot < before)
                    {
                        before = other.BaseAttack.Slot;
                    }
                }

                int slot = FindAttackSlot(enemy, path, path.FirstAttackPoint, before);
                if (slot >= 0 && HasClearAttackEntrance(enemy, path.Points[0]))
                {
                    chosen = path;
                    chosenSlot = slot;
                    bestPreference = preference;
                }
            }

            if (chosen != null)
            {
                enemy.BaseAttack = CreateAttackOrder(enemy.Position, chosen, chosenSlot);
            }
        }

        private void AssignReachableAttackPosition(Enemy enemy, BaseAttackLayout layout)
        {
            BaseAttackOrder chosen = null;
            int radius = enemy.Definition.BodyRadius;
            foreach (var path in layout.Paths)
            {
                int slot = path.FirstAttackPoint;
                if (radius > path.MaxBodyRadius || (chosen?.Layout.Side == 0 && path.Side != 0)
                    || !layout.Engagement.CanAttackFrom(path.Points[slot], radius) || !HasClearAttackPath(enemy, path))
                {
                    continue;
                }

                var candidate = CreateAttackOrder(enemy.Position, path, slot);
                // In the local approach, side 0 marks front positions. Within each tier prefer the shortest open route.
                if (chosen == null || (path.Side == 0 && chosen.Layout.Side != 0) || candidate.Path.Length < chosen.Path.Length)
                {
                    chosen = candidate;
                }
            }

            enemy.BaseAttack = chosen;
        }

        private bool HasClearAttackPath(Enemy enemy, BaseAttackPath path)
        {
            foreach (var other in enemies)
            {
                if (other == enemy)
                {
                    continue;
                }

                int clearance = enemy.Definition.BodyRadius + other.Definition.BodyRadius;
                long required = (long)clearance * clearance;
                var reservation = other.BaseAttack;
                bool following = path.Side != 0 && reservation != null && reservation.Layout.Side == path.Side
                    && reservation.Distance < reservation.Stops[reservation.Slot]
                    && path.Points.Count > 1 && reservation.Layout.Points.Count > 1
                    && reservation.Layout.Points[0].Equals(path.Points[0]) && reservation.Layout.Points[1].Equals(path.Points[1]);
                var from = enemy.Position;
                foreach (var point in path.Points)
                {
                    // Shared flank traffic can follow at body spacing. A reserved stop can still block the whole path.
                    if ((!following && SegmentDistanceSquared(other.Position, from, point) < required)
                        || (reservation != null && SegmentDistanceSquared(reservation.Destination, from, point) < required))
                    {
                        return false;
                    }

                    from = point;
                }

                if (reservation != null && BlocksReservedAttackPath(path.Points[path.FirstAttackPoint], other, required))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool BlocksReservedAttackPath(Position2 position, Enemy mover, long required)
        {
            var order = mover.BaseAttack;
            var from = mover.Position;
            int distance = 0;
            for (int i = 0; i < order.Path.SegmentLengths.Length; i++)
            {
                distance += order.Path.SegmentLengths[i];
                if (distance <= order.Distance)
                {
                    continue;
                }

                var to = order.Path.Definition.Points[i + 1];
                if (SegmentDistanceSquared(position, from, to) < required)
                {
                    return true;
                }

                from = to;
            }

            return false;
        }

        private void FillBaseAttackGaps()
        {
            var layout = Configuration.Objective.BaseAttack;
            if (layout == null || layout.Engagement != null)
            {
                return;
            }

            foreach (var path in layout.Paths)
            {
                int before = path.Points.Count;
                for (int slot = before - 1; slot >= path.FirstAttackPoint; slot--)
                {
                    var enemy = enemies.Find(value => value.BaseAttack?.Layout == path && value.BaseAttack.Slot == slot);
                    if (enemy == null)
                    {
                        continue;
                    }

                    int next = FindAttackSlot(enemy, path, slot + 1, before);
                    if (next >= 0)
                    {
                        enemy.BaseAttack.Slot = next;
                    }

                    before = enemy.BaseAttack.Slot;
                }
            }
        }

        private int FindAttackSlot(Enemy enemy, BaseAttackPath path, int first, int before)
        {
            for (int i = before - 1; i >= first; i--)
            {
                var position = path.Points[i];
                bool available = true;
                foreach (var other in enemies)
                {
                    var reservation = other.BaseAttack;
                    if (other == enemy || reservation == null)
                    {
                        continue;
                    }

                    int clearance = enemy.Definition.BodyRadius + other.Definition.BodyRadius;
                    if (DistanceSquared(position, reservation.Layout.Points[reservation.Slot]) < (long)clearance * clearance)
                    {
                        available = false;
                        break;
                    }
                }

                if (available)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool HasClearAttackEntrance(Enemy enemy, Position2 end)
        {
            foreach (var other in enemies)
            {
                int clearance = enemy.Definition.BodyRadius + other.Definition.BodyRadius;
                if (other != enemy && SegmentDistanceSquared(other.Position, enemy.Position, end) < (long)clearance * clearance)
                {
                    return false;
                }
            }

            return true;
        }

        private static BaseAttackOrder CreateAttackOrder(Position2 entrance, BaseAttackPath layout, int slot)
        {
            bool startsAtEntrance = entrance.Equals(layout.Points[0]);
            var points = startsAtEntrance ? layout.Points.ToArray() : new[] { entrance }.Concat(layout.Points).ToArray();
            var route = CreateRoute(new RouteDefinition { Points = points }, hasBase: false);
            var stops = new int[layout.Points.Count];
            int first = startsAtEntrance ? 1 : 0;
            int distance = 0;
            for (int i = first; i < stops.Length; i++)
            {
                distance += route.SegmentLengths[i - first];
                stops[i] = distance;
            }

            return new BaseAttackOrder { Layout = layout, Path = route, Slot = slot, Stops = stops };
        }
    }
}
