using System;

namespace Game.HotUpdate.Client
{
    public enum CampaignStep
    {
        OutpostSupplies,
        Outpost,
        OutpostReturn,
        WaterPoint,
        Sluice,
        SluiceReturn,
        Ridge,
        RidgeReturn,
        Lighthouse,
        ReturnBell,
        SecondBell,
        TidalGate,
        EndingChoice,
        Epilogue,
        Complete
    }

    public enum CampaignEnding
    {
        None,
        Keeper,
        Chart
    }

    [Serializable]
    public sealed class LocalCampaignData
    {
        public int format;
        public CampaignStep step;
        public bool repairKit;
        public bool bringWater;
        public bool guardRelay;
        public bool relayHeld;
        public CampaignEnding ending;
        public string keeperName;

        internal LocalCampaignData Copy() => (LocalCampaignData)MemberwiseClone();
    }
}
