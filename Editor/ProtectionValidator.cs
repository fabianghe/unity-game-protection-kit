using System;
using System.Collections.Generic;
using System.Linq;
using GameProtection.Checks;
using UnityEditor;

namespace GameProtection.Editor
{
    public enum IssueSeverity { Info, Warning, Error }

    public sealed class ValidationIssue
    {
        public IssueSeverity Severity { get; }
        public string Message { get; }
        public string FixLabel { get; }
        public Action Fix { get; }

        public ValidationIssue(IssueSeverity severity, string message, string fixLabel = null, Action fix = null)
        {
            Severity = severity;
            Message = message;
            FixLabel = fixLabel;
            Fix = fix;
        }

        public override string ToString() => $"[{Severity}] {Message}";
    }

    /// <summary>
    /// Preflight checks for a build target. Shown in Project Settings > Game Protection and run before every build.
    /// Each rule is a small method returning issues, so adding one is a one-method change.
    /// </summary>
    public static class ProtectionValidator
    {
        const string SteamworksAssembly = "com.rlabrecque.steamworks.net";
        const string SteamworksDefine = "GAMEPROTECTION_STEAMWORKS";
        const string BitMonoDefine = "GAMEPROTECTION_BITMONO";
        public const string BitMonoReleasesUrl = "https://github.com/bitmono-project/BitMono/releases";

        /// <summary>Optional editor modules (BitMono, ...) add their validation rules here.</summary>
        public static readonly List<Func<BuildTarget, bool, IEnumerable<ValidationIssue>>> ExtraRules =
            new List<Func<BuildTarget, bool, IEnumerable<ValidationIssue>>>();

        public static List<ValidationIssue> Validate(BuildTarget target, bool isDevelopmentBuild = false)
        {
            var config = ProtectionSetup.FindRuntimeConfig();
            var guard = ReleaseGuardSettings.Find();

            return ConfigRules(config, target)
                .Concat(GuardRules(guard, target))
                .Concat(BackendRules(guard, target, isDevelopmentBuild))
                .Concat(SteamworksRules(config, target))
                .Concat(ObfuscationRules(guard, isDevelopmentBuild))
                .Concat(ContentLockEditor.Validate(target, isDevelopmentBuild))
                .Concat(ExtraRules.SelectMany(rule => rule(target, isDevelopmentBuild)))
                .ToList();
        }

        // Names of known obfuscation/metadata-protection tools, matched against loaded editor assemblies.
        static readonly string[] ObfuscatorMarkers = { "obfuscat", "bitmono", "mfuscator", "beebyte", "llobf", "luridlane" };

        static IEnumerable<ValidationIssue> ObfuscationRules(ReleaseGuardSettings guard, bool isDevelopmentBuild)
        {
            if (isDevelopmentBuild || guard == null || guard.obfuscatorConfiguredManually) yield break;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
            var bitMonoInstalled = assemblies.Contains("BitMono.Unity.Editor");
            var bitMonoModuleLoaded = assemblies.Contains("GameProtection.BitMono.Editor");
            if (bitMonoInstalled && !bitMonoModuleLoaded)
            {
                // Installed as a .unitypackage (no versionDefine): one click turns the integration on.
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "BitMono is installed, but the kit's BitMono integration is off (define missing).",
                    "Enable BitMono integration", () => BuildDefines.Add(BuildDefines.Named(EditorUserBuildSettings.activeBuildTarget), BitMonoDefine));
                yield break;
            }

            var detected = assemblies
                .Select(name => name.ToLowerInvariant())
                .Any(name => !name.StartsWith("gameprotection") && ObfuscatorMarkers.Any(name.Contains));
            if (detected) yield break;

            yield return new ValidationIssue(IssueSeverity.Warning,
                "No obfuscator detected. IL2CPP alone keeps class and method names readable, and public tools dump them for every " +
                "Unity version. BitMono is free and the kit integrates it: download the UPM .tgz for your Unity version, then " +
                "Package Manager > + > Add package from tarball. Already using another obfuscator? Click \"I use one\".",
                "Get BitMono (free)", () => Application.OpenURL(BitMonoReleasesUrl));

            yield return new ValidationIssue(IssueSeverity.Info, "Using a different obfuscator? Silence the warning above.",
                "I use one", () =>
                {
                    guard.obfuscatorConfiguredManually = true;
                    Save(guard);
                });
        }

        static IEnumerable<ValidationIssue> ConfigRules(IntegrityConfig config, BuildTarget target)
        {
            if (config == null)
            {
                yield return ProtectionSetup.HasMisplacedConfig()
                    ? new ValidationIssue(IssueSeverity.Error,
                        "An IntegrityConfig exists but the runtime can't find it. It must be inside a Resources folder and named \"IntegrityConfig\".")
                    : new ValidationIssue(IssueSeverity.Warning,
                        "Runtime detection is off: no IntegrityConfig yet.", "Create (pre-filled)", () => Save(ProtectionSetup.EnsureConfig()));
                yield break;
            }

            if (!config.checksEnabled)
                yield return new ValidationIssue(IssueSeverity.Info, "Runtime checks are disabled in IntegrityConfig.");

            // An empty name disables that comparison at runtime; only a wrong name would flag legit players.
            var productMismatch = !string.IsNullOrEmpty(config.productName) && config.productName != PlayerSettings.productName;
            var companyMismatch = !string.IsNullOrEmpty(config.companyName) && config.companyName != PlayerSettings.companyName;
            if (productMismatch || companyMismatch)
                yield return new ValidationIssue(IssueSeverity.Error,
                    $"Config identity (\"{config.productName}\" / \"{config.companyName}\") doesn't match Player Settings " +
                    $"(\"{PlayerSettings.productName}\" / \"{PlayerSettings.companyName}\"). Legit players would see the stolen-copy notice.",
                    "Sync from Player Settings", () => SyncAndSave(config));
            else if (string.IsNullOrEmpty(config.productName) && string.IsNullOrEmpty(config.companyName))
                yield return new ValidationIssue(IssueSeverity.Info,
                    "Product and company name checks are off (both empty in IntegrityConfig).",
                    "Sync from Player Settings", () => SyncAndSave(config));

            if (target == BuildTarget.Android || target == BuildTarget.iOS)
            {
                var id = PlayerSettings.GetApplicationIdentifier(BuildDefines.Named(target));
                if (!(config.applicationIdentifiers ?? new string[0]).Contains(id))
                    yield return new ValidationIssue(IssueSeverity.Error,
                        $"Application identifier \"{id}\" is not listed in IntegrityConfig. Legit players would see the stolen-copy notice.",
                        "Sync from Player Settings", () => SyncAndSave(config));
            }

            if (target == BuildTarget.Android && !(config.androidCertSha256 ?? new string[0]).Any(h => AndroidSignatureCheck.NormalizeHash(h).Length == 64))
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "No valid Android certificate SHA-256 set, so re-signed APKs won't be detected. Add the App signing key and upload key " +
                    "certificate SHA-256 from Play Console > Test and release > App integrity.");

            if (target == BuildTarget.WebGL && (config.allowedWebHosts ?? new string[0]).Length == 0)
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "No allowed web hosts set, so rehosted WebGL copies won't be detected.");

            if (config.showNoticeOnTamper && !IsHttpUrl(config.officialStoreUrl))
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "Official store URL is empty or invalid. The stolen-copy notice won't be able to send players to you.");
        }

        static IEnumerable<ValidationIssue> GuardRules(ReleaseGuardSettings guard, BuildTarget target)
        {
            if (guard == null)
            {
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "No ReleaseGuardSettings: demo leak protection and build stamping use defaults.",
                    "Create", () => Save(ProtectionSetup.EnsureGuardSettings()));
                yield break;
            }

            if (BuildDefines.IsDefined(target, guard.demoDefine) && guard.DemoScenePaths().Length == 0)
                yield return new ValidationIssue(IssueSeverity.Error,
                    $"\"{guard.demoDefine}\" is defined but no demo scenes are listed in ReleaseGuardSettings.");
        }

        static IEnumerable<ValidationIssue> BackendRules(ReleaseGuardSettings guard, BuildTarget target, bool isDevelopmentBuild)
        {
            if (target == BuildTarget.WebGL || target == BuildTarget.iOS) yield break; // always IL2CPP

            var named = BuildDefines.Named(target);
            if (PlayerSettings.GetScriptingBackend(named) == ScriptingImplementation.IL2CPP) yield break;

            // Only an Error (build-blocking) once the project opted in through setup.
            var severity = isDevelopmentBuild ? IssueSeverity.Info
                : guard == null ? IssueSeverity.Warning
                : guard.requireIl2cppForReleaseBuilds ? IssueSeverity.Error
                : IssueSeverity.Info;
            yield return new ValidationIssue(severity,
                "Scripting backend is Mono: your C# can be decompiled back to near-original source in seconds. Use IL2CPP for release builds.",
                "Switch to IL2CPP", () => PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.IL2CPP));
        }

        static IEnumerable<ValidationIssue> SteamworksRules(IntegrityConfig config, BuildTarget target)
        {
            if (!IsStandalone(target)) yield break;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var steamworks = assemblies.FirstOrDefault(a => a.GetName().Name == SteamworksAssembly);
            if (steamworks == null) yield break;

            // The Steam module only compiles when GAMEPROTECTION_STEAMWORKS is defined (automatic for a Package Manager
            // install of Steamworks.NET). If the module assembly isn't loaded, the define is missing.
            var moduleLoaded = assemblies.Any(a => a.GetName().Name == "GameProtection.Steam");
            if (!moduleLoaded && !BuildDefines.IsDefined(target, SteamworksDefine))
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "Steamworks.NET found, but the Steam module is off (it wasn't installed through the Package Manager, so the define is missing).",
                    "Enable Steam module", () => BuildDefines.Add(BuildDefines.Named(target), SteamworksDefine));

            if (config != null && (config.steamAppIds ?? new uint[0]).All(id => id == 0))
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "Steamworks.NET found but no Steam App IDs set (add full game, demo and playtest IDs).");
        }

        static bool IsStandalone(BuildTarget target) =>
            BuildPipeline.GetBuildTargetGroup(target) == BuildTargetGroup.Standalone;

        static bool IsHttpUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        static void SyncAndSave(IntegrityConfig config)
        {
            ProtectionSetup.SyncIdentity(config);
            Save(config);
        }

        static void Save(UnityEngine.Object asset)
        {
            if (asset != null) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
    }
}
