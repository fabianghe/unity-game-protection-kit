using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameProtection.Checks
{
    /// <summary>
    /// WebGL builds get copied to ad-stuffed game portals. This checks the page host against your allowlist
    /// (exact host or any subdomain of it). Look at Inconclusive/Fail details in your reports to learn the real
    /// hosts your legit portals serve from (itch.io, for example, serves games from a separate CDN domain).
    /// </summary>
    public sealed class WebHostCheck : IIntegrityCheck
    {
        public const string CheckId = "web_host";

        static readonly string[] LocalHosts = { "localhost", "127.0.0.1", "::1", "[::1]" };

        readonly string[] _allowedHosts;
        readonly Func<string> _urlProvider;

        public string Id => CheckId;

        public WebHostCheck(IEnumerable<string> allowedHosts) : this(allowedHosts, () => Application.absoluteURL) { }

        /// <summary>Test seam for injecting a URL.</summary>
        internal WebHostCheck(IEnumerable<string> allowedHosts, Func<string> urlProvider)
        {
            _allowedHosts = (allowedHosts ?? Enumerable.Empty<string>())
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h.Trim().TrimStart('.').ToLowerInvariant())
                .ToArray();
            _urlProvider = urlProvider;
        }

        public IntegrityResult Run()
        {
            if (_allowedHosts.Length == 0) return IntegrityResult.Inconclusive(Id, "no_expected_hosts");

            var url = _urlProvider?.Invoke();
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                return IntegrityResult.Inconclusive(Id, "no_url");

            var host = uri.Host.ToLowerInvariant();
            if (LocalHosts.Contains(host)) return IntegrityResult.Inconclusive(Id, "local");

            var allowed = _allowedHosts.Any(a => host == a || host.EndsWith("." + a, StringComparison.Ordinal));
            return allowed ? IntegrityResult.Pass(Id) : IntegrityResult.Fail(Id, "host=" + host);
        }
    }
}
