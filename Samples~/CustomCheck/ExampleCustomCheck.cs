// Sample: the template for writing your own check. Copy, rename, change Run().
// Rule of thumb: return Fail only on evidence a legit player can never produce. When in doubt, return Inconclusive.

using System.IO;
using GameProtection;
using UnityEngine;

/// <summary>
/// Example: your build ships a marker file in StreamingAssets. Asset-rip-and-rebuild clones usually lose it.
/// Put any small file at Assets/StreamingAssets/build.marker to try it.
/// </summary>
public sealed class StreamingAssetMarkerCheck : IIntegrityCheck
{
    const string MarkerFile = "build.marker";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() => GameProtectionKit.RegisterCheck(new StreamingAssetMarkerCheck());

    public string Id => "streaming_marker";

    public IntegrityResult Run()
    {
        // On Android and WebGL StreamingAssets are not plain files; this simple sample doesn't cover them.
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
            return IntegrityResult.Inconclusive(Id, "unsupported_platform");

        // Starts as telemetry (Inconclusive). Switch to Fail only after you've confirmed every legit build ships the
        // marker, otherwise forgetting the file would show real players the stolen-copy notice.
        return File.Exists(Path.Combine(Application.streamingAssetsPath, MarkerFile))
            ? IntegrityResult.Pass(Id)
            : IntegrityResult.Inconclusive(Id, "marker_missing");
    }
}
