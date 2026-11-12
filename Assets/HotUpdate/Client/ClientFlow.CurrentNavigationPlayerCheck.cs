#if NOVORIZON_CURRENT_NAVIGATION_CHECK
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Presentation;
using UnityEngine;

namespace Game.HotUpdate.Client
{
    public sealed partial class ClientFlow
    {
        private bool StartCurrentNavigationPlayerCheck()
        {
            const string option = "--novorizon-a-current-check=";
            var argument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith(option, StringComparison.Ordinal));
            if (argument == null)
            {
                return false;
            }

            _ = CheckCurrentNavigationPlayerAsync(argument.Substring(option.Length));
            return true;
        }

        private async Task CheckCurrentNavigationPlayerAsync(string screenshotPath)
        {
            var leases = new List<IDisposable>();
            try
            {
                string centerPath = Path.GetFullPath(screenshotPath);
                string folder = Path.GetDirectoryName(centerPath);
                string fileName = Path.GetFileNameWithoutExtension(centerPath);
                string basePath = Path.Combine(folder, fileName + "-base.png");
                string crowdPath = Path.Combine(folder, fileName + "-crowd.png");
                Directory.CreateDirectory(folder);
                if (File.Exists(centerPath) || File.Exists(basePath) || File.Exists(crowdPath))
                {
                    throw new InvalidOperationException("Use fresh screenshot paths for the current navigation check.");
                }

                const int stageId = 1;
                var plan = new ExpeditionPlan(tables, stageId, 64029, Journey.Camp.Level.Id, Art.Campaign, new LocalCampaignData(), mapSettings[stageId]);
                var assets = await LoadBattleAssetsAsync(plan, leases, lifetime.Token);
                using var view = TowerDefensePrototypeView.Create(plan.Configuration, assets.Font, assets.Map, plan.Seed, leases, Art, assets.HudAtlas);
                view.transform.SetParent(root.transform, false);
                view.SetArt(assets.Map, Art.Atlas, assets.Font);
                hub.gameObject.SetActive(false);

                var fight = view.Session;
                int initialHp = fight.BaseHp;
                if (Screen.fullScreen || !fight.UsesACrowd || !plan.Configuration.ForestRedrawEntrance.HasValue || !fight.StartNextWave())
                {
                    throw new InvalidOperationException("Expected a windowed Forest Outpost battle using A navigation.");
                }

                Debug.Log($"A_REDRAW_PLAYER_READY map={assets.Map.name} cell={plan.Configuration.AGrid.CellSize} hp={initialHp} towers={fight.Towers.Count}");
                float deadline = Time.realtimeSinceStartup + 150;
                var previousCenterPositions = new Dictionary<int, Position2>();
                bool capturedCenter = false;
                bool capturedBase = false;
                long lastTick = -1;
                long lastAdmissionTick = 0;
                int observedEnemies = 0;
                while (Time.realtimeSinceStartup < deadline && fight.Phase == BattlePhase.Fighting)
                {
                    await Task.Yield();
                    if (lastTick == fight.Tick)
                    {
                        continue;
                    }

                    lastTick = fight.Tick;
                    if (fight.Enemies.Count != observedEnemies)
                    {
                        observedEnemies = fight.Enemies.Count;
                        lastAdmissionTick = fight.Tick;
                    }

                    if (!capturedCenter)
                    {
                        var center = fight.Enemies.Where(enemy => OnCenterRoad(enemy.Position)).ToArray();
                        bool moving = center.Any(enemy => previousCenterPositions.TryGetValue(enemy.Id, out var previous)
                            && !previous.Equals(enemy.Position));
                        if (moving)
                        {
                            await CaptureNavigationPlayerAsync(fight, centerPath);
                            Debug.Log($"A_REDRAW_PLAYER_BRIDGE tick={fight.Tick} enemies={Positions(center)}");
                            capturedCenter = true;
                        }

                        previousCenterPositions.Clear();
                        foreach (var enemy in center)
                        {
                            previousCenterPositions.Add(enemy.Id, enemy.Position);
                        }
                    }

                    if (capturedCenter && !capturedBase && fight.BaseHp < initialHp)
                    {
                        var attackers = fight.Enemies.Where(enemy => enemy.AttackingBase).ToArray();
                        if (attackers.Length == 0)
                        {
                            continue;
                        }

                        await CaptureNavigationPlayerAsync(fight, basePath);
                        Debug.Log($"A_REDRAW_PLAYER_BASE tick={fight.Tick} attackers={Positions(attackers)} hp={initialHp}->{fight.BaseHp}");
                        capturedBase = true;
                    }

                    if (fight.WaveIndex >= 3 && fight.Tick - lastAdmissionTick >= 200)
                    {
                        int attacking = fight.Enemies.Count(enemy => enemy.AttackingBase);
                        await CaptureNavigationPlayerAsync(fight, crowdPath);
                        Debug.Log($"A_REDRAW_PLAYER_CAPTURED tick={fight.Tick} batch={fight.WaveIndex} enemies={fight.Enemies.Count} attacking={attacking} hp={fight.BaseHp}");
                        Application.Quit(capturedCenter && capturedBase ? 0 : 1);
                        return;
                    }
                }

                throw new InvalidOperationException(
                    $"No-tower check incomplete. center={capturedCenter} base={capturedBase} tick={fight.Tick} enemies={fight.Enemies.Count}");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Application.Quit(1);
            }
            finally
            {
                foreach (var lease in leases)
                {
                    lease.Dispose();
                }
            }
        }

        private static bool OnCenterRoad(Position2 point)
        {
            return point.X >= -2250 && point.X <= -750 && Math.Abs(point.Y - (-560)) <= 250;
        }

        private static string Positions(IEnumerable<EnemySnapshot> enemies)
        {
            return string.Join(", ", enemies.Select(enemy => $"{enemy.Id}=({enemy.Position.X},{enemy.Position.Y})"));
        }

        private static async Task CaptureNavigationPlayerAsync(FightSession fight, string path)
        {
            fight.SetPaused(true);
            try
            {
                await Task.Yield();
                ScreenCapture.CaptureScreenshot(path);
                float deadline = Time.realtimeSinceStartup + 5;
                while ((!File.Exists(path) || new FileInfo(path).Length == 0) && Time.realtimeSinceStartup < deadline)
                {
                    await Task.Yield();
                }

                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    throw new IOException("Failed to capture player screenshot: " + path);
                }
            }
            finally
            {
                fight.SetPaused(false);
            }
        }
    }
}
#endif
