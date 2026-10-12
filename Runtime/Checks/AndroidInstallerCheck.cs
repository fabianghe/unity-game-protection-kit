using System.Collections.Generic;
using System.Linq;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace GameProtection.Checks
{
    /// <summary>
    /// Telemetry only: tells you WHERE copies are installed from (third-party APK sites, alternative stores).
    /// Never fails, because sideloading on its own is not proof of theft.
    /// </summary>
    public sealed class AndroidInstallerCheck : IIntegrityCheck
    {
        public const string CheckId = "android_installer";

        readonly HashSet<string> _allowedInstallers;

        public string Id => CheckId;

        public AndroidInstallerCheck(IEnumerable<string> allowedInstallers) =>
            _allowedInstallers = new HashSet<string>((allowedInstallers ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)));

        public IntegrityResult Run()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var installer = ReadInstaller();
            return _allowedInstallers.Contains(installer ?? string.Empty)
                ? IntegrityResult.Pass(Id)
                : IntegrityResult.Inconclusive(Id, "installer=" + (installer ?? "none"));
#else
            return IntegrityResult.Inconclusive(Id, "not_android");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static string ReadInstaller()
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                var packageName = activity.Call<string>("getPackageName");
                if (version.GetStatic<int>("SDK_INT") >= 30)
                {
                    // getInstallerPackageName is deprecated from API 30.
                    using (var info = packageManager.Call<AndroidJavaObject>("getInstallSourceInfo", packageName))
                        return info.Call<string>("getInstallingPackageName");
                }
                return packageManager.Call<string>("getInstallerPackageName", packageName);
            }
        }
#endif
    }
}
