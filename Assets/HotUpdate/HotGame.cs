using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更入口的类定义。GameEntry 在基础服务就绪后调用 HotUpdateManager，加载程序集并创建本类的对象。
    /// 本类是普通 C# 对象，不挂到 GameObject 上，也不会作为组件显示在 Hierarchy。
    /// 项目可更新的初始化逻辑从这里添加；公共 Manager 继续由 AOT 层的 GameEntry 初始化。
    /// </summary>
    public sealed class HotGame : IGameModule
    {
        private const string Version = "1.0.0";
        private IGameHost host;
        private bool stopped;
        public Task InitializeAsync(IGameHost gameHost, CancellationToken token)
        {
            if (gameHost == null)
            {
                throw new ArgumentNullException(nameof(gameHost));
            }

            token.ThrowIfCancellationRequested();
            if (stopped)
            {
                throw new OperationCanceledException("Hot update module has stopped.");
            }

            host = gameHost;
            // 在这里等待项目的热更初始化完成，例如读取玩法配置或打开首个业务界面。
            host.Log("HOT_UPDATE_READY " + Version);
            host.ReportReady(Version);
            return Task.CompletedTask;
        }

        public void Shutdown()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            // 在这里释放热更代码持有的资源和订阅；公共 Manager 的生命周期由 AOT 层和库管理。
            host?.Log("HOT_UPDATE_STOPPED");
            host = null;
        }
    }
}
