// Sample: send integrity signals to Firebase Analytics.
// Requires the Firebase Analytics SDK in your project. Import this sample, and it registers itself automatically.
// Then in Firebase, build a report on the "integrity_signal" event, broken down by check, detail and channel.

using System.Linq;
using Firebase.Analytics;
using GameProtection;
using UnityEngine;

public sealed class FirebaseIntegrityReporter : IIntegrityReporter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() => GameProtectionKit.Reporter = new FirebaseIntegrityReporter();

    public void Report(IntegrityReport report)
    {
        // Everything that isn't a clean Pass: Fail = stolen copy, Inconclusive = where copies come from.
        foreach (var result in report.Results.Where(r => r.Status != IntegrityStatus.Pass))
        {
            FirebaseAnalytics.LogEvent("integrity_signal",
                new Parameter("check", result.CheckId),
                new Parameter("status", result.Status.ToString()),
                new Parameter("detail", Truncate(result.Detail)),
                new Parameter("build_id", report.Stamp.buildId),
                new Parameter("channel", report.Stamp.channel));
        }
    }

    // Firebase limits parameter values to 100 characters.
    static string Truncate(string value) => value.Length <= 100 ? value : value.Substring(0, 100);
}
