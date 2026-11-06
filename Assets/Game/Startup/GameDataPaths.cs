namespace Game
{
    /// <summary>本工程的数据表路径；生成、YooAsset 采集和运行时加载共用这些地址。</summary>
    public static class GameDataPaths
    {
        public const string SourceDirectory = "Config/Luban";
        public const string ConfigFile = SourceDirectory + "/luban.conf";
        public const string CodeOutput = "Assets/Generated/Data";
        public const string AssetRoot = "Assets/GameAssets";
        public const string DataOutput = AssetRoot + "/Data/Luban";
        public const string LocationPrefix = "Data/Luban";
        public const string ManifestLocation = LocationPrefix + "/manifest";
        public const string GeneratedAssembly = "Game.Data.Generated";
        public const string SourceStamp = "ProjectSettings/LubanSource.sha256";
        public const string PackageName = "DefaultPackage";
    }
}
