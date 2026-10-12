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
    ///   GameProtectionKit.RunNow();                                    // run checks on demand (e.g. after Steam init)
    ///
    /// Set these from your own bootstrap (Awake, or [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]).
    /// They are read when checks run, after IntegrityConfig.delaySeconds.
    /// </summary>
    public static class GameProtectionKit
    {
        static readonly List<IIntegrityCheck> ExtraChecks = new List<IIntegrityCheck>();
        static readonly List<Func<IntegrityConfig, IIntegrityCheck>> CheckFactories = new List<Func<IntegrityConfig, IIntegrityCheck>>();

        public static IIntegrityReporter Reporter { get; set; } = new DebugLogIntegrityReporter();
        public static ITamperNoticePresenter NoticePresenter { get; set; } = new DefaultTamperNoticePresenter();

        /// <summary>The loaded runtime config, or null when the kit isn't set up.</summary>
        public static IntegrityConfig Config { get; private set; }

        /// <summary>True once the automatic run has been scheduled for this session.</summary>
        public static bool IsRunning { get; private set; }

        public static void RegisterCheck(IIntegrityCheck check)
        {
            if (check != null && !ExtraChecks.Contains(check)) ExtraChecks.Add(check);
        }

        public static void UnregisterCheck(IIntegrityCheck check) => ExtraChecks.Remove(check);

        /// <summary>For optional modules (Steam, ...) that need the config to build their check.</summary>
        public static void RegisterCheckFactory(Func<IntegrityConfig, IIntegrityCheck> factory)
        {
            if (factory != null && !CheckFactories.Contains(factory)) CheckFactories.Add(factory);
        }

        /// <summary>The built-in checks for the current platform, plus module and registered ones.</summary>
        public static IEnumerable<IIntegrityCheck> CreateChecks(IntegrityConfig config)
        {
            yield return new AppIdentityCheck(config.applicationIdentifiers, config.productName, config.companyName);
#if UNITY_ANDROID
            yield return new AndroidSignatureCheck(config.androidCertSha256);
            yield return new AndroidInstallerCheck(config.allowedInstallers);
#endif
#if UNITY_WEBGL
            yield return new WebHostCheck(config.allowedWebHosts);
#endif
            foreach (var factory in CheckFactories)
            {
                var check = factory(config);
                if (check != null) yield return check;
            }
            foreach (var check in ExtraChecks) yield return check;
        }

        /// <summary>
        /// Runs all checks immediately and publishes the report. Useful after your own Steam/Play initialization,
        /// or from a debug menu. Safe to call any number of times; does nothing when the kit isn't set up.
        /// </summary>
        public static IntegrityReport RunNow()
        {
            var config = Config != null ? Config : LoadConfig();
            if (config == null) return null;

            var report = new IntegrityService(CreateChecks(config)).Run(BuildStamp.Load());
            Respond(report, config);
            return report;
        }

        static IntegrityConfig LoadConfig() => Config = Resources.Load<IntegrityConfig>(IntegrityConfig.ResourceName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ExtraChecks.Clear();
            CheckFactories.Clear();
            Reporter = new DebugLogIntegrityReporter();
            NoticePresenter = new DefaultTamperNoticePresenter();
            Config = null;
            IsRunning = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var config = LoadConfig();
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
            GameProtectionKit.RunNow();
            Destroy(gameObject);
        }
    }
}
