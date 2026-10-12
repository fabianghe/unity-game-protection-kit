using System;
using System.Linq;
using GameProtection.Checks;
using NUnit.Framework;
using UnityEngine;

namespace GameProtection.Tests
{
    public sealed class IntegrityServiceTests
    {
        sealed class FixedCheck : IIntegrityCheck
        {
            readonly IntegrityResult _result;
            public FixedCheck(IntegrityResult result) => _result = result;
            public string Id => _result.CheckId;
            public IntegrityResult Run() => _result;
        }

        sealed class ThrowingCheck : IIntegrityCheck
        {
            public string Id => "throws";
            public IntegrityResult Run() => throw new InvalidOperationException();
        }

        [Test]
        public void AllPass_IsNotTampered()
        {
            var report = new IntegrityService(new[] { new FixedCheck(IntegrityResult.Pass("a")) }).Run(new BuildStamp());
            Assert.IsFalse(report.IsTampered);
        }

        [Test]
        public void Inconclusive_IsNotTampered()
        {
            var report = new IntegrityService(new[] { new FixedCheck(IntegrityResult.Inconclusive("a", "x")) }).Run(new BuildStamp());
            Assert.IsFalse(report.IsTampered);
        }

        [Test]
        public void AnyFail_IsTampered()
        {
            var report = new IntegrityService(new IIntegrityCheck[]
            {
                new FixedCheck(IntegrityResult.Pass("a")),
                new FixedCheck(IntegrityResult.Fail("b", "why")),
            }).Run(new BuildStamp());

            Assert.IsTrue(report.IsTampered);
            Assert.AreEqual("b", report.Failures.Single().CheckId);
        }

        [Test]
        public void ThrowingCheck_BecomesInconclusive_NeverFail()
        {
            var report = new IntegrityService(new IIntegrityCheck[] { new ThrowingCheck() }).Run(new BuildStamp());
            Assert.AreEqual(IntegrityStatus.Inconclusive, report.Results.Single().Status);
            Assert.IsFalse(report.IsTampered);
        }

        [Test]
        public void NullChecksAndStamp_AreHandled()
        {
            var report = new IntegrityService(null).Run(null);
            Assert.IsEmpty(report.Results);
            Assert.IsNotNull(report.Stamp);
        }
    }

    public sealed class CheckTests
    {
        [Test]
        public void AppIdentity_MatchingPlayerValues_Passes()
        {
            var check = new AppIdentityCheck(new[] { Application.identifier }, Application.productName, Application.companyName, true);
            Assert.AreEqual(IntegrityStatus.Pass, check.Run().Status);
        }

        [Test]
        public void AppIdentity_WrongProduct_Fails()
        {
            var check = new AppIdentityCheck(new string[0], Application.productName + "_stolen", null, false);
            Assert.AreEqual(IntegrityStatus.Fail, check.Run().Status);
        }

        [Test]
        public void AppIdentity_WrongIdentifier_FailsOnlyWhenEnforced()
        {
            var ids = new[] { "com.someone.else" };
            Assert.AreEqual(IntegrityStatus.Fail, new AppIdentityCheck(ids, null, null, true).Run().Status);
            Assert.AreEqual(IntegrityStatus.Pass, new AppIdentityCheck(ids, null, null, false).Run().Status);
        }

        [TestCase("https://mygame.com/play", IntegrityStatus.Pass)]
        [TestCase("https://play.mygame.com/", IntegrityStatus.Pass)]
        [TestCase("https://evilmygame.com/", IntegrityStatus.Fail)]
        [TestCase("https://adportal.example/game", IntegrityStatus.Fail)]
        [TestCase("http://localhost:8080/", IntegrityStatus.Inconclusive)]
        [TestCase("", IntegrityStatus.Inconclusive)]
        public void WebHost(string url, IntegrityStatus expected)
        {
            var check = new WebHostCheck(new[] { "mygame.com" }, () => url);
            Assert.AreEqual(expected, check.Run().Status);
        }

        [Test]
        public void WebHost_NoAllowlist_IsInconclusive()
        {
            Assert.AreEqual(IntegrityStatus.Inconclusive, new WebHostCheck(new string[0], () => "https://x.com").Run().Status);
        }

        [Test]
        public void AndroidHash_NormalizesPlayConsoleFormat()
        {
            Assert.AreEqual("ABCD12", AndroidSignatureCheck.NormalizeHash("ab:cd:12"));
            Assert.AreEqual("ABCD12", AndroidSignatureCheck.NormalizeHash(" AB CD 12 "));
        }

        [Test]
        public void AndroidChecks_OutsideDevice_AreInconclusive()
        {
            Assert.AreEqual(IntegrityStatus.Inconclusive, new AndroidSignatureCheck(new[] { "AA" }).Run().Status);
            Assert.AreEqual(IntegrityStatus.Inconclusive, new AndroidInstallerCheck(new[] { "com.android.vending" }).Run().Status);
        }
    }

    public sealed class EvidenceTests
    {
        [Test]
        public void OwnershipToken_HasNoRecognisablePrefix()
        {
            var token = GameProtection.Editor.ReleaseGuardSettings.NewToken();
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9a-f]{32}$"), token);
            Assert.AreNotEqual(token, GameProtection.Editor.ReleaseGuardSettings.NewToken());
        }

        [Test]
        public void CanaryObjectName_IsStablePerTokenAndNeverEmpty()
        {
            var name = GameProtection.Editor.ReleaseGuardSettings.CanaryObjectName("token-a");
            Assert.AreEqual(name, GameProtection.Editor.ReleaseGuardSettings.CanaryObjectName("token-a"));
            Assert.IsNotEmpty(name);
            Assert.IsNotEmpty(GameProtection.Editor.ReleaseGuardSettings.CanaryObjectName(null));
            Assert.IsFalse(name.Contains("token"));
        }
    }
}
