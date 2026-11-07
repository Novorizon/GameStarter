namespace Game
{
    public struct StartupSnapshot
    {
        public float Fraction;
        public string Message;
        public bool Ready;
        public bool Failed;
        public bool Downloading;
        public long CompletedBytes;
        public long TotalBytes;
    }

    public interface IStartupProgressSource
    {
        StartupSnapshot ReadStartup();
    }
}
