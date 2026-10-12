using UnityEngine;

namespace GameProtection
{
    /// <summary>
    /// Port for telemetry. Implement with Firebase, GameAnalytics, Unity Analytics or your own endpoint and assign
    /// GameProtectionKit.Reporter. The kit itself never sends data anywhere.
    /// </summary>
    public interface IIntegrityReporter
    {
        void Report(IntegrityReport report);
    }

    /// <summary>Default reporter: logs only when something isn't a clean pass, so player logs stay quiet.</summary>
    public sealed class DebugLogIntegrityReporter : IIntegrityReporter
    {
        public void Report(IntegrityReport report)
        {
            if (report.IsTampered) Debug.LogWarning("[GameProtection] " + report);
            else if (Debug.isDebugBuild) Debug.Log("[GameProtection] " + report);
        }
    }
}
