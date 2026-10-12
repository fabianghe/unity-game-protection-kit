using System;
using UnityEngine;

namespace GameProtection
{
    /// <summary>
    /// Written into the build by the editor (see ReleaseBuildGuards). Tells you exactly which build a copy came from:
    /// demo vs full, Next Fest vs a press outlet vs Discord testers, plus commit and date.
    /// </summary>
    [Serializable]
    public sealed class BuildStamp
    {
        public const string ResourceName = "GameProtectionBuildStamp";

        public string buildId = "editor";
        public string channel = "editor";
        public string version = "";
        public string builtAtUtc = "";
        public string commit = "";

        /// <summary>Permanent project-unique token. Survives asset ripping, so it proves a rebuilt clone came from your build.</summary>
        public string ownerToken = "";
        public string ownerStatement = "";

        public static BuildStamp Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return new BuildStamp();

            try { return JsonUtility.FromJson<BuildStamp>(asset.text) ?? new BuildStamp(); }
            catch (ArgumentException) { return new BuildStamp(); }
        }
    }
}
