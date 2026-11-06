// The verifier models initialization parameters and the default manifest naming boundary.
using System.Threading.Tasks;

namespace YooAsset
{
    public class InitializeParameters
    {
    }

    public sealed class OfflinePlayModeParameters : InitializeParameters
    {
    }

    public sealed class HostPlayModeParameters : InitializeParameters
    {
    }

    public enum EOperationStatus
    {
        None,
        Succeed,
        Failed
    }

    public sealed class DestroyOperation
    {
        public Task Task = Task.CompletedTask;
        public EOperationStatus Status = EOperationStatus.Succeed;
        public string Error = "";
    }

    public sealed class ResourcePackage
    {
        public EOperationStatus InitializeStatus;
        public int Destroyed;
        public DestroyOperation DestroyAsync()
        {
            Destroyed++;
            InitializeStatus = EOperationStatus.None;
            return new DestroyOperation();
        }
    }

    public static class YooAssets
    {
        public static ResourcePackage Package;
        public static ResourcePackage TryGetPackage(string name) => Package;
    }

    public static class YooAssetSettingsData
    {
        public static string GetManifestBinaryFileName(string package, string version) => package + "_" + version + ".bytes";
    }
}
