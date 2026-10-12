using System;
using UnityEngine;

namespace GameProtection.Content
{
    /// <summary>
    /// Keys embedded in the build as a Resources blob written at build time (ReleaseBuildGuards), masked with a value
    /// derived from the product and company name. Honest limits: this defeats generic asset rippers, which can't
    /// open the bundles, and a repackaged copy that changes those names gets unusable keys; it does not stop someone
    /// who reads this source and extracts the blob. For that, use Remote mode.
    /// </summary>
    public sealed class LocalKeyProvider : IContentKeyProvider
    {
        public const string ResourceName = "GameProtectionContentKeys";

        KeyRing _ring;

        public LocalKeyProvider() { }

        public LocalKeyProvider(KeyRing ring) => _ring = ring;

        public void RequestKeys(Action<KeyResult> onComplete)
        {
            TryGetKeysNow(out var keys);
            onComplete?.Invoke(keys != null && keys.Count > 0 ? KeyResult.Success(keys) : KeyResult.Failure("no_embedded_keys"));
        }

        public bool TryGetKeysNow(out KeyRing keys)
        {
            if (_ring == null) _ring = LoadEmbedded();
            keys = _ring;
            return keys != null && keys.Count > 0;
        }

#if UNITY_EDITOR
        /// <summary>Set by the editor so Play Mode can decrypt content with the project's key store (no blob exists in the editor).</summary>
        public static Func<KeyRing> EditorKeySource;
#endif

        static KeyRing LoadEmbedded()
        {
#if UNITY_EDITOR
            if (EditorKeySource != null) return EditorKeySource() ?? new KeyRing();
#endif
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return new KeyRing();
            return KeyRing.FromBlob(KeyMask.Apply(asset.bytes, Application.productName, Application.companyName));
        }
    }
}
