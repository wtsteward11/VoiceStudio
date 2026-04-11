using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Helpers;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Seam tests for GAP-010: MaterialsHelper capability / cleanup and MainWindow shell init discipline.
    /// </summary>
    [TestClass]
    [TestCategory("Services")]
    public sealed class ShellBackdropTitleBarSeamTests
    {
        [TestMethod]
        public void MaterialsHelper_GetBestAvailableMaterial_ReturnsKnownEnum()
        {
            var best = MaterialsHelper.GetBestAvailableMaterial();
            Assert.IsTrue(
                best == MaterialsHelper.MaterialType.Mica
                || best == MaterialsHelper.MaterialType.DesktopAcrylic
                || best == MaterialsHelper.MaterialType.None,
                $"Unexpected material: {best}");
        }

        [TestMethod]
        public void MaterialsHelper_CleanupMaterial_IsIdempotent()
        {
            MaterialsHelper.CleanupMaterial();
            MaterialsHelper.CleanupMaterial();
            MaterialsHelper.CleanupMaterial();
        }

        [TestMethod]
        public void MaterialsHelper_ApplyMaterial_NoneBranch_IsDocumentedNonThrowingSwitchArm()
        {
            var appRoot = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "VoiceStudio.App", "Helpers", "MaterialsHelper.cs"));
            if (!File.Exists(appRoot))
            {
                Assert.Inconclusive($"MaterialsHelper.cs not found at {appRoot}");
            }

            var src = File.ReadAllText(appRoot);
            Assert.IsTrue(
                src.Contains("MaterialType.None => true", StringComparison.Ordinal),
                "ApplyMaterial must keep None => true (no-op controllers, safe for callers).");
        }

        [TestMethod]
        public void MainWindow_ShellInit_DoesNotCallBackdropFromConstructorRegion()
        {
            var mainPath = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "VoiceStudio.App", "MainWindow.xaml.cs"));
            var shellPath = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "VoiceStudio.App", "MainWindow.Shell.cs"));

            if (!File.Exists(mainPath))
            {
                Assert.Inconclusive($"MainWindow.xaml.cs not found at {mainPath}");
            }

            var mainSrc = File.ReadAllText(mainPath);
            var ctorIdx = mainSrc.IndexOf("public MainWindow(", StringComparison.Ordinal);
            Assert.IsTrue(ctorIdx >= 0, "MainWindow constructor not found.");

            var loadedIdx = mainSrc.IndexOf("contentFE.Loaded +=", StringComparison.Ordinal);
            Assert.IsTrue(loadedIdx >= 0, "contentFE.Loaded handler not found.");
            Assert.IsTrue(
                loadedIdx > ctorIdx,
                "Loaded handler must be registered after constructor start.");

            var preLoadedRegion = mainSrc.Substring(ctorIdx, loadedIdx - ctorIdx);
            Assert.IsFalse(
                preLoadedRegion.Contains("ApplyMicaBackdrop", StringComparison.Ordinal),
                "ApplyMicaBackdrop must not appear before contentFE.Loaded (ADR-047).");
            Assert.IsFalse(
                preLoadedRegion.Contains("InitializeCustomTitleBar", StringComparison.Ordinal),
                "InitializeCustomTitleBar must not appear before contentFE.Loaded (ADR-047).");
            Assert.IsFalse(
                preLoadedRegion.Contains("MaterialsHelper.", StringComparison.Ordinal),
                "MaterialsHelper must not be referenced before contentFE.Loaded.");

            var applyIdx = mainSrc.IndexOf("ApplyMicaBackdrop", StringComparison.Ordinal);
            Assert.IsTrue(applyIdx > loadedIdx, "ApplyMicaBackdrop must appear inside the Loaded handler.");

            if (File.Exists(shellPath))
            {
                var shellSrc = File.ReadAllText(shellPath);
                Assert.IsTrue(
                    shellSrc.Contains("private void ApplyMicaBackdrop", StringComparison.Ordinal),
                    "MainWindow.Shell.cs should define ApplyMicaBackdrop.");
            }
        }
    }
}
