using System;
using System.Collections.Generic;
using System.Linq;

namespace GameProtection
{
    /// <summary>Runs checks and builds a report. Pure C#: no Unity lifecycle, no analytics, no UI (single responsibility).</summary>
    public sealed class IntegrityService
    {
        readonly IReadOnlyList<IIntegrityCheck> _checks;

        public IntegrityService(IEnumerable<IIntegrityCheck> checks) =>
            _checks = (checks ?? Enumerable.Empty<IIntegrityCheck>()).Where(c => c != null).ToList();

        public IntegrityReport Run(BuildStamp stamp)
        {
            var results = new List<IntegrityResult>(_checks.Count);
            foreach (var check in _checks)
            {
                try
                {
                    results.Add(check.Run());
                }
                catch (Exception e)
                {
                    // A broken check must never punish a legit player.
                    results.Add(IntegrityResult.Inconclusive(check.Id, "exception:" + e.GetType().Name));
                }
            }
            return new IntegrityReport(results, stamp);
        }
    }
}
