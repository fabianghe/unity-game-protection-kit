using System.Collections.Generic;
using System.Linq;

namespace GameProtection
{
    public enum IntegrityStatus
    {
        /// <summary>The check confirmed this is your build.</summary>
        Pass,

        /// <summary>Strong evidence of a re-upload, re-sign or rehost. Only use for signals a legit player can never trigger.</summary>
        Fail,

        /// <summary>Couldn't tell (platform not applicable, service not running, sideloaded). Reported, never punished.</summary>
        Inconclusive,
    }

    public readonly struct IntegrityResult
    {
        public string CheckId { get; }
        public IntegrityStatus Status { get; }
        public string Detail { get; }

        IntegrityResult(string checkId, IntegrityStatus status, string detail)
        {
            CheckId = checkId;
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public static IntegrityResult Pass(string checkId) => new IntegrityResult(checkId, IntegrityStatus.Pass, null);
        public static IntegrityResult Fail(string checkId, string detail) => new IntegrityResult(checkId, IntegrityStatus.Fail, detail);
        public static IntegrityResult Inconclusive(string checkId, string detail) => new IntegrityResult(checkId, IntegrityStatus.Inconclusive, detail);

        public override string ToString() => $"{CheckId}:{Status}:{Detail}";
    }

    /// <summary>
    /// One self-contained check (Strategy pattern). Implement this to add a check without touching anything else.
    /// Rules: never throw on purpose, never block for long, and only return Fail on evidence a legit player can't produce.
    /// </summary>
    public interface IIntegrityCheck
    {
        string Id { get; }
        IntegrityResult Run();
    }

    public sealed class IntegrityReport
    {
        public IReadOnlyList<IntegrityResult> Results { get; }
        public BuildStamp Stamp { get; }

        public bool IsTampered => Results.Any(r => r.Status == IntegrityStatus.Fail);
        public IEnumerable<IntegrityResult> Failures => Results.Where(r => r.Status == IntegrityStatus.Fail);

        public IntegrityReport(IReadOnlyList<IntegrityResult> results, BuildStamp stamp)
        {
            Results = results;
            Stamp = stamp ?? new BuildStamp();
        }

        public override string ToString() =>
            $"build={Stamp.buildId} channel={Stamp.channel} tampered={IsTampered} | {string.Join(" | ", Results)}";
    }
}
