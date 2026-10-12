using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>Editor-only build rules. Created by Tools > Game Protection > Setup inside an Editor folder, so it never ships.</summary>
    [CreateAssetMenu(menuName = "Game Protection/Release Guard Settings", fileName = "ReleaseGuardSettings")]
    public sealed class ReleaseGuardSettings : ScriptableObject
    {
        [Tooltip("Scripting define that marks a demo build. Add it to your demo Build Profile (Unity 6) or Player Settings.")]
        public string demoDefine = "GAME_DEMO";

        [Tooltip("The ONLY scenes a demo build may contain.")]
        public SceneAsset[] demoScenes = new SceneAsset[0];

        [Tooltip("Folders whose content must never end up in a demo: later levels, chapters, art, audio.")]
        public DefaultAsset[] fullGameOnlyFolders = new DefaultAsset[0];

        [Tooltip("Fail non-development builds that use the Mono backend (Mono ships your C# in a readable form).")]
        public bool requireIl2cppForReleaseBuilds = true;

        [Tooltip("Fail non-development builds when the validator reports errors.")]
        public bool failReleaseBuildsOnValidationErrors = true;

        [Tooltip("Move IL2CPP/Burst \"DoNotShip\" folders out of the build output after building.")]
        public bool moveDoNotShipFolders = true;

        [Tooltip("Environment variable naming the build channel (e.g. nextfest, press-outlet, itch). Falls back to demo/full.")]
        public string channelEnvironmentVariable = "BUILD_CHANNEL";

        [Header("Ownership canary")]
        [Tooltip("Permanent, project-unique token embedded in every build's data and scenes. Generated at setup. " +
                 "Never change it, and keep a copy in your evidence folder: finding it inside a copy proves the copy came from you.")]
        public string ownershipToken = "";

        [Tooltip("Plain-language ownership statement embedded next to the token.")]
        public string ownershipStatement = "";

        [Tooltip("Add a hidden canary object to every scene in the build (build only, your scenes on disk are untouched).")]
        public bool injectSceneCanaries = true;

        [Header("Obfuscation")]
        [Tooltip("Tick if you use an obfuscator the validator can't detect, to silence the warning.")]
        public bool obfuscatorConfiguredManually;

        public static string NewToken() => "GPK-" + Guid.NewGuid().ToString("N");

        public static ReleaseGuardSettings Find()
        {
            var guid = AssetDatabase.FindAssets("t:" + nameof(ReleaseGuardSettings)).FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<ReleaseGuardSettings>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public string[] DemoScenePaths() =>
            (demoScenes ?? Array.Empty<SceneAsset>()).Where(s => s != null).Select(AssetDatabase.GetAssetPath).ToArray();

        public string[] FullGameOnlyPaths() =>
            (fullGameOnlyFolders ?? Array.Empty<DefaultAsset>())
            .Where(f => f != null)
            .Select(AssetDatabase.GetAssetPath)
            .Where(AssetDatabase.IsValidFolder)
            .Select(p => p.TrimEnd('/') + "/")
            .ToArray();
    }
}
