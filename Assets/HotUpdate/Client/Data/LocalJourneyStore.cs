using Game.Framework.Storage;

namespace Game.HotUpdate.Client
{
    public sealed class LocalJourneyStore : ILocalJourneyStore
    {
        private const string FileName = "local-journey.json";

        public LocalJourneyData Load()
        {
            return LocalStorageManager.Instance.Exists(FileName) ? LocalStorageManager.Instance.LoadJson<LocalJourneyData>(FileName) : null;
        }

        public void Save(LocalJourneyData value)
        {
            LocalStorageManager.Instance.SaveJson(FileName, value);
        }
    }
}
