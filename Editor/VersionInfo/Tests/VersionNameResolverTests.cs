using NUnit.Framework;
using UnityEditor;

namespace Com.Scheherazade.Common.VIC.Editor.Tests
{
    public sealed class VersionNameResolverTests
    {
        private string _bundleVersion;
        private int _androidBundleVersionCode;
        private string _iosBuildNumber;

        [SetUp]
        public void SetUp()
        {
            _bundleVersion = PlayerSettings.bundleVersion;
            _androidBundleVersionCode =
                PlayerSettings.Android.bundleVersionCode;
            _iosBuildNumber = PlayerSettings.iOS.buildNumber;
        }

        [TearDown]
        public void TearDown()
        {
            PlayerSettings.bundleVersion = _bundleVersion;
            PlayerSettings.Android.bundleVersionCode =
                _androidBundleVersionCode;
            PlayerSettings.iOS.buildNumber = _iosBuildNumber;
        }

        [Test]
        public void Resolve_PlatformUsesExplicitBuildTarget()
        {
            string resolved = VersionInfoSettingsProvider.VersionNameResolver
                .Resolve("{platform}", BuildTarget.Android);

            Assert.That(resolved, Is.EqualTo(BuildTarget.Android.ToString()));
        }

        [Test]
        public void Resolve_DefaultOverloadUsesActiveBuildTarget()
        {
            string resolved = VersionInfoSettingsProvider.VersionNameResolver
                .Resolve("{platform}");

            Assert.That(
                resolved,
                Is.EqualTo(EditorUserBuildSettings.activeBuildTarget.ToString())
            );
        }

        [Test]
        public void Resolve_AppBundleUsesExplicitAndroidTarget()
        {
            PlayerSettings.bundleVersion = "desktop-version";
            PlayerSettings.Android.bundleVersionCode = 2468;
            PlayerSettings.iOS.buildNumber = "ios-version";

            string resolved = VersionInfoSettingsProvider.VersionNameResolver
                .Resolve("{app-bundle}", BuildTarget.Android);

            Assert.That(resolved, Is.EqualTo("2468"));
        }

        [Test]
        public void Resolve_AppBundleUsesExplicitIosTarget()
        {
            PlayerSettings.bundleVersion = "desktop-version";
            PlayerSettings.Android.bundleVersionCode = 2468;
            PlayerSettings.iOS.buildNumber = "ios-version";

            string resolved = VersionInfoSettingsProvider.VersionNameResolver
                .Resolve("{app-bundle}", BuildTarget.iOS);

            Assert.That(resolved, Is.EqualTo("ios-version"));
        }

        [Test]
        public void Resolve_AppBundleUsesGenericVersionForOtherTargets()
        {
            PlayerSettings.bundleVersion = "desktop-version";
            PlayerSettings.Android.bundleVersionCode = 2468;
            PlayerSettings.iOS.buildNumber = "ios-version";

            string resolved = VersionInfoSettingsProvider.VersionNameResolver
                .Resolve("{app-bundle}", BuildTarget.StandaloneWindows64);

            Assert.That(resolved, Is.EqualTo("desktop-version"));
        }
    }
}
