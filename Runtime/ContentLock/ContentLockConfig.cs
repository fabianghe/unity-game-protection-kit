using UnityEngine;

namespace GameProtection.Content
{
    public enum ContentKeyMode
    {
        /// <summary>Keys are embedded in the build (lightly masked). Stops asset rippers; not someone who reads this repo.</summary>
        Local,

        /// <summary>Keys come from your key server after the copy proves entitlement (Steam ticket, Play Integrity). The build has no key.</summary>
        Remote,
    }

    /// <summary>
    /// Content Lock settings. Must live in a Resources folder and be named "ContentLockConfig" (setup creates it).
    /// </summary>
    [CreateAssetMenu(menuName = "Game Protection/Content Lock Config", fileName = ResourceName)]
    public sealed class ContentLockConfig : ScriptableObject
    {
        public const string ResourceName = "ContentLockConfig";

        [Tooltip("Master switch. When off, nothing is encrypted at build time and the loaders pass plain files through.")]
        public bool enabled;

        public ContentKeyMode mode = ContentKeyMode.Local;

        [Header("Remote mode")]
        [Tooltip("Base URL of your key server, e.g. https://keys.yourgame.workers.dev")]
        public string keyServerUrl = "";

        [Tooltip("Fetch keys right after the first scene loads, so they're ready before content is needed.")]
        public bool prefetchOnStart = true;

        [Tooltip("Days a fetched key ring stays valid offline before the game tries to refresh it.")]
        [Min(1)] public int cacheDays = 30;

        [Tooltip("If a refresh fails (offline, server down), keep using the expired cache. Strongly recommended: never lock out paying players over a hiccup.")]
        public bool allowStaleCache = true;

        [Tooltip("Seconds to wait for the key server.")]
        [Min(1)] public int timeoutSeconds = 15;

        [Tooltip("TEST ONLY. Shared secret sent as proof when no platform proof provider is registered (editor, dev builds). " +
                 "Must match DEV_SHARED_SECRET on the server. Leave empty in release builds; the validator checks.")]
        public string devSharedSecret = "";
    }
}
