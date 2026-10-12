using System;

namespace GameProtection.Content
{
    public readonly struct KeyResult
    {
        public KeyRing Keys { get; }
        public string Error { get; }
        public bool Ok => Keys != null && Keys.Count > 0;

        KeyResult(KeyRing keys, string error)
        {
            Keys = keys;
            Error = error;
        }

        public static KeyResult Success(KeyRing keys) => new KeyResult(keys, null);
        public static KeyResult Failure(string error) => new KeyResult(null, error ?? "unknown");
    }

    /// <summary>
    /// Where content keys come from (Strategy). Built-in: LocalKeyProvider (keys embedded in the build) and
    /// RemoteKeyProvider (keys fetched from your key server after proving entitlement). The callback may run
    /// synchronously.
    /// </summary>
    public interface IContentKeyProvider
    {
        void RequestKeys(Action<KeyResult> onComplete);

        /// <summary>Keys available without waiting (embedded, or already fetched/cached). Used for synchronous loads.</summary>
        bool TryGetKeysNow(out KeyRing keys);
    }

    public readonly struct ProofResult
    {
        /// <summary>JSON object for the server's "proof" field, e.g. {"type":"steam","appId":123,"ticket":"..."}.</summary>
        public string ProofJson { get; }
        public string Error { get; }
        public bool Ok => !string.IsNullOrEmpty(ProofJson);

        ProofResult(string proofJson, string error)
        {
            ProofJson = proofJson;
            Error = error;
        }

        public static ProofResult Success(string proofJson) => new ProofResult(proofJson, null);
        public static ProofResult Failure(string error) => new ProofResult(null, error ?? "unknown");
    }

    /// <summary>
    /// Proves to the key server that this copy is entitled to the content: a Steam auth ticket, a Play Integrity
    /// token, or (for testing only) a shared dev secret. Modules register their provider on ContentLock.ProofProvider.
    /// </summary>
    public interface IEntitlementProofProvider
    {
        string ProofType { get; }

        /// <summary>The nonce comes from the server and must be bound into the proof where the platform supports it.</summary>
        void GetProof(string nonce, Action<ProofResult> onComplete);
    }
}
