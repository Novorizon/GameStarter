using System;

namespace Game
{
    [Serializable]
    public sealed class StartupLayout
    {
        public int version = 1;
        public StartupWidget[] widgets = Array.Empty<StartupWidget>();
    }

    [Serializable]
    public sealed class StartupWidget
    {
        public string name;
        public float left;
        public float bottom;
        public float right;
        public float top;
        public int size;
        public string text;
        public string color;
    }
}
