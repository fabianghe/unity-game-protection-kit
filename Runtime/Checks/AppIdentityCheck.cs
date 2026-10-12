using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameProtection.Checks
{
    /// <summary>
    /// A store re-upload has to change the package name, and most Steam/itch re-uploads rename the product or company.
    /// Cheapest, highest-signal check there is.
    /// The application identifier is only enforced on Android and iOS, where it is guaranteed to be meaningful.
    /// </summary>
    public sealed class AppIdentityCheck : IIntegrityCheck
    {
        public const string CheckId = "app_identity";

        readonly HashSet<string> _identifiers;
        readonly string _productName;
        readonly string _companyName;
        readonly bool _enforceIdentifier;

        public string Id => CheckId;

        public AppIdentityCheck(IEnumerable<string> identifiers, string productName, string companyName)
            : this(identifiers, productName, companyName, IsMobileRuntime) { }

        /// <summary>Test seam: lets tests force identifier enforcement on or off.</summary>
        internal AppIdentityCheck(IEnumerable<string> identifiers, string productName, string companyName, bool enforceIdentifier)
        {
            _identifiers = new HashSet<string>((identifiers ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            _productName = productName;
            _companyName = companyName;
            _enforceIdentifier = enforceIdentifier;
        }

        static bool IsMobileRuntime =>
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public IntegrityResult Run()
        {
            var identifier = Application.identifier;
            if (_enforceIdentifier && _identifiers.Count > 0 && !string.IsNullOrEmpty(identifier) && !_identifiers.Contains(identifier))
                return IntegrityResult.Fail(Id, "identifier=" + identifier);

            if (!string.IsNullOrEmpty(_productName) && Application.productName != _productName)
                return IntegrityResult.Fail(Id, "product=" + Application.productName);

            if (!string.IsNullOrEmpty(_companyName) && Application.companyName != _companyName)
                return IntegrityResult.Fail(Id, "company=" + Application.companyName);

            return IntegrityResult.Pass(Id);
        }
    }
}
