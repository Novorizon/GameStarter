using System;

namespace Game.HotUpdate.Camp
{
    [Serializable]
    internal sealed class CampStoryScript
    {
        public string id;
        public string version = "1";
        public string title;
        public string speaker;
        public string[] speakers;
        public string[] lines;
    }
}
