using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更入口的类定义。GameEntry 在基础服务就绪后调用 HotUpdateManager，加载程序集并创建本类的对象。
    /// 本类是普通 C# 对象，不挂到 GameObject 上，也不会作为组件显示在 Hierarchy。
    /// 业务系统和首场景的启动从 InitializeAsync 添加，业务类可以放在本程序集的其他文件中。
    /// 当前示例的公共 Manager 由 GameEntry 准备；需要热更的装配调用也可以移入本入口。
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
            // 业务启动写在这里，可拆成 InitializeManagersAsync(token) 和 StartGameAsync(token)。
            // 例如注册本项目使用的 Luban 表、初始化玩法系统、加载首场景或打开登录界面。
            // 需要等待的工作完成后，再调用 ReportReady。
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
            // 在这里释放热更业务持有的资源和订阅。当前公共 Manager 由 GameEntry 释放；
            // 如果把某个 Manager 的初始化迁到这里，也要在此配对释放，并删除 GameEntry 中对应调用。
            host?.Log("HOT_UPDATE_STOPPED");
            host = null;
        }
    }
}
