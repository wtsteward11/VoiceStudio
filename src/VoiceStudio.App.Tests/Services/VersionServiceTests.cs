using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Unit tests for VersionService.
    /// Tests version information retrieval and formatting.
    /// </summary>
    [TestClass]
    public class VersionServiceTests
    {
        #region Version Property Tests

        [TestMethod]
        public void Version_ReturnsNonNullString()
        {
            // Act
            var version = VersionService.Version;

            // Assert
            Assert.IsNotNull(version);
        }

        [TestMethod]
        public void Version_ReturnsNonEmptyString()
        {
            // Act
            var version = VersionService.Version;

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(version));
        }

        [TestMethod]
        public void Version_ContainsDots()
        {
            // Act
            var version = VersionService.Version;

            // Assert - Version should be in format like "1.0.0" or "1.0.0.0"
            Assert.IsTrue(version.Contains('.'), "Version should contain dots separating components");
        }

        [TestMethod]
        public void Version_HasValidFormat()
        {
            // Act
            var version = VersionService.Version;

            // Assert - Should be parseable or at least have number-like components
            var parts = version.Split('.');
            Assert.IsTrue(parts.Length >= 2, "Version should have at least 2 components");

            // First part should be numeric
            Assert.IsTrue(int.TryParse(parts[0], out var major), "Major version should be numeric");
            Assert.IsTrue(major >= 0, "Major version should be non-negative");
        }

        [TestMethod]
        public void Version_IsCached()
        {
            // Act
            var version1 = VersionService.Version;
            var version2 = VersionService.Version;

            // Assert - Should return same instance (cached)
            Assert.AreSame(version1, version2);
        }

        #endregion

        #region BuildDate Property Tests

        [TestMethod]
        public void BuildDate_ReturnsNonNullString()
        {
            // Act
            var buildDate = VersionService.BuildDate;

            // Assert
            Assert.IsNotNull(buildDate);
        }

        [TestMethod]
        public void BuildDate_ReturnsNonEmptyString()
        {
            // Act
            var buildDate = VersionService.BuildDate;

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(buildDate));
        }

        [TestMethod]
        public void BuildDate_HasValidDateFormat()
        {
            // Act
            var buildDate = VersionService.BuildDate;

            // Assert - Should be in yyyy-MM-dd format
            Assert.IsTrue(DateTime.TryParse(buildDate, out var parsedDate), 
                "BuildDate should be parseable as a date");
        }

        [TestMethod]
        public void BuildDate_IsNotFuture()
        {
            // Act
            var buildDate = VersionService.BuildDate;
            var parsed = DateTime.Parse(buildDate);

            // Assert
            Assert.IsTrue(parsed <= DateTime.Now.AddDays(1), 
                "Build date should not be significantly in the future");
        }

        [TestMethod]
        public void BuildDate_IsCached()
        {
            // Act
            var date1 = VersionService.BuildDate;
            var date2 = VersionService.BuildDate;

            // Assert
            Assert.AreSame(date1, date2);
        }

        #endregion

        #region FullVersion Property Tests

        [TestMethod]
        public void FullVersion_ReturnsNonNullString()
        {
            // Act
            var fullVersion = VersionService.FullVersion;

            // Assert
            Assert.IsNotNull(fullVersion);
        }

        [TestMethod]
        public void FullVersion_ContainsVersion()
        {
            // Act
            var fullVersion = VersionService.FullVersion;
            var version = VersionService.Version;

            // Assert
            Assert.IsTrue(fullVersion.Contains(version), 
                "FullVersion should contain the version number");
        }

        [TestMethod]
        public void FullVersion_ContainsBuildDate()
        {
            // Act
            var fullVersion = VersionService.FullVersion;
            var buildDate = VersionService.BuildDate;

            // Assert
            Assert.IsTrue(fullVersion.Contains(buildDate), 
                "FullVersion should contain the build date");
        }

        [TestMethod]
        public void FullVersion_ContainsBuildKeyword()
        {
            // Act
            var fullVersion = VersionService.FullVersion;

            // Assert
            Assert.IsTrue(fullVersion.Contains("Build"), 
                "FullVersion should contain 'Build' keyword");
        }

        [TestMethod]
        public void FullVersion_HasParentheses()
        {
            // Act
            var fullVersion = VersionService.FullVersion;

            // Assert
            Assert.IsTrue(fullVersion.Contains("(") && fullVersion.Contains(")"),
                "FullVersion should have parentheses around build info");
        }

        #endregion

        #region ApplicationName Property Tests

        [TestMethod]
        public void ApplicationName_ReturnsVoiceStudio()
        {
            // Act
            var appName = VersionService.ApplicationName;

            // Assert
            Assert.AreEqual("VoiceStudio Quantum+", appName);
        }

        [TestMethod]
        public void ApplicationName_IsNotNull()
        {
            // Assert
            Assert.IsNotNull(VersionService.ApplicationName);
        }

        [TestMethod]
        public void ApplicationName_IsNotEmpty()
        {
            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(VersionService.ApplicationName));
        }

        #endregion

        #region Copyright Property Tests

        [TestMethod]
        public void Copyright_ReturnsNonNullString()
        {
            // Act
            var copyright = VersionService.Copyright;

            // Assert
            Assert.IsNotNull(copyright);
        }

        [TestMethod]
        public void Copyright_ContainsCopyrightSymbol()
        {
            // Act
            var copyright = VersionService.Copyright;

            // Assert
            Assert.IsTrue(copyright.Contains("©") || copyright.Contains("(c)") || copyright.Contains("Copyright"),
                "Copyright should contain copyright symbol or text");
        }

        [TestMethod]
        public void Copyright_ContainsVoiceStudio()
        {
            // Act
            var copyright = VersionService.Copyright;

            // Assert
            Assert.IsTrue(copyright.Contains("VoiceStudio"),
                "Copyright should mention VoiceStudio");
        }

        #endregion

        #region RuntimeVersion Property Tests

        [TestMethod]
        public void RuntimeVersion_ReturnsNonNullString()
        {
            // Act
            var runtimeVersion = VersionService.RuntimeVersion;

            // Assert
            Assert.IsNotNull(runtimeVersion);
        }

        [TestMethod]
        public void RuntimeVersion_MatchesEnvironmentVersion()
        {
            // Act
            var runtimeVersion = VersionService.RuntimeVersion;

            // Assert
            Assert.AreEqual(Environment.Version.ToString(), runtimeVersion);
        }

        [TestMethod]
        public void RuntimeVersion_ContainsDots()
        {
            // Act
            var runtimeVersion = VersionService.RuntimeVersion;

            // Assert
            Assert.IsTrue(runtimeVersion.Contains('.'),
                "Runtime version should have dot separators");
        }

        #endregion

        #region OSVersion Property Tests

        [TestMethod]
        public void OSVersion_ReturnsNonNullString()
        {
            // Act
            var osVersion = VersionService.OSVersion;

            // Assert
            Assert.IsNotNull(osVersion);
        }

        [TestMethod]
        public void OSVersion_ReturnsNonEmptyString()
        {
            // Act
            var osVersion = VersionService.OSVersion;

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(osVersion));
        }

        [TestMethod]
        public void OSVersion_ContainsPlatformInfo()
        {
            // Act
            var osVersion = VersionService.OSVersion;

            // Assert
            Assert.IsTrue(osVersion.Contains("Win") || osVersion.Contains("Unix") || osVersion.Contains("Unknown"),
                "OS version should contain platform information");
        }

        #endregion
    }
}
