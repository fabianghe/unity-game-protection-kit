#if !GAMEPROTECTION_STEAM_LEGACY
using System;
using GameProtection.Content;
using Steamworks;
using UnityEngine;

namespace GameProtection.Steam
{
    /// <summary>
    /// Entitlement proof for Content Lock: a Steam Web API auth ticket. The key server verifies it with
    /// ISteamUserAuth/AuthenticateUserTicket (identity "gpk") and optionally checks ownership of your App ID.
    /// Registers itself automatically. Needs Steamworks.NET 20+ (Steamworks SDK 1.57+); on older versions define
    /// GAMEPROTECTION_STEAM_LEGACY to exclude this file and register your own provider.
    /// Your game must call SteamAPI.RunCallbacks() (the standard SteamManager does), or the ticket never arrives.
    /// </summary>
    public sealed class SteamTicketProofProvider : IEntitlementProofProvider
    {
        public const string Identity = "gpk";

        Callback<GetTicketForWebApiResponse_t> _callback;
        HAuthTicket _pending;
        Action<ProofResult> _onComplete;

        public string ProofType => "steam";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (ContentLock.ProofProvider == null) ContentLock.ProofProvider = new SteamTicketProofProvider();
        }

        public void GetProof(string nonce, Action<ProofResult> onComplete)
        {
            try
            {
                if (!SteamAPI.IsSteamRunning())
                {
                    onComplete?.Invoke(ProofResult.Failure("steam_not_running"));
                    return;
                }

                if (_callback == null) _callback = Callback<GetTicketForWebApiResponse_t>.Create(OnTicket);
                _onComplete = onComplete;
                _pending = SteamUser.GetAuthTicketForWebApi(Identity);
                if (_pending == HAuthTicket.Invalid)
                {
                    _onComplete = null;
                    onComplete?.Invoke(ProofResult.Failure("ticket_request_failed"));
                }
            }
            catch (InvalidOperationException)
            {
                onComplete?.Invoke(ProofResult.Failure("steam_not_initialized"));
            }
        }

        void OnTicket(GetTicketForWebApiResponse_t response)
        {
            if (response.m_hAuthTicket != _pending) return;
            var callback = _onComplete;
            _onComplete = null;

            if (response.m_eResult != EResult.k_EResultOK || response.m_cubTicket <= 0)
            {
                callback?.Invoke(ProofResult.Failure("ticket_result:" + response.m_eResult));
                return;
            }

            var hex = BitConverter.ToString(response.m_rgubTicket, 0, response.m_cubTicket).Replace("-", string.Empty);
            var appId = SteamUtils.GetAppID().m_AppId;
            callback?.Invoke(ProofResult.Success("{\"type\":\"steam\",\"appId\":" + appId + ",\"ticket\":\"" + hex + "\"}"));
            // The ticket stays valid for the server-side check; cancel it once the key has been fetched.
            ContentLockRunner.Instance.StartCoroutine(CancelLater(response.m_hAuthTicket));
        }

        static System.Collections.IEnumerator CancelLater(HAuthTicket ticket)
        {
            yield return new WaitForSecondsRealtime(60f);
            try { SteamUser.CancelAuthTicket(ticket); }
            catch (InvalidOperationException) { }
        }
    }
}
#endif
