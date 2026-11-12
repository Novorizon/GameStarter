using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    public sealed partial class GameEntry
    {
        internal Task StartupTask { get; private set; }

        partial void ObserveStartup(Task startup, CancellationToken token)
        {
            StartupTask = startup;
        }
    }
}
