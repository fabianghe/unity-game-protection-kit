using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GameProtection.Content
{
    /// <summary>
    /// Facade for encrypted content. With Addressables, nothing here is needed: the encrypted provider handles it.
    /// With plain AssetBundles, replace AssetBundle.LoadFromFile(path) with ContentLock.LoadBundle(path, callback).
    ///
    ///   ContentLock.ProofProvider = new MyProof();          // modules (Steam) register theirs automatically
    ///   ContentLock.KeyProvider   = new MyKeyProvider();    // replace the whole key strategy
    ///   ContentLock.RequestKeys(result => ...);             // make sure keys are present (remote mode)
    /// </summary>
    public static class ContentLock
    {
        static ContentLockConfig _config;
        static bool _configLoaded;
        static IContentKeyProvider _keyProvider;

        public static IEntitlementProofProvider ProofProvider { get; set; }

        public static ContentLockConfig Config
        {
            get
            {
                if (!_configLoaded)
                {
                    _config = Resources.Load<ContentLockConfig>(ContentLockConfig.ResourceName);
                    _configLoaded = true;
                }
                return _config;
            }
        }

        public static bool IsEnabled => Config != null && Config.enabled;

        public static IContentKeyProvider KeyProvider
        {
            get => _keyProvider ?? (_keyProvider = CreateDefaultProvider());
            set => _keyProvider = value;
        }

        static IContentKeyProvider CreateDefaultProvider()
        {
            var config = Config;
            if (config == null || config.mode == ContentKeyMode.Local) return new LocalKeyProvider();
            return new RemoteKeyProvider(config, ResolveProofProvider);
        }

        static IEntitlementProofProvider ResolveProofProvider()
        {
            if (ProofProvider != null) return ProofProvider;
            var secret = Config != null ? Config.devSharedSecret : null;
            return string.IsNullOrEmpty(secret) ? null : new DevSecretProofProvider(secret);
        }

        // ---------- keys ----------

        public static void RequestKeys(Action<KeyResult> onComplete) => KeyProvider.RequestKeys(onComplete);

        public static bool TryGetKey(ulong keyId, out ContentKey key)
        {
            key = null;
            return KeyProvider.TryGetKeysNow(out var ring) && ring.TryGet(keyId, out key);
        }

        public static bool HasKeys => KeyProvider.TryGetKeysNow(out var ring) && ring.Count > 0;

        /// <summary>Remote mode: ask the server again regardless of the cache (e.g. after a purchase).</summary>
        public static void RefreshKeys(Action<KeyResult> onComplete)
        {
            if (KeyProvider is RemoteKeyProvider remote) remote.Refresh(onComplete);
            else RequestKeys(onComplete);
        }

        // ---------- loading ----------

        /// <summary>Plain or encrypted bundle from a local file. Synchronous; keys must already be available.</summary>
        public static AssetBundle LoadBundleFromFile(string path)
        {
            if (!ContentFormat.IsEncryptedFile(path)) return AssetBundle.LoadFromFile(path);
            return AssetBundle.LoadFromStream(OpenDecrypted(path));
        }

        public static AssetBundleCreateRequest LoadBundleFromFileAsync(string path)
        {
            if (!ContentFormat.IsEncryptedFile(path)) return AssetBundle.LoadFromFileAsync(path);
            return AssetBundle.LoadFromStreamAsync(OpenDecrypted(path));
        }

        /// <summary>Plain or encrypted bundle bytes (e.g. downloaded). Keys must already be available.</summary>
        public static AssetBundle LoadBundleFromMemory(byte[] bytes)
        {
            if (!ContentFormat.TryReadHeader(bytes, out var header)) return AssetBundle.LoadFromMemory(bytes);
            return AssetBundle.LoadFromMemory(ContentEncryptor.Decrypt(bytes, RequireKey(header.KeyId)));
        }

        /// <summary>
        /// One call that does everything: fetches keys if needed, reads a file path or URL (http, jar: on Android),
        /// decrypts and loads. Callback gets the bundle, or null plus an error code.
        /// </summary>
        public static void LoadBundle(string pathOrUrl, Action<AssetBundle, string> onDone) =>
            ContentLockRunner.Instance.StartCoroutine(LoadBundleRoutine(pathOrUrl, onDone));

        /// <summary>Opens a decrypting stream over an encrypted file. Keys must already be available.</summary>
        public static Stream OpenDecrypted(string path)
        {
            var source = File.OpenRead(path);
            try
            {
                if (!ContentFormat.TryReadHeader(source, out var header))
                    throw new InvalidDataException("Not an encrypted container: " + path);
                source.Position = 0;
                return DecryptingStream.Open(source, RequireKey(header.KeyId));
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        static ContentKey RequireKey(ulong keyId)
        {
            if (TryGetKey(keyId, out var key)) return key;
            throw new InvalidOperationException(HasKeys
                ? "No content key with id " + ContentKey.FormatId(keyId) + " (content encrypted with a key this build doesn't have)."
                : "Content keys are not available yet. Call ContentLock.RequestKeys first (remote mode) or check the Content Lock setup.");
        }

        static IEnumerator LoadBundleRoutine(string pathOrUrl, Action<AssetBundle, string> onDone)
        {
            var isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || pathOrUrl.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)
                        || pathOrUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

            byte[] bytes = null;
            if (isUrl)
            {
                using (var request = UnityWebRequest.Get(pathOrUrl))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        onDone?.Invoke(null, "download_failed:" + request.error);
                        yield break;
                    }
                    bytes = request.downloadHandler.data;
                }
            }
            else if (!File.Exists(pathOrUrl))
            {
                onDone?.Invoke(null, "file_not_found");
                yield break;
            }

            var encrypted = isUrl ? ContentFormat.HasMagic(bytes) : ContentFormat.IsEncryptedFile(pathOrUrl);
            if (encrypted && !HasKeys)
            {
                KeyResult result = default;
                var done = false;
                RequestKeys(r => { result = r; done = true; });
                while (!done) yield return null;
                if (!result.Ok)
                {
                    onDone?.Invoke(null, "keys_unavailable:" + result.Error);
                    yield break;
                }
            }

            AssetBundleCreateRequest load;
            try
            {
                load = isUrl ? AssetBundle.LoadFromMemoryAsync(encrypted ? DecryptBytes(bytes) : bytes) : LoadBundleFromFileAsync(pathOrUrl);
            }
            catch (Exception e)
            {
                onDone?.Invoke(null, "load_failed:" + e.GetType().Name + ":" + e.Message);
                yield break;
            }

            yield return load;
            onDone?.Invoke(load.assetBundle, load.assetBundle != null ? null : "bundle_invalid");
        }

        static byte[] DecryptBytes(byte[] bytes)
        {
            ContentFormat.TryReadHeader(bytes, out var header);
            return ContentEncryptor.Decrypt(bytes, RequireKey(header.KeyId));
        }

        // ---------- lifecycle ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _config = null;
            _configLoaded = false;
            _keyProvider = null;
            ProofProvider = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Prefetch()
        {
            var config = Config;
            if (config == null || !config.enabled || config.mode != ContentKeyMode.Remote || !config.prefetchOnStart) return;
            RequestKeys(result =>
            {
                if (!result.Ok) Debug.LogWarning("[GameProtection] Content keys not available: " + result.Error);
            });
        }
    }
}
