using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Security.Cryptography;
using UnityEngine;
#endif

namespace GameProtection.Checks
{
    /// <summary>
    /// Repackaged APKs (injected ads, "mod" versions, re-uploads) can't be re-signed with your key.
    /// Configure BOTH the Play App Signing certificate SHA-256 and your upload certificate SHA-256
    /// (Play Console > Test and release > App integrity > App signing).
    /// </summary>
    public sealed class AndroidSignatureCheck : IIntegrityCheck
    {
        public const string CheckId = "android_signature";

        readonly HashSet<string> _allowedSha256;

        public string Id => CheckId;

        public AndroidSignatureCheck(IEnumerable<string> allowedSha256) =>
            _allowedSha256 = new HashSet<string>((allowedSha256 ?? Enumerable.Empty<string>()).Select(NormalizeHash).Where(s => s.Length > 0));

        /// <summary>Accepts "AB:CD:..." (Play Console format), "abcd..." or "AB CD ...".</summary>
        public static string NormalizeHash(string hex) =>
            (hex ?? string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();

        public IntegrityResult Run()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_allowedSha256.Count == 0) return IntegrityResult.Inconclusive(Id, "no_expected_certs");

            var actual = ReadSignerHashes();
            if (actual.Count == 0) return IntegrityResult.Inconclusive(Id, "no_signers_read");

            return actual.Any(_allowedSha256.Contains)
                ? IntegrityResult.Pass(Id)
                : IntegrityResult.Fail(Id, "cert=" + actual[0]);
#else
            return IntegrityResult.Inconclusive(Id, "not_android");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static List<string> ReadSignerHashes()
        {
            const int GET_SIGNATURES = 0x00000040;
            const int GET_SIGNING_CERTIFICATES = 0x08000000;

            var result = new List<string>();
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var packageManager = activity.Call<AndroidJavaObject>("getPackageManager");
            var packageName = activity.Call<string>("getPackageName");
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            var sdk = version.GetStatic<int>("SDK_INT");

            AndroidJavaObject[] signatures;
            if (sdk >= 28)
            {
                using var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, GET_SIGNING_CERTIFICATES);
                using var signingInfo = info.Get<AndroidJavaObject>("signingInfo");
                signatures = signingInfo.Call<AndroidJavaObject[]>("getApkContentsSigners");
            }
            else
            {
                using var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, GET_SIGNATURES);
                signatures = info.Get<AndroidJavaObject[]>("signatures");
            }

            using var sha = SHA256.Create();
            foreach (var signature in signatures ?? Array.Empty<AndroidJavaObject>())
            {
                using (signature)
                {
                    var signed = signature.Call<sbyte[]>("toByteArray");
                    var bytes = new byte[signed.Length];
                    Buffer.BlockCopy(signed, 0, bytes, 0, signed.Length);
                    result.Add(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty));
                }
            }
            return result;
        }
#endif
    }
}
