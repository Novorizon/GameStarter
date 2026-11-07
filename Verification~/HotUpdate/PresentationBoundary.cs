using System;
using System.Threading;
using System.Threading.Tasks;
namespace Game.HotUpdate.Client
{
    // Native UI and leases are verified in real Unity, not this API model.
    public sealed class ClientFlow : IDisposable
    {
        public Task StartAsync(cfg.Tables tables, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _ = TowerDefense.Data.TowerDefenseTables.Create(tables, 1);
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}
