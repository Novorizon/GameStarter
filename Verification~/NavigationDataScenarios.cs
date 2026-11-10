using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal static partial class Program
{
    private static void NavigationDataScenarios()
    {
        Check("actual navigation resource and enemy bodies are bound to the authored stage", () =>
        {
            var grid = NavigationLayout.Parse(File.ReadAllText("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
            var config = TowerDefenseTables.Create(ReadTowerDefenseTables(), 1, grid);
            Equal(44, grid.Width);
            Equal(30, grid.Height);
            True(config.Enemies.Select(enemy => enemy.Radius).Distinct().Count() > 1);
            True(config.Enemies.All(enemy => enemy.AttackIntervalTicks > 0 && enemy.AttackRange > 0));
            True(new FightSession(config).Navigation != null);
        });
        Check("every authored wave reaches the base without towers while the base stays alive", () =>
        {
            var tables = ReadTowerDefenseTables();
            var grid = NavigationLayout.Parse(File.ReadAllText("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
            int count = tables.TbTdWave.DataList.Count(row => row.StageId == 1);
            for (int index = 0; index < count; index++)
            {
                var config = TowerDefenseTables.Create(tables, 1, grid);
                config.BaseHealth = int.MaxValue;
                config.Waves = new[] { config.Waves[index] };
                int expected = config.Waves[0].Groups.Sum(group => group.Count);
                var fight = new FightSession(config, 917);
                True(fight.StartNextWave());
                bool arrived = false;
                var timer = Stopwatch.StartNew();
                long maximumStep = 0;
                for (int tick = 0; tick < 30000; tick++)
                {
                    long started = Stopwatch.GetTimestamp();
                    fight.Step();
                    maximumStep = Math.Max(maximumStep, Stopwatch.GetTimestamp() - started);
                    True(fight.Phase == BattlePhase.Fighting && fight.Enemies.Count == fight.Navigation.Agents.Count);
                    if (fight.TotalSpawned == expected && fight.Navigation.Agents.All(agent => agent.RemainingDistance == 0))
                    {
                        arrived = true;
                        break;
                    }
                }

                if (!arrived)
                {
                    throw new Exception("Authored wave " + (index + 1) + " did not gather: admitted=" + fight.TotalSpawned
                        + "/" + expected + ", blockedTicks=" + fight.SpawnBlockedTicks + ", tick=" + fight.Tick);
                }

                Equal(expected, fight.Enemies.Count);
                True(fight.SpawnBlockedTicks <= fight.Tick);
                Equal(0, fight.Kills);
                Equal(0, fight.Leaks);
                Equal(0, fight.Towers.Count);
                True(fight.Navigation.Agents.Any(agent => agent.Status == NavigationAgentStatus.Attacking));
                foreach (var agent in fight.Navigation.Agents)
                {
                    Equal(agent.GoalCell, grid.CellAt(agent.Position));
                    True(grid.CanOccupy(agent.GoalCell, agent.Radius));
                    if (agent.Status == NavigationAgentStatus.Attacking)
                    {
                        True(grid.HasAttackLine(agent.GoalCell, agent.Radius, agent.AttackRange));
                    }
                }

                var sequences = fight.Enemies.ToDictionary(enemy => enemy.Id, enemy => enemy.AttackSequence);
                int health = fight.BaseHp;
                fight.Step(100);
                True(fight.BaseHp < health && fight.Enemies.Count == expected);
                foreach (var enemy in fight.Enemies.Where(enemy => enemy.MovementState == NavigationAgentStatus.Waiting))
                {
                    Equal(sequences[enemy.Id], enemy.AttackSequence);
                }

                if (index == 4)
                {
                    // 所有抵达自身槽位的单位 RemainingDistance 都为零；不能用它将候场者当成基地威胁。
                    config.Towers[0].Range = 50000;
                    config.Towers[0].Damage = 1;
                    var target = fight.Enemies.Where(enemy => enemy.MovementState == NavigationAgentStatus.Attacking)
                        .OrderBy(enemy => (long)(enemy.Position.X - grid.TargetCenter.X) * (enemy.Position.X - grid.TargetCenter.X)
                            + (long)(enemy.Position.Y - grid.TargetCenter.Y) * (enemy.Position.Y - grid.TargetCenter.Y))
                        .ThenBy(enemy => enemy.Id).First();
                    True(fight.BuildTower(config.BuildSlots[0].Id, config.Towers[0].Id));
                    fight.Step();
                    Equal(target.Health - 1, fight.Enemies.Single(enemy => enemy.Id == target.Id).Health);
                }

                Console.WriteLine("NAV_AUTHORED_WAVE " + (index + 1) + " units=" + expected + " tick=" + fight.Tick
                    + " attacking=" + fight.Enemies.Count(enemy => enemy.MovementState == NavigationAgentStatus.Attacking)
                    + " waiting=" + fight.Enemies.Count(enemy => enemy.MovementState == NavigationAgentStatus.Waiting)
                    + " elapsedMs=" + timer.ElapsedMilliseconds + " maxStepMs=" + (maximumStep * 1000.0 / Stopwatch.Frequency).ToString("F2"));
            }
        });
        Check("actual navigation pauses without advancing reservations and replays across tick batching", () =>
        {
            var tables = ReadTowerDefenseTables();
            var grid = NavigationLayout.Parse(File.ReadAllText("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
            var config = TowerDefenseTables.Create(tables, 1, grid);
            config.BaseHealth = int.MaxValue;
            config.Waves = new[] { config.Waves[4] };
            var first = new FightSession(config, 917);
            var second = new FightSession(config, 917);
            True(first.StartNextWave() && second.StartNextWave());
            first.Step(500);
            string paused = NavigationDigest(first);
            first.SetPaused(true);
            first.Step(2000);
            Equal(paused, NavigationDigest(first));
            first.SetPaused(false);
            first.Step(1000);
            for (int tick = 0; tick < 1500; tick += 5)
            {
                second.Step(5);
            }

            Equal(NavigationDigest(first), NavigationDigest(second));
            Equal(first.Tick, first.Navigation.CurrentTick);
            var fresh = new FightSession(config, 917);
            True(fresh.Navigation.Agents.Count == 0 && fresh.Navigation.CurrentTick == 0 && fresh.Navigation.ReservationCount == 0);
        });
        Check("actual navigation releases killed enemies and survives consecutive waves", () =>
        {
            var tables = ReadTowerDefenseTables();
            var grid = NavigationLayout.Parse(File.ReadAllText("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
            var config = TowerDefenseTables.Create(tables, 1, grid);
            config.BaseHealth = int.MaxValue;
            config.InitialGold = 100000;
            config.Towers[0].Damage = 100000;
            config.Towers[0].Range = 50000;
            config.Towers[0].AttackIntervalTicks = 1;
            var fight = new FightSession(config, 917);
            True(fight.BuildTower(config.BuildSlots[0].Id, config.Towers[0].Id));
            for (int wave = 0; wave < config.Waves.Length; wave++)
            {
                True(fight.StartNextWave());
                for (int tick = 0; tick < 5000 && fight.Phase == BattlePhase.Fighting; tick++)
                {
                    fight.Step();
                    Equal(fight.Enemies.Count, fight.Navigation.Agents.Count);
                }

                Equal(0, fight.Navigation.Agents.Count);
                Equal(0, fight.Navigation.ReservationCount);
                Equal(fight.Tick, fight.Navigation.CurrentTick);
                if (fight.Phase == BattlePhase.ChoosingCard)
                {
                    True(fight.ChooseCard(0));
                }
            }

            Equal(BattlePhase.Victory, fight.Phase);
            Equal(config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count)), fight.Kills);
            Equal(0, fight.Leaks);
        });
        Check("merged authored waves report bounded admission and resume after kills without hiding living units", () =>
        {
            var tables = ReadTowerDefenseTables();
            var grid = NavigationLayout.Parse(File.ReadAllText("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
            var config = TowerDefenseTables.Create(tables, 1, grid);
            int requested = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
            var groups = config.Waves.SelectMany(wave => wave.Groups).Select(group => new SpawnGroup
            {
                EnemyId = group.EnemyId,
                RouteId = group.RouteId,
                Count = group.Count,
                StartTick = 0,
                IntervalTicks = 1
            }).ToArray();
            config.Waves = new[] { new WaveDefinition { Id = 1, Groups = groups } };
            config.BaseHealth = int.MaxValue;
            config.InitialGold = 100000;
            var fight = new FightSession(config, 917);
            True(fight.StartNextWave());
            bool bounded = false;
            for (int tick = 0; tick < 6000; tick++)
            {
                fight.Step();
                Equal(fight.TotalSpawned, fight.Enemies.Count);
                if (fight.PendingSpawns > 0 && fight.Navigation.LastFailure == NavigationFailure.CapacityExceeded
                    && fight.Navigation.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating))
                {
                    bounded = true;
                    break;
                }
            }

            True(bounded && fight.TotalSpawned >= 30 && fight.TotalSpawned < requested);
            Equal(requested, fight.TotalSpawned + fight.PendingSpawns);
            Equal(0, fight.Leaks);
            Equal(0, fight.Kills);
            int admitted = fight.TotalSpawned;
            long expanded = fight.Navigation.TotalExpandedNodes;
            fight.Step(200);
            Equal(admitted, fight.TotalSpawned);
            long stableExpanded = fight.Navigation.TotalExpandedNodes - expanded;
            Equal(0L, stableExpanded);
            config.Towers[0].Range = 50000;
            config.Towers[0].Damage = 100000;
            config.Towers[0].AttackIntervalTicks = 1;
            True(fight.BuildTower(config.BuildSlots[0].Id, config.Towers[0].Id));
            fight.Step(200);
            True(fight.TotalSpawned > admitted && fight.Kills > 0 && fight.Leaks == 0);
            Equal(fight.TotalSpawned - fight.Kills, fight.Enemies.Count);
            Equal(fight.Enemies.Count, fight.Navigation.Agents.Count);
            for (int tick = 0; tick < 6000 && fight.Phase == BattlePhase.Fighting; tick++)
            {
                fight.Step();
            }

            Equal(BattlePhase.Victory, fight.Phase);
            Equal(requested, fight.TotalSpawned);
            Equal(requested, fight.Kills);
            Equal(0, fight.PendingSpawns);
            Equal(0, fight.Navigation.Agents.Count);
            Equal(0, fight.Navigation.ReservationCount);
            Console.WriteLine("NAV_AUTHORED_MERGED requested=" + requested + " safeAdmittedBeforeKills=" + admitted
                + " queued=" + (requested - admitted) + " expandedWhileStable=" + stableExpanded + " finalKills=" + fight.Kills);
        });
    }

    private static string NavigationDigest(FightSession fight)
    {
        return fight.Tick + ":" + fight.BaseHp + ":" + fight.TotalSpawned + ":" + fight.SpawnBlockedTicks + ":"
            + fight.Navigation.ReservationCount + ":" + string.Join(";", fight.Navigation.Agents.Select(agent => agent.Id + "," + agent.Position.X
                + "," + agent.Position.Y + "," + agent.GoalCell + "," + agent.Status + "," + agent.ArrivalTick));
    }
}
