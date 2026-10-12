using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BitMono.Unity.Editor;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor.BitMono
{
    /// <summary>
    /// Makes BitMono (free, open-source obfuscator with IL2CPP metadata encryption) a one-click part of the kit:
    /// validates its setup, enables what matters for IL2CPP, and writes Unity-safe exclusions (Animation Events,
    /// UnityEvents) into BitMono's criticals.json so an obfuscated build doesn't silently lose callbacks.
    /// Compiles only when BitMono's Unity package is installed (GAMEPROTECTION_BITMONO).
    /// </summary>
    [InitializeOnLoad]
    public static class BitMonoIntegration
    {
        const string BeginMarker = "// >>> GameProtection: Unity-safe exclusions (auto-generated, do not edit between markers)";
        const string EndMarker = "// <<< GameProtection";
        const string DefaultConfigAssetPath = ProtectionSetup.EditorFolder + "/BitMonoConfig.asset";

        static BitMonoIntegration() => ProtectionValidator.ExtraRules.Add(Validate);

        // ---------- config access ----------

        public static BitMonoConfig FindConfig()
        {
            var guid = AssetDatabase.FindAssets("t:" + nameof(BitMonoConfig)).FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<BitMonoConfig>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static BitMonoConfig EnsureConfig()
        {
            var existing = FindConfig();
            if (existing != null) return existing;

            ProtectionSetup.EnsureFolder(ProtectionSetup.EditorFolder);
            var config = ScriptableObject.CreateInstance<BitMonoConfig>();
            AssetDatabase.CreateAsset(config, DefaultConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>Folder holding BitMono's criticals.json/obfuscation.json/protections.json, or null.</summary>
        public static string ConfigFolder(BitMonoConfig config)
        {
            if (config == null) return null;
            var path = config.GetDetectedConfigPath();
            return !string.IsNullOrEmpty(path) && Directory.Exists(path) && File.Exists(Path.Combine(path, "criticals.json")) ? path : null;
        }

        // ---------- menu ----------

        [MenuItem("Tools/Game Protection/Obfuscation/Add Unity-Safe Exclusions To BitMono", priority = 40)]
        public static void AddExclusionsFromMenu()
        {
            var added = AddUnitySafeExclusions(out var message);
            EditorUtility.DisplayDialog("Game Protection", message, "OK");
            if (added < 0) Debug.LogWarning("[GameProtection] " + message);
        }

        [MenuItem("Tools/Game Protection/Obfuscation/Select BitMono Config", priority = 41)]
        static void SelectConfig()
        {
            var config = EnsureConfig();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        // ---------- exclusions ----------

        /// <summary>
        /// Scans the project for names Unity calls by string and merges them into BitMono's criticals.json
        /// "CriticalMethods" list between markers. Idempotent. Returns the number of names written, or -1 on failure.
        /// </summary>
        public static int AddUnitySafeExclusions(out string message)
        {
            var folder = ConfigFolder(FindConfig());
            if (folder == null)
            {
                message = "BitMono's config folder (with criticals.json) was not found. Set BitMonoConfig > Config Path, or reinstall BitMono.";
                return -1;
            }

            var scan = UnityNameScanner.Scan();
            var names = scan.AllNames.ToList();
            var path = Path.Combine(folder, "criticals.json");
            try
            {
                var text = File.ReadAllText(path);
                var updated = Merge(text, names);
                if (updated == null)
                {
                    message = "Could not find the \"CriticalMethods\" list in criticals.json. The file may be from an unsupported BitMono version.";
                    return -1;
                }
                if (updated != text) File.WriteAllText(path, updated);
            }
            catch (IOException e)
            {
                message = "Could not update criticals.json: " + e.Message;
                return -1;
            }

            message = $"Added {names.Count} Unity-safe exclusion(s) to BitMono's criticals.json\n" +
                      $"({scan.AnimationEventFunctions.Count} Animation Event function(s) from {scan.ClipsScanned} clips, " +
                      $"{scan.UnityEventMethods.Count} UnityEvent method(s) from {scan.FilesScanned} files).\n\nRe-run this after adding new events.";
            Debug.Log("[GameProtection] " + message.Replace("\n", " "));
            return names.Count;
        }

        /// <summary>Pure text merge so BitMono's comments and formatting survive. Returns null when the list isn't found.</summary>
        public static string Merge(string criticalsJson, IEnumerable<string> names)
        {
            // Drop a previous generated block.
            var stripped = Regex.Replace(criticalsJson,
                @"\r?\n[ \t]*" + Regex.Escape(BeginMarker) + @".*?" + Regex.Escape(EndMarker), string.Empty, RegexOptions.Singleline);

            var match = Regex.Match(stripped, @"""CriticalMethods""\s*:\s*\[");
            if (!match.Success) return null;

            var block = new StringBuilder();
            block.Append('\n').Append("    ").Append(BeginMarker).Append('\n');
            foreach (var name in names)
                block.Append("    \"").Append(name.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\",\n");
            block.Append("    ").Append(EndMarker);

            return stripped.Insert(match.Index + match.Length, block.ToString());
        }

        public static bool HasGeneratedExclusions(string folder)
        {
            var path = Path.Combine(folder, "criticals.json");
            try { return File.Exists(path) && File.ReadAllText(path).Contains(BeginMarker); }
            catch (IOException) { return false; }
        }

        // ---------- validation ----------

        static readonly Regex AsmdefNameRegex = new Regex(@"""name""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
        static readonly Regex AsmdefPlatformsRegex = new Regex(@"""includePlatforms""\s*:\s*\[(.*?)\]", RegexOptions.Compiled | RegexOptions.Singleline);

        static IEnumerable<ValidationIssue> Validate(BuildTarget target, bool isDevelopmentBuild)
        {
            if (isDevelopmentBuild) yield break;

            var config = FindConfig();
            if (config == null)
            {
                yield return new ValidationIssue(IssueSeverity.Warning, "BitMono is installed but has no BitMonoConfig asset, so nothing gets obfuscated.",
                    "Create config", () => EnsureConfig());
                yield break;
            }

            if (!config.EnableObfuscation)
                yield return new ValidationIssue(IssueSeverity.Warning, "BitMono obfuscation is disabled in BitMonoConfig.",
                    "Enable", () => Set(config, c => c.EnableObfuscation = true));

            if (config.UseUnityUIForProtections && (config.ProtectionSettings == null || config.ProtectionSettings.All(p => !p.Enabled)))
                yield return new ValidationIssue(IssueSeverity.Error,
                    "BitMono: no protections are enabled, and BitMono fails the build in that state. Enable at least FullRenamer and StringsEncryption in BitMonoConfig.",
                    "Select config", SelectConfig);

            var named = BuildDefines.Named(target);
            var il2cpp = PlayerSettings.GetScriptingBackend(named) == ScriptingImplementation.IL2CPP;
            var metadataPlatform = target == BuildTarget.StandaloneWindows64 || target == BuildTarget.Android;
            if (il2cpp && metadataPlatform && !config.EncryptIl2CppMetadata)
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "BitMono can encrypt global-metadata.dat on this platform so static dumpers (Il2CppDumper, Cpp2IL) can't parse the files " +
                    "on disk. A memory dump of the running game still can, so treat it as one layer. It's off.",
                    "Enable metadata encryption", () => Set(config, c => c.EncryptIl2CppMetadata = true));

            var folder = ConfigFolder(config);
            if (folder == null)
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "BitMono's config folder (criticals.json) wasn't found, so Unity-safe exclusions can't be written. Set BitMonoConfig > Config Path.");
            else if (!HasGeneratedExclusions(folder))
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "Animation Events and UnityEvents call methods by name; an obfuscated build silently loses them unless they're excluded.",
                    "Add Unity-safe exclusions", () => AddUnitySafeExclusions(out _));

            // Assembly-CSharp only: every asmdef under Assets and every package assembly (this kit's GameProtection.Runtime
            // included) keeps its type and method names. Runtime inspectors search by those names.
            var custom = RuntimeAssembliesUnderAssets().ToList();
            var readable = new[] { "GameProtection.Runtime (this kit: ContentKey, KeyRing, SceneMetadata...)" }.Concat(custom.Take(5)).ToList();
            yield return new ValidationIssue(IssueSeverity.Info,
                "BitMono's Unity integration obfuscates Assembly-CSharp only. Code in these assemblies stays readable: " +
                string.Join(", ", readable) + (custom.Count > 5 ? $" (+{custom.Count - 5} more)" : string.Empty) +
                ". Move sensitive gameplay code out of asmdefs, or run BitMono.CLI on these DLLs in CI.");
        }

        /// <summary>Names of non-editor, non-test assembly definitions under Assets (what Assembly-CSharp-only obfuscation misses).</summary>
        static IEnumerable<string> RuntimeAssembliesUnderAssets()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                string text;
                try { text = File.ReadAllText(path); } catch (IOException) { continue; }

                var name = AsmdefNameRegex.Match(text).Groups[1].Value;
                if (string.IsNullOrEmpty(name) || name.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var platforms = AsmdefPlatformsRegex.Match(text).Groups[1].Value;
                var editorOnly = platforms.Contains("\"Editor\"") && !platforms.Contains("Standalone") && !platforms.Contains("Android") && !platforms.Contains("iOS") && !platforms.Contains("WebGL");
                if (editorOnly) continue;

                yield return name;
            }
        }

        static void Set(BitMonoConfig config, Action<BitMonoConfig> change)
        {
            Undo.RecordObject(config, "Change BitMono config");
            change(config);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }
    }
}
