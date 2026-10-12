// Sample: Content Lock entitlement proof for Android using Google Play Integrity.
// Requires the Google Play Integrity plugin for Unity (com.google.play.integrity, via Google's package registry
// or the Play plugins .unitypackage). Import this sample and it registers itself.
// Server side: set GOOGLE_SERVICE_ACCOUNT_JSON and ANDROID_PACKAGE_NAMES on the key server, and enable the
// Play Integrity API for your app in Google Play Console.

using System;
using System.Collections;
using GameProtection.Content;
using Google.Play.Integrity;
using UnityEngine;

public sealed class PlayIntegrityProofProvider : MonoBehaviour, IEntitlementProofProvider
{
    public string ProofType => "play";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        if (Application.platform != RuntimePlatform.Android || ContentLock.ProofProvider != null) return;
        var host = new GameObject("[GameProtection PlayIntegrity]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(host);
        ContentLock.ProofProvider = host.AddComponent<PlayIntegrityProofProvider>();
    }

    public void GetProof(string nonce, Action<ProofResult> onComplete) => StartCoroutine(Request(nonce, onComplete));

    IEnumerator Request(string nonce, Action<ProofResult> onComplete)
    {
        var manager = new IntegrityManager();
        var operation = manager.RequestIntegrityToken(new IntegrityTokenRequest(nonce));
        yield return operation;

        if (operation.Error != IntegrityErrorCode.NoError)
        {
            onComplete?.Invoke(ProofResult.Failure("play_integrity:" + operation.Error));
            yield break;
        }

        var token = operation.GetResult().Token;
        onComplete?.Invoke(ProofResult.Success(
            "{\"type\":\"play\",\"packageName\":\"" + Json.Escape(Application.identifier) + "\",\"token\":\"" + Json.Escape(token) + "\"}"));
    }
}
