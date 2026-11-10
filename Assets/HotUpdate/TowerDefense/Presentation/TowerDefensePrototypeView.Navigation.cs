using System;
using System.Collections.Generic;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.HotUpdate.TowerDefense.Presentation
{
    public sealed partial class TowerDefensePrototypeView
    {
        private sealed class EnemyNavigationVisual
        {
            internal GameObject Owner;
            internal LineRenderer Footprint;
            internal LineRenderer Attack;
            internal LineRenderer Goal;
            internal int AttackSequence;
            internal float AttackPulse;
        }

        private readonly Dictionary<int, EnemyNavigationVisual> enemyNavigationVisuals = new Dictionary<int, EnemyNavigationVisual>();
        private GameObject navigationGridVisual;
        private TMP_Text navigationLegend;
        private GameObject navigationLegendPanel;
        private bool navigationDebugVisible;
        private int navigationToggleFrame = -1;

        public bool NavigationDebugVisible => navigationDebugVisible;

        /// <summary>显示实际逻辑格与身体半径；只影响表现，不修改导航或碰撞规则。</summary>
        public void SetNavigationDebug(bool visible)
        {
            navigationDebugVisible = visible && configuration?.Navigation != null;
            if (navigationDebugVisible && navigationGridVisual == null)
            {
                CreateNavigationGridVisual();
            }

            if (navigationGridVisual != null)
            {
                navigationGridVisual.SetActive(navigationDebugVisible);
            }

            if (navigationLegendPanel != null)
            {
                navigationLegendPanel.SetActive(navigationDebugVisible);
            }

            foreach (var visual in enemyNavigationVisuals.Values)
            {
                if (visual.Footprint != null)
                {
                    visual.Footprint.enabled = navigationDebugVisible;
                    visual.Goal.enabled = navigationDebugVisible;
                }
            }
        }

        private void SynchronizeEnemyNavigation(EnemySnapshot state, EnemyView view, float elapsed)
        {
            if (configuration.Navigation == null)
            {
                return;
            }

            if (!enemyNavigationVisuals.TryGetValue(state.Id, out var visual) || !ReferenceEquals(visual.Owner, view.Root))
            {
                var footprint = new GameObject("NavigationFootprint").AddComponent<LineRenderer>();
                footprint.transform.SetParent(view.Root.transform, false);
                footprint.sharedMaterial = lineMaterial;
                footprint.useWorldSpace = false;
                footprint.loop = true;
                footprint.positionCount = 32;
                footprint.startWidth = .035f;
                footprint.endWidth = .035f;
                for (int i = 0; i < footprint.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2 / footprint.positionCount;
                    footprint.SetPosition(i, new Vector3(Mathf.Cos(angle) * .38f, .025f, Mathf.Sin(angle) * .38f));
                }

                var attack = new GameObject("BaseAttackPulse").AddComponent<LineRenderer>();
                attack.transform.SetParent(view.Root.transform, false);
                attack.sharedMaterial = lineMaterial;
                attack.useWorldSpace = true;
                attack.positionCount = 2;
                attack.startWidth = .035f;
                attack.endWidth = .015f;
                attack.startColor = new Color(1, .55f, .25f);
                attack.endColor = new Color(1, .85f, .4f);
                var goal = new GameObject("ReservedNavigationGoal").AddComponent<LineRenderer>();
                goal.transform.SetParent(view.Root.transform, false);
                goal.sharedMaterial = lineMaterial;
                goal.useWorldSpace = true;
                goal.loop = true;
                goal.positionCount = 16;
                goal.startWidth = .025f;
                goal.endWidth = .025f;
                visual = new EnemyNavigationVisual { Owner = view.Root, Footprint = footprint, Attack = attack, Goal = goal };
                enemyNavigationVisuals[state.Id] = visual;
            }

            var target = World(configuration.Navigation.TargetCenter, .05f);
            var direction = target - view.Root.transform.position;
            direction.y = 0;
            if (state.MovementState != NavigationAgentStatus.Navigating && direction.sqrMagnitude > .0001f)
            {
                view.Root.transform.rotation = Quaternion.LookRotation(direction);
            }

            Color color = state.MovementState == NavigationAgentStatus.Attacking ? new Color(1, .55f, .25f)
                : state.MovementState == NavigationAgentStatus.Waiting ? new Color(.35f, .8f, 1) : new Color(.65f, 1, .45f);
            visual.Footprint.startColor = color;
            visual.Footprint.endColor = color;
            visual.Footprint.enabled = navigationDebugVisible;
            visual.Goal.enabled = navigationDebugVisible;
            if (navigationDebugVisible)
            {
                foreach (var agent in session.Navigation.Agents)
                {
                    if (agent.Id != state.Id)
                    {
                        continue;
                    }

                    var goalCenter = World(configuration.Navigation.Center(agent.GoalCell), .09f);
                    for (int i = 0; i < visual.Goal.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2 / visual.Goal.positionCount;
                        visual.Goal.SetPosition(i, goalCenter + new Vector3(Mathf.Cos(angle) * .12f, 0, Mathf.Sin(angle) * .12f));
                    }

                    visual.Goal.startColor = color;
                    visual.Goal.endColor = color;
                    break;
                }
            }
            if (state.AttackSequence != visual.AttackSequence)
            {
                visual.AttackSequence = state.AttackSequence;
                visual.AttackPulse = .14f;
            }

            visual.AttackPulse = Mathf.Max(0, visual.AttackPulse - elapsed);
            visual.Attack.enabled = state.MovementState == NavigationAgentStatus.Attacking && visual.AttackPulse > 0;
            if (visual.Attack.enabled)
            {
                var targetEdge = target - direction.normalized * (configuration.Navigation.TargetRadius / 1000f);
                visual.Attack.SetPosition(0, view.Root.transform.position + Vector3.up * .20f);
                visual.Attack.SetPosition(1, targetEdge + Vector3.up * .30f);
            }
        }

        private void SynchronizeNavigationDebug()
        {
            if (configuration.Navigation == null)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame && navigationToggleFrame != Time.frameCount)
            {
                navigationToggleFrame = Time.frameCount;
                SetNavigationDebug(!navigationDebugVisible);
            }

            if (navigationDebugVisible && navigationLegend != null)
            {
                int approaching = 0;
                int waiting = 0;
                int attacking = 0;
                foreach (var enemy in session.Enemies)
                {
                    if (enemy.MovementState == NavigationAgentStatus.Attacking)
                    {
                        attacking++;
                    }
                    else if (enemy.MovementState == NavigationAgentStatus.Waiting)
                    {
                        waiting++;
                    }
                    else
                    {
                        approaching++;
                    }
                }

                string pendingReason = session.PendingSpawns > 0 && session.Navigation.LastFailure != NavigationFailure.None
                    ? "\n准入等待：" + session.Navigation.LastFailure : "";
                navigationLegend.text = "F3 逻辑导航  ·  前进 " + approaching + "  /  集结 " + waiting + "  /  攻击 " + attacking
                    + "  /  待生成 " + session.PendingSpawns + pendingReason
                    + "\n绿格：最大身体可通行  ·  身体圆：实际半径  ·  小圆：预留槽位  ·  蓝：候场  ·  橙：攻击";
            }
        }

        private void CreateNavigationGridVisual()
        {
            var grid = configuration.Navigation;
            navigationGridVisual = new GameObject("LogicalNavigationGrid");
            navigationGridVisual.transform.SetParent(field, false);
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            float half = grid.CellSize / 2000f;
            int radius = 1;
            foreach (var enemy in configuration.Enemies)
            {
                radius = Math.Max(radius, enemy.Radius);
            }

            for (int cell = 0; cell < grid.Width * grid.Height; cell++)
            {
                var center = World(grid.Center(cell), .075f);
                bool walkable = grid.CanOccupy(cell, radius);
                Color color = walkable ? new Color(.35f, 1, .45f, .36f) : new Color(1, .25f, .2f, .18f);
                var corners = new[]
                {
                    center + new Vector3(-half, 0, -half), center + new Vector3(-half, 0, half),
                    center + new Vector3(half, 0, half), center + new Vector3(half, 0, -half)
                };
                for (int edge = 0; edge < corners.Length; edge++)
                {
                    indices.Add(vertices.Count);
                    vertices.Add(corners[edge]);
                    colors.Add(color);
                    indices.Add(vertices.Count);
                    vertices.Add(corners[(edge + 1) % corners.Length]);
                    colors.Add(color);
                }
            }

            var mesh = new Mesh { name = "Logical navigation cell outlines" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            generatedMeshes.Add(mesh);
            navigationGridVisual.AddComponent<MeshFilter>().sharedMesh = mesh;
            navigationGridVisual.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(Color.white, true);
            var panel = Panel("NavigationLegend", safeArea, new Vector2(.27f, .79f), new Vector2(.78f, .915f), Color.white);
            navigationLegendPanel = panel.gameObject;
            navigationLegend = Label("NavigationStatus", panel, new Vector2(.035f, .10f), new Vector2(.965f, .90f), 20, TextAnchor.MiddleLeft);
            navigationLegend.color = ink;
        }
    }
}
