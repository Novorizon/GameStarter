using UnityEngine;
#if GAMESTARTER_UNITY_IAP
using System.Collections.Generic;
using Game.Framework.Payments;
using Game.Framework.Payments.UnityIap;
#endif
#if NOVORIZON_CSJ || NOVORIZON_MAX
using Game.Framework.Ads;
#endif
#if NOVORIZON_CSJ && !NOVORIZON_MAX
using Game.Framework.Ads.Csj;
#endif
#if NOVORIZON_MAX && !NOVORIZON_CSJ
using Game.Framework.Ads.AppLovin;
#endif
#if NOVORIZON_FIREBASE_APP && NOVORIZON_FIREBASE_CRASHLYTICS
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Firebase;
using Game.Framework.Firebase.Unity;
#endif

namespace Game.PlatformServices
{
    /// <summary>Creates opt-in managers from the selected distribution's configuration. The caller owns initialization and disposal.</summary>
    public static class PlatformServiceManagers
    {
#if GAMESTARTER_UNITY_IAP
        public static IapManager CreateIap(NativeStore store, IReadOnlyCollection<StoreProduct> catalog, IPurchaseFulfillment fulfillment)
        {
            bool supportedStore = (store == NativeStore.Apple && Application.platform == RuntimePlatform.IPhonePlayer)
                || (store == NativeStore.GooglePlay && Application.platform == RuntimePlatform.Android);
            if (!supportedStore || catalog == null || catalog.Count == 0 || fulfillment == null)
            {
                return null;
            }

            return new IapManager(new UnityIapStore(store, catalog), fulfillment, ownsStore: true);
        }
#endif

#if NOVORIZON_CSJ && !NOVORIZON_MAX
        public static AdManager CreateCsjAds(string appId, AdPlacement placement, bool privacyConsent)
        {
            if (!IsMobilePlayer || string.IsNullOrWhiteSpace(appId) || placement == null || !privacyConsent)
            {
                return null;
            }

            return new AdManager(new CsjAdProvider(appId, privacyConsent), ownsProvider: true);
        }
#endif

#if NOVORIZON_MAX && !NOVORIZON_CSJ
        public static AdManager CreateMaxAds(bool sdkKeyConfigured, AdPlacement placement, bool privacyConsent)
        {
            if (!IsMobilePlayer || !sdkKeyConfigured || placement == null || !privacyConsent)
            {
                return null;
            }

            return new AdManager(new MaxAdProvider(privacyConsent), ownsProvider: true);
        }
#endif

#if NOVORIZON_FIREBASE_APP && NOVORIZON_FIREBASE_CRASHLYTICS
        public static async Task<ICrashReporter> CreateCrashReporterAsync(bool projectConfigured, CancellationToken cancellationToken = default)
        {
            if (!IsMobilePlayer || !projectConfigured)
            {
                return null;
            }

            var firebase = new FirebaseManager();
            await firebase.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return firebase.CreateCrashReporter();
        }
#endif

        private static bool IsMobilePlayer => Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
    }
}
