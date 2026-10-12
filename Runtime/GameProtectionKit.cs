using System;
using System.Collections;
using System.Collections.Generic;
using GameProtection.Checks;
using UnityEngine;

namespace GameProtection
{
    /// <summary>
    /// Public entry point and composition root. Zero code is needed: once an IntegrityConfig exists in Resources,
    /// checks run automatically after the first scene loads. Everything here is optional customization:
    ///
    ///   GameProtectionKit.Reporter = new MyAnalyticsReporter();       // where reports go
    ///   GameProtectionKit.NoticePresenter = new MyNoticeUi();          // what stolen-copy players see
    ///   GameProtectionKit.RegisterCheck(new MyCustomCheck());          // add your own checks
    ///   IntegrityEvents.ReportReady += report => { ... };              // react to results
    ///
    /// Set these from your own bootstrap (Awake, or [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]).
    /// They are read when checks run, after IntegrityConfig.delaySeconds.
    /// </summary>
    public static class GameProtectionKit
    {
        static readonly List<IIntegrityCheck> ExtraChecks = new List<IIntegrityCheck>();

        public static IIntegrityReporter Reporter { get; set; } = new DebugLogIntegrityReporter();
        public static ITamperNoticePresenter NoticePresenter { get; set; } = new DefaultTamperNoticePresenter();

        /// <summary>True once checks have been scheduled for this session.</summary>
        public static bool IsRunning { get; private set; }

        public static void RegisterCheck(IIntegrityCheck check)
        {
            if (check != null && !ExtraChecks.Contains(check)) ExtraChecks.Add(check);
        }

        public static void UnregisterCheck(IIntegrityCheck check) => ExtraChecks.Remove(check);

        /// <summary>The built-in checks for the current platform, plus any registered ones.</summary>
        public static IEnumerable<IIntegrityCheck> CreateChecks(IntegrityConfig config)
        {
            yield return new AppIdentityCheck(config.applicationIdentifiers, config.productName, config.companyName);
#if UNITY_ANDROID
            yield return new AndroidSignatureCheck(config.androidCertSha256);
            yield return new AndroidInstallerCheck(config.allowedInstallers);
#endif
#if UNITY_STANDALONE
            yield return new SteamAppCheck(config.steamAppIds);
#endif
#if UNITY_WEBGL
            yield return new WebHostCheck(config.allowedWebHosts);
#endif
            foreach (var check in ExtraChecks) yield return check;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ExtraChecks.Clear();
            Reporter = new DebugLogIntegrityReporter();
            NoticePresenter = new DefaultTamperNoticePresenter();
            IsRunning = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var config = Resources.Load<IntegrityConfig>(IntegrityConfig.ResourceName);
            if (config == null || !config.checksEnabled || (Application.isEditor && !config.runInEditor)) return;

            var host = new GameObject("[GameProtection]") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<IntegrityRunner>().Configure(config);
            IsRunning = true;
        }

        internal static void Respond(IntegrityReport report, IntegrityConfig config)
        {
            IntegrityEvents.Publish(report);

            try { Reporter?.Report(report); }
            catch (Exception e) { Debug.LogException(e); }

            if (!report.IsTampered || !config.showNoticeOnTamper) return;
            try { NoticePresenter?.Show(report, config.officialStoreUrl); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Thin Unity adapter: owns timing only, delegates the work.</summary>
    [AddComponentMenu("")]
    sealed class IntegrityRunner : MonoBehaviour
    {
        IntegrityConfig _config;

        public void Configure(IntegrityConfig config) => _config = config;

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(_config.delaySeconds);

            var service = new IntegrityService(GameProtectionKit.CreateChecks(_config));
            GameProtectionKit.Respond(service.Run(BuildStamp.Load()), _config);
            Destroy(gameObject);
        }
    }
}
