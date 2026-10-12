using UnityEngine;

namespace GameProtection
{
    /// <summary>
    /// Runtime settings. Must live in a Resources folder and be named "IntegrityConfig".
    /// Create it with Tools > Game Protection > Setup (pre-filled from Player Settings).
    /// No asset = runtime detection is off.
    ///
    /// These values are plain serialized data. A determined attacker can edit them; re-uploaders rarely bother.
    /// Pair with an obfuscator that encrypts strings if that matters to you.
    /// </summary>
    [CreateAssetMenu(menuName = "Game Protection/Integrity Config", fileName = ResourceName)]
    public sealed class IntegrityConfig : ScriptableObject
    {
        public const string ResourceName = "IntegrityConfig";

        [Tooltip("Master switch for runtime checks.")]
        public bool checksEnabled = true;

        [Header("Identity")]
        [Tooltip("Application identifiers you publish under (Android/iOS). Enforced on mobile only.")]
        public string[] applicationIdentifiers = new string[0];
        [Tooltip("Must match Player Settings > Product Name. The build validator keeps you honest.")]
        public string productName = "";
        [Tooltip("Must match Player Settings > Company Name.")]
        public string companyName = "";

        [Header("Android")]
        [Tooltip("SHA-256 of the Play App Signing certificate AND the upload certificate. Colons are fine.")]
        public string[] androidCertSha256 = new string[0];
        [Tooltip("Installers you consider official. Anything else is reported as Inconclusive (never fails).")]
        public string[] allowedInstallers = { "com.android.vending" };

        [Header("Steam")]
        [Tooltip("Full game, demo and playtest App IDs.")]
        public uint[] steamAppIds = new uint[0];

        [Header("WebGL")]
        [Tooltip("Hosts allowed to serve your WebGL build. Subdomains are included (example.com also allows play.example.com).")]
        public string[] allowedWebHosts = new string[0];

        [Header("Response")]
        [Tooltip("Seconds after the first scene loads before checks run. Lets Steamworks initialize and keeps checks off startup.")]
        [Min(0f)] public float delaySeconds = 20f;
        [Tooltip("Show players of a stolen copy where to get the official game.")]
        public bool showNoticeOnTamper = true;
        [Tooltip("Where the official game lives. Shown in the notice.")]
        public string officialStoreUrl = "";
        [Tooltip("Run checks in Play Mode. Off by default: the editor isn't a shipped build.")]
        public bool runInEditor;
    }
}
