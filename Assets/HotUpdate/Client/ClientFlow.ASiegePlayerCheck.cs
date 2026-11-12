#if NOVORIZON_COMPILED_GAME
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;
using Game.HotUpdate.TowerDefense.Presentation;
using Game.Resource;
using UnityEngine;

namespace Game.HotUpdate.Client
{
    public sealed partial class ClientFlow
    {
        private bool StartASiegePlayerCheck()
        {
            const string option = "--novorizon-a-siege-check=";
            var argument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith(option, StringComparison.Ordinal));
            if (argument == null)
            {
                return false;
            }

            _ = CheckASiegePlayerAsync(argument.Substring(option.Length));
            return true;
        }

        private async Task CheckASiegePlayerAsync(string screenshotPath)
        {
            try
            {
                var plan = new ExpeditionPlan(tables, Stage.Id, 64029, Journey.Camp.Level.Id, navigation);
                using var map = await ResourceManager.Instance.AcquireAsync<Sprite>(plan.Stage.MapAsset, lifetime.Token, true);
                using var view = TowerDefensePrototypeView.Create(plan.Configuration, Art.Font, map.Asset, plan.Seed, art: Art);
                hub.gameObject.SetActive(false);
                view.SetArt(map.Asset, Art.Atlas, Art.Panel, Art.Font);
                view.SetNavigationDebug(true);
                var fight = view.Session;
                int initialHp = fight.BaseHp;
                if (Screen.fullScreen || fight.Navigation == null || !fight.StartNextWave())
                {
                    throw new InvalidOperationException("Expected a windowed, playable navigation fight.");
                }

                float deadline = Time.realtimeSinceStartup + 90;
                EnemySnapshot? first = null;
                while (Time.realtimeSinceStartup < deadline && fight.Phase == BattlePhase.Fighting)
                {
                    await Task.Yield();
                    var attackers = fight.Enemies.Where(enemy => enemy.MovementState == NavigationAgentStatus.Attacking).ToArray();
                    if (attackers.Length == 0)
                    {
                        continue;
                    }

                    first ??= attackers[0];
                    var anchored = attackers.FirstOrDefault(enemy => enemy.Id == first.Value.Id);
                    if (anchored.Id != first.Value.Id || anchored.Position.X != first.Value.Position.X || anchored.Position.Y != first.Value.Position.Y)
                    {
                        throw new InvalidOperationException("The first attacker lost its original position.");
                    }

                    if (attackers.Length < 2 || fight.BaseHp >= initialHp)
                    {
                        continue;
                    }

                    fight.SetPaused(true);
                    await Task.Yield();
                    foreach (var attacker in attackers)
                    {
                        var body = GameObject.Find("Enemy " + attacker.Id + " - Forest Raider");
                        var target = plan.Configuration.Navigation.TargetCenter;
                        var direction = new Vector3(target.X - attacker.Position.X, 0, target.Y - attacker.Position.Y);
                        if (body == null || Vector3.Dot(body.transform.forward, direction.normalized) < .99f)
                        {
                            throw new InvalidOperationException("An attacking model is not facing the base.");
                        }
                    }

                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(screenshotPath));
                    float captureDeadline = Time.realtimeSinceStartup + 3;
                    while (!File.Exists(screenshotPath) && Time.realtimeSinceStartup < captureDeadline)
                    {
                        await Task.Yield();
                    }

                    var positions = string.Join("; ", attackers.Select(enemy => $"{enemy.Id}=({enemy.Position.X},{enemy.Position.Y})"));
                    Debug.Log($"A_SIEGE_PLAYER_PASSED tick={fight.Tick} attack={attackers.Length} towers={fight.Towers.Count} hp={initialHp}->{fight.BaseHp} first={first.Value.Id} positions={positions}");
                    Application.Quit(0);
                    return;
                }

                throw new InvalidOperationException($"No-tower fight did not reach two attackers before stopping. tick={fight.Tick} hp={fight.BaseHp}");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Application.Quit(1);
            }
        }
    }
}
#endif
