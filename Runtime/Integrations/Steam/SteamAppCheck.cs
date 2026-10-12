using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEngine;

namespace GameProtection.Steam
{
    /// <summary>
    /// A stolen build re-uploaded to Steam under another App ID, or a demo sold as a "full game", reports an App ID you
    /// don't own. This module compiles only when GAMEPROTECTION_STEAMWORKS is defined: automatic for a Package Manager
    /// install of Steamworks.NET, one click in Project Settings > Game Protection otherwise.
    /// Detection, not DRM: it never blocks the game.
    /// </summary>
    public sealed class SteamAppCheck : IIntegrityCheck
    {
        public const string CheckId = "steam_app";

        readonly HashSet<uint> _allowedAppIds;

        public string Id => CheckId;

        public SteamAppCheck(IEnumerable<uint> allowedAppIds) =>
            _allowedAppIds = new HashSet<uint>((allowedAppIds ?? Enumerable.Empty<uint>()).Where(id => id != 0));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => GameProtectionKit.RegisterCheckFactory(config => new SteamAppCheck(config.steamAppIds));

        public IntegrityResult Run()
        {
#if DISABLESTEAMWORKS
            return IntegrityResult.Inconclusive(Id, "steam_disabled");
#else
            if (_allowedAppIds.Count == 0) return IntegrityResult.Inconclusive(Id, "no_expected_app_ids");
            try
            {
                var appId = SteamUtils.GetAppID().m_AppId;
                if (!_allowedAppIds.Contains(appId)) return IntegrityResult.Fail(Id, "appid=" + appId);

                return SteamApps.BIsSubscribedApp(new AppId_t(appId))
                    ? IntegrityResult.Pass(Id)
                    : IntegrityResult.Inconclusive(Id, "not_subscribed");
            }
            catch (System.InvalidOperationException)
            {
                // Steam not initialized: DRM-free copy, Steam closed, or stripped. Report, don't punish.
                return IntegrityResult.Inconclusive(Id, "steam_not_initialized");
            }
#endif
        }
    }
}
