// The verifier models initialization parameters and the default manifest naming boundary.
namespace YooAsset
{
    public class InitializeParameters
    {
    }

    public static class YooAssetSettingsData
    {
        public static string GetManifestBinaryFileName(string package, string version) => package + "_" + version + ".bytes";
    }
}
