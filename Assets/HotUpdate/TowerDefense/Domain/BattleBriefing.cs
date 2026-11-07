using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed class EnemyBriefing
    {
        public int Id { get; }
        public string Name { get; }
        public int Count { get; }

        internal EnemyBriefing(int id, string name, int count)
        {
            Id = id;
            Name = name;
            Count = count;
        }
    }

    public sealed class WaveBriefing
    {
        public int Number { get; }
        public int RewardGold { get; }
        public bool OffersCard { get; }
        public int EnemyCount { get; }
        public IReadOnlyList<EnemyBriefing> Enemies { get; }

        internal WaveBriefing(WaveDefinition wave, int number, int waveCount, IReadOnlyDictionary<int, EnemyDefinition> definitions)
        {
            Number = number;
            RewardGold = wave.RewardGold;
            OffersCard = wave.GiveCard && number < waveCount;
            var enemies = wave.Groups.GroupBy(group => group.EnemyId).OrderBy(group => group.Key)
                .Select(group => new EnemyBriefing(group.Key, definitions[group.Key].Name, group.Sum(item => item.Count))).ToArray();
            Enemies = Array.AsReadOnly(enemies);
            EnemyCount = enemies.Sum(enemy => enemy.Count);
        }
    }

    /// <summary>从实际战斗配置投影情报；准备页和局内预告共用同一份规则数据。</summary>
    public sealed class BattleBriefing
    {
        public IReadOnlyList<WaveBriefing> Waves { get; }
        public int EnemyCount { get; }

        public BattleBriefing(FightConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var definitions = configuration.Enemies.ToDictionary(enemy => enemy.Id);
            var waves = configuration.Waves.Select((wave, index) => new WaveBriefing(wave, index + 1, configuration.Waves.Length, definitions)).ToArray();
            Waves = Array.AsReadOnly(waves);
            EnemyCount = waves.Sum(wave => wave.EnemyCount);
        }
    }
}
