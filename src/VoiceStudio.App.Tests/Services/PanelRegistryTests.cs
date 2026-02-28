using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Services
{
    [TestClass]
    public class PanelRegistryTests
    {
        private PanelRegistry _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _sut = new PanelRegistry();
        }

        #region RegisterPanel Tests

        [TestMethod]
        public void RegisterPanel_AddsPanel()
        {
            var panel = new TestPanelView("test1", "Test Panel", PanelRegion.Left);

            _sut.RegisterPanel(panel);

            var panels = _sut.GetPanelsForRegion(PanelRegion.Left).ToList();
            Assert.AreEqual(1, panels.Count);
            Assert.AreSame(panel, panels[0]);
        }

        [TestMethod]
        public void RegisterPanel_DoesNotAddDuplicates()
        {
            var panel = new TestPanelView("test1", "Test Panel", PanelRegion.Left);

            _sut.RegisterPanel(panel);
            _sut.RegisterPanel(panel);

            var panels = _sut.GetPanelsForRegion(PanelRegion.Left).ToList();
            Assert.AreEqual(1, panels.Count);
        }

        [TestMethod]
        public void RegisterPanel_AllowsDifferentPanels()
        {
            var panel1 = new TestPanelView("test1", "Test Panel 1", PanelRegion.Left);
            var panel2 = new TestPanelView("test2", "Test Panel 2", PanelRegion.Left);

            _sut.RegisterPanel(panel1);
            _sut.RegisterPanel(panel2);

            var panels = _sut.GetPanelsForRegion(PanelRegion.Left).ToList();
            Assert.AreEqual(2, panels.Count);
        }

        #endregion

        #region GetPanelsForRegion Tests

        [TestMethod]
        public void GetPanelsForRegion_ReturnsEmptyForUnregisteredRegion()
        {
            var panels = _sut.GetPanelsForRegion(PanelRegion.Left).ToList();
            Assert.AreEqual(0, panels.Count);
        }

        [TestMethod]
        public void GetPanelsForRegion_ReturnsPanelsOnlyForRequestedRegion()
        {
            var leftPanel = new TestPanelView("left1", "Left Panel", PanelRegion.Left);
            var rightPanel = new TestPanelView("right1", "Right Panel", PanelRegion.Right);
            var centerPanel = new TestPanelView("center1", "Center Panel", PanelRegion.Center);

            _sut.RegisterPanel(leftPanel);
            _sut.RegisterPanel(rightPanel);
            _sut.RegisterPanel(centerPanel);

            var leftPanels = _sut.GetPanelsForRegion(PanelRegion.Left).ToList();
            Assert.AreEqual(1, leftPanels.Count);
            Assert.AreSame(leftPanel, leftPanels[0]);
        }

        [TestMethod]
        public void GetPanelsForRegion_ReturnsAllPanelsForRegion()
        {
            var panel1 = new TestPanelView("left1", "Left Panel 1", PanelRegion.Left);
            var panel2 = new TestPanelView("left2", "Left Panel 2", PanelRegion.Left);
            var panel3 = new TestPanelView("left3", "Left Panel 3", PanelRegion.Left);

            _sut.RegisterPanel(panel1);
            _sut.RegisterPanel(panel2);
            _sut.RegisterPanel(panel3);

            var panels = _sut.GetPanelsForRegion(PanelRegion.Left).ToList();
            Assert.AreEqual(3, panels.Count);
        }

        [TestMethod]
        public void GetPanelsForRegion_SupportsAllRegions()
        {
            _sut.RegisterPanel(new TestPanelView("1", "1", PanelRegion.Left));
            _sut.RegisterPanel(new TestPanelView("2", "2", PanelRegion.Right));
            _sut.RegisterPanel(new TestPanelView("3", "3", PanelRegion.Center));
            _sut.RegisterPanel(new TestPanelView("4", "4", PanelRegion.Bottom));
            _sut.RegisterPanel(new TestPanelView("5", "5", PanelRegion.Floating));

            Assert.AreEqual(1, _sut.GetPanelsForRegion(PanelRegion.Left).Count());
            Assert.AreEqual(1, _sut.GetPanelsForRegion(PanelRegion.Right).Count());
            Assert.AreEqual(1, _sut.GetPanelsForRegion(PanelRegion.Center).Count());
            Assert.AreEqual(1, _sut.GetPanelsForRegion(PanelRegion.Bottom).Count());
            Assert.AreEqual(1, _sut.GetPanelsForRegion(PanelRegion.Floating).Count());
        }

        #endregion

        #region GetDefaultPanel Tests

        [TestMethod]
        public void GetDefaultPanel_ReturnsNullForEmptyRegion()
        {
            var result = _sut.GetDefaultPanel(PanelRegion.Left);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetDefaultPanel_ReturnsFirstPanelInRegion()
        {
            var panel1 = new TestPanelView("left1", "Left Panel 1", PanelRegion.Left);
            var panel2 = new TestPanelView("left2", "Left Panel 2", PanelRegion.Left);

            _sut.RegisterPanel(panel1);
            _sut.RegisterPanel(panel2);

            var result = _sut.GetDefaultPanel(PanelRegion.Left);
            Assert.AreSame(panel1, result);
        }

        [TestMethod]
        public void GetDefaultPanel_ReturnsNullForDifferentRegion()
        {
            var panel = new TestPanelView("left1", "Left Panel", PanelRegion.Left);
            _sut.RegisterPanel(panel);

            var result = _sut.GetDefaultPanel(PanelRegion.Right);
            Assert.IsNull(result);
        }

        #endregion

        #region Register (PanelDescriptor) Tests

        [TestMethod]
        public void Register_AddsDescriptor()
        {
            var descriptor = new PanelDescriptor
            {
                PanelId = "test1",
                DisplayName = "Test Panel",
                DefaultRegion = PanelRegion.Left
            };

            _sut.Register(descriptor);

            var descriptors = _sut.GetAllDescriptors().ToList();
            Assert.AreEqual(1, descriptors.Count);
            Assert.AreEqual("test1", descriptors[0].PanelId);
        }

        [TestMethod]
        public void Register_DoesNotAddDuplicatePanelId()
        {
            var descriptor1 = new PanelDescriptor
            {
                PanelId = "test1",
                DisplayName = "Test Panel 1",
                DefaultRegion = PanelRegion.Left
            };
            var descriptor2 = new PanelDescriptor
            {
                PanelId = "test1",
                DisplayName = "Test Panel 2",
                DefaultRegion = PanelRegion.Right
            };

            _sut.Register(descriptor1);
            _sut.Register(descriptor2);

            var descriptors = _sut.GetAllDescriptors().ToList();
            Assert.AreEqual(1, descriptors.Count);
            Assert.AreEqual("Test Panel 1", descriptors[0].DisplayName);
        }

        [TestMethod]
        public void Register_AllowsDifferentPanelIds()
        {
            var descriptor1 = new PanelDescriptor { PanelId = "test1" };
            var descriptor2 = new PanelDescriptor { PanelId = "test2" };

            _sut.Register(descriptor1);
            _sut.Register(descriptor2);

            var descriptors = _sut.GetAllDescriptors().ToList();
            Assert.AreEqual(2, descriptors.Count);
        }

        #endregion

        #region GetAllDescriptors Tests

        [TestMethod]
        public void GetAllDescriptors_ReturnsEmptyWhenNoDescriptors()
        {
            var descriptors = _sut.GetAllDescriptors().ToList();
            Assert.AreEqual(0, descriptors.Count);
        }

        [TestMethod]
        public void GetAllDescriptors_ReturnsAllRegisteredDescriptors()
        {
            _sut.Register(new PanelDescriptor { PanelId = "1" });
            _sut.Register(new PanelDescriptor { PanelId = "2" });
            _sut.Register(new PanelDescriptor { PanelId = "3" });

            var descriptors = _sut.GetAllDescriptors().ToList();
            Assert.AreEqual(3, descriptors.Count);
        }

        #endregion

        #region Test Helpers

        private class TestPanelView : IPanelView
        {
            public string PanelId { get; }
            public string DisplayName { get; }
            public PanelRegion Region { get; }

            public TestPanelView(string panelId, string displayName, PanelRegion region)
            {
                PanelId = panelId;
                DisplayName = displayName;
                Region = region;
            }
        }

        #endregion
    }
}
