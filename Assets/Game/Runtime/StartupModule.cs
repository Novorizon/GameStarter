using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 可挂载到场景 GameObject 上的启动模块基类，默认不执行任何工作。
    /// 使用方式：继承此类，重写需要的方法，将组件拖入 GameEntry 的 startupModules 列表。
    /// 生命周期调用来自 GameSession，列表顺序就是初始化顺序，清理时顺序相反。
    /// </summary>
    public abstract class StartupModule : MonoBehaviour, IStartupModule
    {
        /// <summary>
        /// 编写本模块的初始化逻辑；同步初始化直接返回 Task.CompletedTask，异步初始化等待实际完成。
        /// 异步工作应响应 cancellationToken，避免入口退出后继续创建对象或修改状态。
        /// </summary>
        public virtual Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// 编写每帧更新逻辑。只有全部模块就绪后才会调用；使用这个方法时无需另写 Unity Update。
        /// deltaTime 受时间缩放影响，unscaledDeltaTime 不受时间缩放影响。
        /// </summary>
        public virtual void Tick(float deltaTime, float unscaledDeltaTime) { }

        /// <summary>
        /// 编写退出或启动失败时的同步清理逻辑，也要能清理初始化尚未完成的资源。
        /// 入口关闭后即使组件仍在场景中，也应释放它为本次运行持有的资源和订阅。
        /// </summary>
        public virtual void Shutdown() { }
    }
}
