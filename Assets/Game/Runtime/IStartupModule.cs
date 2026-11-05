using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    /// <summary>
    /// 项目启动模块的生命周期约定，由 GameSession 统一调用。
    /// 场景组件通常继承 StartupModule；普通 C# 对象也可以实现本接口。
    /// GameEntry 的 Inspector 列表接收的是 StartupModule 组件。
    /// </summary>
    public interface IStartupModule
    {
        /// <summary>
        /// 初始化模块；返回的 Task 成功完成后才会启动列表中的下一个模块。
        /// 入口关闭时会取消 cancellationToken，异步操作应响应取消并停止后续工作。
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>全部模块初始化成功后，每帧按启动顺序调用。</summary>
        /// <param name="deltaTime">受 Time.timeScale 影响的帧间隔。</param>
        /// <param name="unscaledDeltaTime">不受 Time.timeScale 影响的帧间隔。</param>
        void Tick(float deltaTime, float unscaledDeltaTime);

        /// <summary>
        /// 逆序清理已开始初始化的模块，例如取消订阅、归还资源。
        /// 初始化中途失败或被取消时也可能调用，应能处理只完成部分初始化的状态。
        /// </summary>
        void Shutdown();
    }

}
