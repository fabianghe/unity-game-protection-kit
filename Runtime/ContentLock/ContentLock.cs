using System;
using System.Collections;
using System.Collections.Generic;
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
    ///   ContentLock.UnloadBundle(bundle, true);             // unload and close the encrypted file behind it
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

        /// <summary>
        /// Makes sure the key for one container is available, fetching or refreshing keys when needed. This is what
        /// makes key rotation work in Remote mode: a player whose cached ring is still fresh doesn't have the new
        /// key yet, so content encrypted with it triggers one extra request to the server.
        /// The callback gets null on success, or an error code. It may run synchronously.
        /// </summary>
        public static void RequestKey(ulong keyId, Action<string> onDone)
        {
            if (TryGetKey(keyId, out _))
            {
                onDone?.Invoke(null);
                return;
            }

            Action<KeyResult> recheck = result =>
            {
                if (TryGetKey(keyId, out _)) onDone?.Invoke(null);
                else onDone?.Invoke(result.Ok ? "key_missing:" + ContentKey.FormatId(keyId) : "keys_unavailable:" + result.Error);
            };

            if (HasKeys && KeyProvider is RemoteKeyProvider remote) remote.RefreshForMissingKey(recheck);
            else RequestKeys(recheck);
        }

        // ---------- loading ----------

        /// <summary>
        /// Plain or encrypted bundle from a local file. Synchronous; keys must already be available.
        /// Unload it with <see cref="UnloadBundle"/> (or bundle.Unload) as usual.
        /// </summary>
        public static AssetBundle LoadBundleFromFile(string path)
        {
            if (!ContentFormat.IsEncryptedFile(path)) return AssetBundle.LoadFromFile(path);

            ReleaseUnloadedStreams();
            var stream = OpenDecrypted(path);
            AssetBundle bundle;
            try { bundle = AssetBundle.LoadFromStream(stream); }
            catch
            {
                stream.Dispose();
                throw;
            }
            Track(bundle, stream);
            return bundle;
        }

        public static AssetBundleCreateRequest LoadBundleFromFileAsync(string path)
        {
            if (!ContentFormat.IsEncryptedFile(path)) return AssetBundle.LoadFromFileAsync(path);

            ReleaseUnloadedStreams();
            var stream = OpenDecrypted(path);
            AssetBundleCreateRequest request;
            try { request = AssetBundle.LoadFromStreamAsync(stream); }
            catch
            {
                stream.Dispose();
                throw;
            }
            request.completed += _ => Track(request.assetBundle, stream);
            return request;
        }

        /// <summary>Unloads a bundle and closes the file behind it right away (otherwise it is closed at the next load).</summary>
        public static void UnloadBundle(AssetBundle bundle, bool unloadAllLoadedObjects)
        {
            if (bundle != null) bundle.Unload(unloadAllLoadedObjects);
            ReleaseUnloadedStreams();
        }

        // Unity reads from the stream for as long as the bundle is loaded, so the stream can't be closed when loading
        // finishes: it has to stay open until the bundle is unloaded. Unity has no "bundle unloaded" event, so the
        // pairs are remembered here and checked on every load and unload.
        static readonly List<KeyValuePair<AssetBundle, Stream>> OpenStreams = new List<KeyValuePair<AssetBundle, Stream>>();

        static void Track(AssetBundle bundle, Stream stream)
        {
            if (bundle == null) stream.Dispose(); // the load failed
            else OpenStreams.Add(new KeyValuePair<AssetBundle, Stream>(bundle, stream));
        }

        static void ReleaseUnloadedStreams()
        {
            for (var i = OpenStreams.Count - 1; i >= 0; i--)
            {
                if (OpenStreams[i].Key != null) continue; // Unity's == null: true once the bundle has been unloaded
                OpenStreams[i].Value.Dispose();
                OpenStreams.RemoveAt(i);
            }
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

            ContentFormat.Header header;
            var encrypted = isUrl ? ContentFormat.TryReadHeader(bytes, out header) : ContentFormat.TryReadFileHeader(pathOrUrl, out header);
            if (encrypted)
            {
                string keyError = null;
                var done = false;
                RequestKey(header.KeyId, e => { keyError = e; done = true; });
                while (!done) yield return null;
                if (keyError != null)
                {
                    onDone?.Invoke(null, keyError);
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
            ReleaseUnloadedStreams();
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
