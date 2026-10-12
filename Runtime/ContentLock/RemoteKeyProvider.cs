using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GameProtection.Content
{
    /// <summary>
    /// Fetches keys from your key server (see Server~/cloudflare-worker) after proving entitlement:
    ///   GET  {server}/v1/nonce            -> { "nonce": "..." }
    ///   POST {server}/v1/key              -> { "keys": [{ "id": "hex16", "key": "base64" }], "ttlSeconds": n }
    ///        { "v":1, "nonce":"...", "proof": {...}, "buildId":"...", "channel":"...", "platform":"..." }
    /// Keys are cached device-bound so the game works offline afterwards. A failed refresh never discards a cache
    /// when allowStaleCache is on: paying players are never locked out by a server hiccup.
    /// </summary>
    public sealed class RemoteKeyProvider : IContentKeyProvider
    {
        readonly string _serverUrl;
        readonly Func<IEntitlementProofProvider> _proofProvider;
        readonly DeviceBoundKeyCache _cache;
        readonly TimeSpan _cacheTtl;
        readonly bool _allowStale;
        readonly int _timeoutSeconds;

        KeyRing _ring;
        DateTime _fetchedAtUtc;
        bool _fetching;
        readonly List<Action<KeyResult>> _waiting = new List<Action<KeyResult>>();

        public RemoteKeyProvider(ContentLockConfig config, Func<IEntitlementProofProvider> proofProvider, DeviceBoundKeyCache cache = null)
        {
            _serverUrl = (config.keyServerUrl ?? string.Empty).TrimEnd('/');
            _proofProvider = proofProvider;
            _cache = cache ?? new DeviceBoundKeyCache();
            _cacheTtl = TimeSpan.FromDays(Math.Max(1, config.cacheDays));
            _allowStale = config.allowStaleCache;
            _timeoutSeconds = Math.Max(1, config.timeoutSeconds);
        }

        public bool TryGetKeysNow(out KeyRing keys)
        {
            if (_ring == null && _cache.TryLoad(out var cached, out var at))
            {
                _ring = cached;
                _fetchedAtUtc = at;
            }
            keys = _ring;
            return keys != null && keys.Count > 0;
        }

        public void RequestKeys(Action<KeyResult> onComplete) => Request(onComplete, false);

        /// <summary>Ignores the cache TTL and asks the server again (e.g. after a purchase or key rotation).</summary>
        public void Refresh(Action<KeyResult> onComplete) => Request(onComplete, true);

        void Request(Action<KeyResult> onComplete, bool force)
        {
            var haveCache = TryGetKeysNow(out _);
            var fresh = haveCache && DateTime.UtcNow - _fetchedAtUtc < _cacheTtl;
            if (fresh && !force)
            {
                onComplete?.Invoke(KeyResult.Success(_ring));
                return;
            }

            if (onComplete != null) _waiting.Add(onComplete);
            if (_fetching) return;
            _fetching = true;
            ContentLockRunner.Instance.StartCoroutine(Fetch());
        }

        IEnumerator Fetch()
        {
            string error = null;
            KeyRing fetched = null;

            if (string.IsNullOrEmpty(_serverUrl)) error = "no_server_url";

            var proofProvider = error == null ? _proofProvider?.Invoke() : null;
            if (error == null && proofProvider == null) error = "no_proof_provider";

            string nonce = null;
            if (error == null)
            {
                using (var request = UnityWebRequest.Get(_serverUrl + "/v1/nonce"))
                {
                    request.timeout = _timeoutSeconds;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success) error = "nonce_request_failed:" + request.error;
                    else
                    {
                        var nonceResponse = JsonUtility.FromJson<NonceResponse>(request.downloadHandler.text);
                        nonce = nonceResponse != null ? nonceResponse.nonce : null;
                        if (string.IsNullOrEmpty(nonce)) error = "nonce_missing";
                    }
                }
            }

            ProofResult proof = default;
            if (error == null)
            {
                var done = false;
                proofProvider.GetProof(nonce, result => { proof = result; done = true; });
                var deadline = Time.realtimeSinceStartup + _timeoutSeconds;
                while (!done && Time.realtimeSinceStartup < deadline) yield return null;
                if (!done) error = "proof_timeout";
                else if (!proof.Ok) error = "proof_failed:" + proof.Error;
            }

            if (error == null)
            {
                var stamp = BuildStamp.Load();
                var body = "{\"v\":1,\"nonce\":\"" + Json.Escape(nonce) + "\",\"proof\":" + proof.ProofJson +
                           ",\"buildId\":\"" + Json.Escape(stamp.buildId) + "\",\"channel\":\"" + Json.Escape(stamp.channel) +
                           "\",\"platform\":\"" + Application.platform + "\",\"version\":\"" + Json.Escape(Application.version) + "\"}";

                using (var request = new UnityWebRequest(_serverUrl + "/v1/key", "POST"))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = _timeoutSeconds;
                    yield return request.SendWebRequest();

                    var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    if (request.result != UnityWebRequest.Result.Success && request.responseCode == 0)
                        error = "key_request_failed:" + request.error;
                    else
                    {
                        var response = SafeParse(text);
                        if (request.responseCode != 200 || response == null || response.keys == null || response.keys.Length == 0)
                            error = "key_denied:" + request.responseCode + ":" + (response != null && !string.IsNullOrEmpty(response.error) ? response.error : "bad_response");
                        else
                        {
                            try
                            {
                                fetched = new KeyRing();
                                foreach (var entry in response.keys) fetched.Add(ContentKey.FromBase64(entry.key));
                                if (fetched.Count == 0) error = "no_keys_in_response";
                            }
                            catch (FormatException) { error = "bad_key_encoding"; }
                            catch (ArgumentException) { error = "bad_key_length"; }
                        }
                    }
                }
            }

            KeyResult outcome;
            if (fetched != null)
            {
                _ring = fetched;
                _fetchedAtUtc = DateTime.UtcNow;
                _cache.Save(_ring, _fetchedAtUtc);
                outcome = KeyResult.Success(_ring);
            }
            else if (_allowStale && _ring != null && _ring.Count > 0)
            {
                Debug.LogWarning("[GameProtection] Key refresh failed (" + error + "); using the cached keys.");
                outcome = KeyResult.Success(_ring);
            }
            else
            {
                outcome = KeyResult.Failure(error);
            }

            _fetching = false;
            var callbacks = _waiting.ToArray();
            _waiting.Clear();
            foreach (var callback in callbacks)
            {
                try { callback(outcome); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static KeyResponse SafeParse(string text)
        {
            try { return string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<KeyResponse>(text); }
            catch (ArgumentException) { return null; }
        }

        [Serializable] class NonceResponse { public string nonce; }
        [Serializable] class KeyEntry { public string id; public string key; }
        [Serializable] class KeyResponse { public KeyEntry[] keys; public long ttlSeconds; public string error; }
    }

    /// <summary>Hidden coroutine host for the content lock.</summary>
    [AddComponentMenu("")]
    sealed class ContentLockRunner : MonoBehaviour
    {
        static ContentLockRunner _instance;

        public static ContentLockRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var host = new GameObject("[GameProtection ContentLock]") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<ContentLockRunner>();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;
    }
}
