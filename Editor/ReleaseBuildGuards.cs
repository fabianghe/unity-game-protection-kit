using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace GameProtection.Editor
{
    /// <summary>
    /// Runs automatically on every build:
    /// 1) Validation: release builds fail on errors (Mono backend, config not matching Player Settings, ...).
    /// 2) Demo leak guard: demo builds fail if they contain non-demo scenes or full-game assets.
    /// 3) Build stamp: every build gets an ID + channel, logged to GameProtectionBuildLedger.csv in the project root.
    ///    The stamp and a canary in every scene carry your permanent ownership token, which survives asset ripping.
    /// 4) Moves IL2CPP/Burst "DoNotShip" folders out of the output so they never get uploaded.
    /// </summary>
    sealed class ReleaseBuildGuards : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport
    {
        static readonly string StampAssetPath = $"{ProtectionSetup.GeneratedResourcesFolder}/{BuildStamp.ResourceName}.json";
        const string LedgerFile = "GameProtectionBuildLedger.csv";

        static readonly string[] DoNotShipMarkers =
        {
            "_BackUpThisFolder_ButDontShipItWithYourProduct",
            "_BurstDebugInformation_DoNotShip",
        };

        static bool _isDemoBuild;
        static string _buildId;

        public int callbackOrder => -1000;

        // ---------- 1 + 3: before the build ----------

        public void OnPreprocessBuild(BuildReport report)
        {
            var summary = report.summary;
            var isDevelopment = (summary.options & BuildOptions.Development) != 0;
            var guard = ReleaseGuardSettings.Find();

            _buildId = null;
            _isDemoBuild = guard != null && BuildDefines.IsDefined(summary.platform, guard.demoDefine);
            ContentLockEditor.OnPreprocessBuild(_isDemoBuild); // independent of the guards: embeds keys only when Content Lock is on
            if (guard == null)
            {
                // Installed but not set up yet: never break or touch someone's build.
                Debug.Log("[GameProtection] Not set up yet (Tools > Game Protection > Setup). Build guards skipped.");
                return;
            }

            ProtectionSetup.EnsureOwnership(guard);

            var issues = ProtectionValidator.Validate(summary.platform, isDevelopment);
            foreach (var issue in issues)
            {
                var line = "[GameProtection] " + issue.Message;
                if (issue.Severity == IssueSeverity.Error) Debug.LogError(line);
                else if (issue.Severity == IssueSeverity.Warning) Debug.LogWarning(line);
            }

            if (!isDevelopment && guard.failReleaseBuildsOnValidationErrors && issues.Any(i => i.Severity == IssueSeverity.Error))
                throw new BuildFailedException(
                    "[GameProtection] Release build blocked by validation errors (see Console, or Project Settings > Game Protection).");

            if (_isDemoBuild) AssertNoFullGameResources(guard);

            WriteStamp(guard, isDevelopment, summary.platform);
        }

        // ---------- 2: every scene that goes into the build ----------

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return; // null report = entering Play Mode
            var guard = ReleaseGuardSettings.Find();
            if (guard == null) return;

            if (_isDemoBuild) AssertDemoScene(scene, guard);
            if (guard.injectSceneCanaries && !string.IsNullOrWhiteSpace(guard.ownershipToken)) InjectCanary(scene, guard);
        }

        /// <summary>Build-only: the scene file on disk is never modified.</summary>
        static void InjectCanary(Scene scene, ReleaseGuardSettings guard)
        {
            var canary = new GameObject("SceneSettings");
            canary.SetActive(false);
            SceneManager.MoveGameObjectToScene(canary, scene);
            canary.AddComponent<SceneMetadata>().Set($"{guard.ownershipToken}|{_buildId}|{guard.ownershipStatement}");
        }

        static void AssertDemoScene(Scene scene, ReleaseGuardSettings guard)
        {
            if (!guard.DemoScenePaths().Contains(scene.path))
                throw new BuildFailedException($"[GameProtection] Demo build contains a non-demo scene: {scene.path}. " +
                                               "Remove it from the demo Build Profile or add it to ReleaseGuardSettings > Demo Scenes.");

            var forbidden = guard.FullGameOnlyPaths();
            var leaked = AssetDatabase.GetDependencies(scene.path, true)
                .Where(dep => forbidden.Any(f => dep.StartsWith(f, StringComparison.Ordinal)))
                .Take(10).ToArray();
            if (leaked.Length > 0)
                throw new BuildFailedException($"[GameProtection] Demo scene {scene.path} references full-game assets:\n  " +
                                               string.Join("\n  ", leaked));
        }

        // ---------- 4: after the build ----------

        public void OnPostprocessBuild(BuildReport report)
        {
            ContentLockEditor.OnPostprocessBuild();
            var guard = ReleaseGuardSettings.Find();
            if (guard == null) return;

            if (guard.moveDoNotShipFolders)
                MoveDoNotShipFolders(report.summary.outputPath, _buildId);

            RemoveStamp(); // editor stays on the "editor" stamp
        }

        internal static void RemoveStamp()
        {
            if (File.Exists(StampAssetPath)) AssetDatabase.DeleteAsset(StampAssetPath);
        }

        // ---------- helpers ----------

        static void AssertNoFullGameResources(ReleaseGuardSettings guard)
        {
            // Anything inside a Resources folder ships, referenced or not.
            var leaked = guard.FullGameOnlyPaths()
                .SelectMany(folder => AssetDatabase.FindAssets(string.Empty, new[] { folder.TrimEnd('/') }))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path))
                .Distinct().Take(10).ToArray();
            if (leaked.Length > 0)
                throw new BuildFailedException("[GameProtection] Full-game assets inside Resources folders would ship in the demo:\n  " +
                                               string.Join("\n  ", leaked));
        }

        static void WriteStamp(ReleaseGuardSettings guard, bool isDevelopment, BuildTarget platform)
        {
            var channelVar = guard.channelEnvironmentVariable;
            var envChannel = string.IsNullOrWhiteSpace(channelVar) ? null : Environment.GetEnvironmentVariable(channelVar);

            var stamp = new BuildStamp
            {
                buildId = Guid.NewGuid().ToString("N").Substring(0, 12),
                channel = (string.IsNullOrWhiteSpace(envChannel) ? (_isDemoBuild ? "demo" : "full") : envChannel.Trim())
                          + (isDevelopment ? "-dev" : string.Empty),
                version = PlayerSettings.bundleVersion,
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                commit = GitCommit(),
                ownerToken = guard.ownershipToken,
                ownerStatement = guard.ownershipStatement,
            };
            _buildId = stamp.buildId;

            ProtectionSetup.EnsureGeneratedFolder();
            File.WriteAllText(StampAssetPath, JsonUtility.ToJson(stamp));
            AssetDatabase.ImportAsset(StampAssetPath, ImportAssetOptions.ForceSynchronousImport);

            if (!File.Exists(LedgerFile)) File.WriteAllText(LedgerFile, "builtAtUtc,buildId,channel,version,commit,platform\n");
            File.AppendAllText(LedgerFile, $"{stamp.builtAtUtc},{stamp.buildId},{Csv(stamp.channel)},{Csv(stamp.version)},{stamp.commit},{platform}\n");
            Debug.Log($"[GameProtection] Build stamped {stamp.buildId} ({stamp.channel}).");
        }

        static string Csv(string value) => (value ?? string.Empty).Replace(",", ";");

        static void MoveDoNotShipFolders(string outputPath, string buildId)
        {
            if (string.IsNullOrEmpty(outputPath)) return;

            var buildDir = Directory.Exists(outputPath) && !outputPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                ? outputPath
                : Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(buildDir) || !Directory.Exists(buildDir)) return;

            var symbolsDir = Path.Combine(buildDir.TrimEnd('/', '\\') + "_symbols", buildId ?? "unknown");
            foreach (var dir in Directory.GetDirectories(buildDir))
            {
                var name = Path.GetFileName(dir);
                if (!DoNotShipMarkers.Any(marker => name.Contains(marker))) continue;

                Directory.CreateDirectory(symbolsDir);
                var target = Path.Combine(symbolsDir, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(dir, target);
                Debug.Log($"[GameProtection] Moved {name} out of the build (kept in {symbolsDir} for crash symbolication).");
            }
        }

        static string GitCommit()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(psi))
                {
                    if (process == null) return string.Empty;
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(2000);
                    return process.ExitCode == 0 ? output : string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
