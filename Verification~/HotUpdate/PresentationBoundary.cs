using System.Threading;
using System.Threading.Tasks;
namespace Game.HotUpdate
{
    // uGUI/TMP rendering and YooAsset UI leases are exercised in real Unity, not this API model.
    public sealed partial class HotGame
    {
        private Task StartTowerDefenseAsync(cfg.Tables tables, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _ = TowerDefense.Data.TowerDefenseTables.Create(tables, 1);
            return Task.CompletedTask;
        }
        private void StopTowerDefense() { }
    }
}
