using System.Collections.Generic;
using System.Linq;

namespace GameProtection.Checks
{
    /// <summary>
    /// A stolen build re-uploaded to Steam under another App ID, or a demo sold as a "full game", reports an App ID you
    /// don't own. Uses Steamworks.NET. Enabled automatically when Steamworks.NET is installed through the Package Manager;
    /// for a .unitypackage install add GAMEPROTECTION_STEAMWORKS to your scripting defines (the validator offers a fix).
    /// This is detection, not DRM: it never blocks the game.
    /// </summary>
    public sealed class SteamAppCheck : IIntegrityCheck
    {
        public const string CheckId = "steam_app";

        readonly HashSet<uint> _allowedAppIds;

        public string Id => CheckId;

        public SteamAppCheck(IEnumerable<uint> allowedAppIds) =>
            _allowedAppIds = new HashSet<uint>((allowedAppIds ?? Enumerable.Empty<uint>()).Where(id => id != 0));

        public IntegrityResult Run()
        {
#if GAMEPROTECTION_STEAMWORKS && !DISABLESTEAMWORKS && UNITY_STANDALONE
            if (_allowedAppIds.Count == 0) return IntegrityResult.Inconclusive(Id, "no_expected_app_ids");
            try
            {
                var appId = Steamworks.SteamUtils.GetAppID().m_AppId;
                if (!_allowedAppIds.Contains(appId)) return IntegrityResult.Fail(Id, "appid=" + appId);

                return Steamworks.SteamApps.BIsSubscribedApp(new Steamworks.AppId_t(appId))
                    ? IntegrityResult.Pass(Id)
                    : IntegrityResult.Inconclusive(Id, "not_subscribed");
            }
            catch (System.InvalidOperationException)
            {
                // Steam not initialized: DRM-free copy, Steam closed, or stripped. Report, don't punish.
                return IntegrityResult.Inconclusive(Id, "steam_not_initialized");
            }
#else
            return IntegrityResult.Inconclusive(Id, "steam_unavailable");
#endif
        }
    }
}
