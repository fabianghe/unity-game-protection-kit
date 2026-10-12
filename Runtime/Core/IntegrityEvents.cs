using System;
using UnityEngine;

namespace GameProtection
{
    /// <summary>Observer hub. Subscribe to react to a finished report (analytics, UI, logging).</summary>
    public static class IntegrityEvents
    {
        public static event Action<IntegrityReport> ReportReady;

        /// <summary>Last published report, so late subscribers don't miss it. Null until checks have run.</summary>
        public static IntegrityReport Latest { get; private set; }

        internal static void Publish(IntegrityReport report)
        {
            Latest = report;
            if (ReportReady == null) return;

            // One faulty subscriber must not stop the others.
            foreach (Action<IntegrityReport> handler in ReportReady.GetInvocationList())
            {
                try { handler(report); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // Clean static state when "Enter Play Mode Options" disables domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ReportReady = null;
            Latest = null;
        }
    }
}
