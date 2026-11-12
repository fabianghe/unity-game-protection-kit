using System;
using System.ComponentModel;
using System.IO;
using GameProtection.Content;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace GameProtection.Addressables
{
    /// <summary>
    /// Drop-in replacement for the stock AssetBundle provider that understands Content Lock containers.
    /// Select it as "Asset Bundle Provider" on a group (or use Tools > Game Protection > Content Lock > Use Encrypted
    /// Provider For All Groups). Plain bundles still load, so you can switch groups over gradually.
    ///
    /// Local files are decrypted on the fly through a stream (no full copy in memory). Bundles that must be read
    /// through a web request (http, and StreamingAssets on Android) are downloaded, decrypted and loaded from memory.
    /// </summary>
    [DisplayName("Encrypted AssetBundle Provider")]
    public class EncryptedAssetBundleProvider : AssetBundleProvider
    {
        public override void Provide(ProvideHandle providerInterface) => new EncryptedAssetBundleResource(providerInterface).Fetch();

        public override void Release(IResourceLocation location, object asset)
        {
            if (asset is EncryptedAssetBundleResource resource) resource.Unload();
        }
    }

    sealed class EncryptedAssetBundleResource : IAssetBundleResource
    {
        ProvideHandle _handle;
        AssetBundleRequestOptions _options;
        string _path;
        bool _useWebRequest;

        UnityWebRequestAsyncOperation _download;
        AssetBundleCreateRequest _request;
        Stream _stream;
        AssetBundle _bundle;
        bool _waitingForKeys;
        bool _completed;

        public EncryptedAssetBundleResource(ProvideHandle handle)
        {
            _handle = handle;
            _options = handle.Location.Data as AssetBundleRequestOptions;
        }

        public AssetBundle GetAssetBundle() => _bundle;

        public void Fetch()
        {
            _path = _handle.ResourceManager.TransformInternalId(_handle.Location);
            _useWebRequest = ResourceManagerConfig.ShouldPathUseWebRequest(_path);

            _handle.SetProgressCallback(Progress);
            _handle.SetWaitForCompletionCallback(WaitForCompletion);

            try
            {
                if (_useWebRequest) BeginDownload();
                else BeginFileLoad();
            }
            catch (Exception e)
            {
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        // Plain bundles never wait for keys: only an encrypted container triggers a key request, and it asks for the
        // exact key the container names, so content built after a key rotation refreshes a cached ring by itself.
        void WithKey(ulong keyId, Action then)
        {
            _waitingForKeys = true;
            ContentLock.RequestKey(keyId, error =>
            {
                _waitingForKeys = false;
                if (_completed) return;
                if (error != null)
                {
                    Fail("Content key unavailable (" + error + ") for " + _path);
                    return;
                }
                try { then(); }
                catch (Exception e) { Fail(e.GetType().Name + ": " + e.Message); }
            });
        }

        void BeginFileLoad()
        {
            if (!File.Exists(_path))
            {
                Fail("Bundle file not found: " + _path);
                return;
            }

            var crc = _options != null ? _options.Crc : 0u;
            if (!ContentFormat.TryReadFileHeader(_path, out var header))
            {
                _request = AssetBundle.LoadFromFileAsync(_path, crc);
                _request.completed += _ => OnLoaded();
                return;
            }

            WithKey(header.KeyId, () =>
            {
                _stream = ContentLock.OpenDecrypted(_path);
                _request = AssetBundle.LoadFromStreamAsync(_stream, crc);
                _request.completed += _ => OnLoaded();
            });
        }

        void BeginDownload()
        {
            var request = UnityWebRequest.Get(_path);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (_options != null && _options.Timeout > 0) request.timeout = _options.Timeout;
            _download = request.SendWebRequest();
            _download.completed += _ => OnDownloaded();
        }

        void OnDownloaded()
        {
            if (_completed || _download == null) return;
            var request = _download.webRequest;
            _download = null;
            byte[] bytes;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail("Download failed for " + _path + ": " + request.error);
                    return;
                }
                bytes = request.downloadHandler.data;
            }
            finally
            {
                request.Dispose();
            }

            var crc = _options != null ? _options.Crc : 0u;
            if (!ContentFormat.TryReadHeader(bytes, out var header))
            {
                _request = AssetBundle.LoadFromMemoryAsync(bytes, crc);
                _request.completed += _ => OnLoaded();
                return;
            }

            WithKey(header.KeyId, () =>
            {
                _request = AssetBundle.LoadFromMemoryAsync(DecryptBytes(bytes), crc);
                _request.completed += _ => OnLoaded();
            });
        }

        void OnLoaded()
        {
            if (_completed || _request == null) return;
            _bundle = _request.assetBundle;
            _request = null;
            if (_bundle != null) Succeed();
            else Fail("AssetBundle failed to load (wrong key, corrupt file or CRC mismatch): " + _path);
        }

        static byte[] DecryptBytes(byte[] bytes)
        {
            ContentFormat.TryReadHeader(bytes, out var header);
            if (!ContentLock.TryGetKey(header.KeyId, out var key))
                throw new InvalidOperationException("No content key " + ContentKey.FormatId(header.KeyId));
            return ContentEncryptor.Decrypt(bytes, key);
        }

        float Progress()
        {
            if (_completed) return 1f;
            if (_request != null) return 0.5f + _request.progress * 0.5f;
            if (_download != null) return _download.webRequest.downloadProgress * 0.5f;
            return 0f;
        }

        /// <summary>Synchronous completion (Addressables WaitForCompletion). Not possible while keys are being fetched.</summary>
        bool WaitForCompletion()
        {
            if (_completed) return true;
            if (_waitingForKeys) return false;

            if (_download != null)
            {
                var request = _download.webRequest;
                var deadline = DateTime.UtcNow.AddSeconds(_options != null && _options.Timeout > 0 ? _options.Timeout : 30);
                while (!request.isDone && DateTime.UtcNow < deadline) { }
                if (!request.isDone) return false;
                OnDownloaded();
                if (_waitingForKeys) return false;
            }

            if (_request != null)
            {
                // Reading assetBundle blocks until the load has finished, and Unity raises the request's completed
                // event from inside that call, so OnLoaded has usually finished the handle by the time it returns.
                // Completing a handle twice throws.
                var bundle = _request.assetBundle;
                if (!_completed)
                {
                    _bundle = bundle;
                    _request = null;
                    if (_bundle != null) Succeed();
                    else Fail("AssetBundle failed to load: " + _path);
                }
            }
            return _completed;
        }

        void Succeed()
        {
            _completed = true;
            _handle.Complete(this, true, null);
        }

        void Fail(string message)
        {
            _completed = true;
            DisposeStream();
            Debug.LogError("[GameProtection] " + message);
            _handle.Complete<EncryptedAssetBundleResource>(null, false, new Exception(message));
        }

        public void Unload()
        {
            // Released while still loading: stop what is in flight first, so nothing completes into a released
            // handle and the stream isn't closed under a load that is still reading it.
            _completed = true;
            if (_download != null)
            {
                var request = _download.webRequest;
                _download = null;
                request.Dispose(); // aborts a request that is still running
            }
            if (_request != null)
            {
                _bundle = _request.assetBundle; // blocks until the load has finished
                _request = null;
            }

            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }
            DisposeStream();
        }

        void DisposeStream()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
