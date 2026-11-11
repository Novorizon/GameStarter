using System;
using System.Collections.Generic;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int AttackVacancyCandidateLimit = 24;
        private const int AttackVacancyBlockerLimit = 12;
        private const int AttackVacancyProbeBudget = 4;

        private sealed class AttackVacancy
        {
            internal readonly Position2 Position;
            internal readonly int Radius;
            internal int ExhaustedLayoutVersion;

            internal AttackVacancy(Position2 position, int radius)
            {
                Position = position;
                Radius = radius;
            }
        }

        private enum VacancyCoverage
        {
            Open,
            Reserved,
            Occupied
        }

        private readonly List<AttackVacancy> attackVacancies = new List<AttackVacancy>();
        private readonly List<Enemy> vacancyReserves = new List<Enemy>();
        private Position2[] yieldingAttackPath;
        private AttackVacancy activeAttackVacancy;
        private AttackVacancy probedVacancy;
        private int vacancyProbeVersion;
        private int nextVacancyPair;

        private void RecordAttackVacancy(Enemy enemy)
        {
            if (!attackVacancies.Exists(vacancy => vacancy.Position.Equals(enemy.Position) && vacancy.Radius == enemy.Radius))
            {
                attackVacancies.Add(new AttackVacancy(enemy.Position, enemy.Radius));
            }
        }

        private void RefreshAttackVacancy()
        {
            var previous = activeAttackVacancy;
            activeAttackVacancy = null;
            for (int index = attackVacancies.Count - 1; index >= 0; index--)
            {
                var vacancy = attackVacancies[index];
                var coverage = GetVacancyCoverage(vacancy);
                if (coverage == VacancyCoverage.Occupied)
                {
                    attackVacancies.RemoveAt(index);
                }
                else if (coverage == VacancyCoverage.Open && vacancy.ExhaustedLayoutVersion != attackLayoutVersion && activeAttackVacancy == null)
                {
                    activeAttackVacancy = vacancy;
                }
            }

            releasedAttackPosition = activeAttackVacancy?.Position;
            if (activeAttackVacancy == null)
            {
                probedVacancy = null;
                vacancyReserves.Clear();
            }
            else if (activeAttackVacancy != previous)
            {
                checkedStaging.Clear();
            }
        }

        private VacancyCoverage GetVacancyCoverage(AttackVacancy vacancy)
        {
            var coverage = VacancyCoverage.Open;
            foreach (var enemy in enemies)
            {
                if (!enemy.AtAttackPosition && (!enemy.AttackDestination || enemy.Maneuver == null))
                {
                    continue;
                }

                var point = enemy.AtAttackPosition ? enemy.Position : enemy.Maneuver[^1];
                long clearance = vacancy.Radius + enemy.Radius + TrafficGap;
                if (DistanceSquared(vacancy.Position, point) < clearance * clearance)
                {
                    if (enemy.AtAttackPosition)
                    {
                        return VacancyCoverage.Occupied;
                    }

                    coverage = VacancyCoverage.Reserved;
                }
            }

            return coverage;
        }

        private void ResetAttackVacancyProbe()
        {
            vacancyProbeVersion = 0;
            if (attackVacancies.Count > 0)
            {
                checkedStaging.Clear();
            }

            foreach (var vacancy in attackVacancies)
            {
                vacancy.ExhaustedLayoutVersion = 0;
            }
        }

        private bool TryClearAttackVacancy()
        {
            var vacancy = activeAttackVacancy;
            if (vacancyProbeVersion != attackLayoutVersion || probedVacancy != vacancy)
            {
                probedVacancy = vacancy;
                vacancyProbeVersion = attackLayoutVersion;
                nextVacancyPair = 0;
                vacancyReserves.Clear();
                if (TryRelayAttackVacancy(vacancy.Position))
                {
                    return true;
                }

                foreach (var enemy in enemies)
                {
                    if (enemy.HasStagingPosition)
                    {
                        vacancyReserves.Add(enemy);
                    }
                }

                vacancyReserves.Sort((first, second) =>
                {
                    int order = DistanceSquared(first.Position, vacancy.Position).CompareTo(DistanceSquared(second.Position, vacancy.Position));
                    return order == 0 ? first.Id.CompareTo(second.Id) : order;
                });
            }

            int blockerCount = Math.Min(AttackVacancyBlockerLimit, vacancyReserves.Count);
            int pairCount = Math.Min(AttackVacancyCandidateLimit, vacancyReserves.Count) * blockerCount;
            int attempts = 0;
            while (nextVacancyPair < pairCount && attempts < AttackVacancyProbeBudget)
            {
                var reserve = vacancyReserves[nextVacancyPair / blockerCount];
                var blocker = vacancyReserves[nextVacancyPair % blockerCount];
                nextVacancyPair++;
                if (reserve == blocker || !reserve.HasStagingPosition || !blocker.HasStagingPosition)
                {
                    continue;
                }

                attempts++;
                if (TryYieldPosition(reserve, blocker, vacancy.Position))
                {
                    return true;
                }
            }

            if (nextVacancyPair == pairCount)
            {
                vacancy.ExhaustedLayoutVersion = attackLayoutVersion;
                vacancyReserves.Clear();
            }

            return false;
        }

        private bool TryResumeAttackVacancyYield(Enemy reserve, Position2[] path)
        {
            // Revalidate the saved attack route because construction or casualties may have changed the field during the yield.
            var grid = navigationGrids[reserve.Radius];
            CollectPathObstacles(reserve);
            var from = reserve.Position;
            foreach (var next in path)
            {
                if (!grid.CanSweep(from, next) || BlocksAny(pathObstacles, from, next, reserve.Radius))
                {
                    return TryPlanAssaultMove(reserve, false);
                }

                from = next;
            }

            return StartManeuver(reserve, path, true);
        }
    }
}
