using System;

namespace GameProtection.Content
{
    /// <summary>
    /// TEST ONLY. Proves nothing: sends a shared secret so you can exercise the remote flow in the editor or on a
    /// test device before Steam/Play are wired up. The server only accepts it when DEV_SHARED_SECRET is configured.
    /// The validator flags a non-empty dev secret in release builds.
    /// </summary>
    public sealed class DevSecretProofProvider : IEntitlementProofProvider
    {
        readonly string _secret;

        public string ProofType => "dev";

        public DevSecretProofProvider(string secret) => _secret = secret;

        public void GetProof(string nonce, Action<ProofResult> onComplete) =>
            onComplete?.Invoke(string.IsNullOrEmpty(_secret)
                ? ProofResult.Failure("no_dev_secret")
                : ProofResult.Success("{\"type\":\"dev\",\"secret\":\"" + Json.Escape(_secret) + "\"}"));
    }

    /// <summary>Minimal JSON helpers so the runtime has no dependency on a JSON library.</summary>
    public static class Json
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new System.Text.StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
