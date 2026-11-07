using System;
namespace Game
{
    // Rendering is verified by real Unity; the lifecycle model only owns/disposes this boundary.
    public sealed class StartupScreen : IDisposable
    {
        public static StartupScreen Open(IStartupProgressSource source) => new StartupScreen();
        public void Dispose() { }
    }
}
