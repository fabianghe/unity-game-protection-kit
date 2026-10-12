using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>
    /// Finds method names that Unity calls by STRING at runtime, which no obfuscator can see from the code alone:
    ///   - Animation Events (function names stored in AnimationClips, including clips inside FBX files)
    ///   - UnityEvent persistent listeners wired in the Inspector (m_MethodName in scenes, prefabs and assets)
    /// Renaming any of these silently breaks the game in the build only. Obfuscator modules feed this list into
    /// their exclusion config. Works with any obfuscator; BitMono integration uses it automatically.
    /// </summary>
    public static class UnityNameScanner
    {
        static readonly Regex MethodNameRegex = new Regex(@"^\s*m_MethodName:\s*(\S+)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly string[] SerializedAssetExtensions = { ".unity", ".prefab", ".asset", ".controller", ".overrideController", ".playable" };

        public sealed class Result
        {
            public SortedSet<string> AnimationEventFunctions { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public SortedSet<string> UnityEventMethods { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public int ClipsScanned { get; set; }
            public int FilesScanned { get; set; }

            public IEnumerable<string> AllNames => AnimationEventFunctions.Concat(UnityEventMethods).Distinct().OrderBy(n => n, StringComparer.Ordinal);
        }

        /// <summary>Scans the whole project (Assets only; packages are not obfuscated). Shows a progress bar.</summary>
        public static Result Scan(bool showProgress = true)
        {
            var result = new Result();
            try
            {
                ScanAnimationEvents(result, showProgress);
                ScanUnityEvents(result, showProgress);
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            return result;
        }

        static void ScanAnimationEvents(Result result, bool showProgress)
        {
            var guids = AssetDatabase.FindAssets("t:AnimationClip t:Model", new[] { "Assets" });
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
            for (var i = 0; i < paths.Length; i++)
            {
                if (showProgress && i % 10 == 0)
                    EditorUtility.DisplayProgressBar("Scanning Animation Events", paths[i], (float)i / paths.Length * 0.5f);

                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(paths[i]).OfType<AnimationClip>())
                {
                    result.ClipsScanned++;
                    foreach (var animationEvent in AnimationUtility.GetAnimationEvents(clip))
                        if (!string.IsNullOrEmpty(animationEvent.functionName))
                            result.AnimationEventFunctions.Add(animationEvent.functionName);
                }
            }
        }

        static void ScanUnityEvents(Result result, bool showProgress)
        {
            var files = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(f => SerializedAssetExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();

            for (var i = 0; i < files.Length; i++)
            {
                if (showProgress && i % 20 == 0)
                    EditorUtility.DisplayProgressBar("Scanning UnityEvents", Path.GetFileName(files[i]), 0.5f + (float)i / files.Length * 0.5f);

                string text;
                try { text = File.ReadAllText(files[i]); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                // Binary-serialized assets have no readable YAML; skip them (Edit > Project Settings > Editor > Asset Serialization = Force Text).
                if (!text.StartsWith("%YAML", StringComparison.Ordinal)) continue;
                result.FilesScanned++;

                foreach (Match match in MethodNameRegex.Matches(text))
                {
                    var name = match.Groups[1].Value.Trim('"', '\'');
                    if (name.Length > 0 && name != "{}") result.UnityEventMethods.Add(name);
                }
            }
        }
    }
}
