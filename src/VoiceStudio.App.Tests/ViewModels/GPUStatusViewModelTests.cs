using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Models;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for GPUStatus related model classes.
    /// Note: GPUStatusViewModel requires a WinUI DispatcherQueue that cannot be mocked in unit tests.
    /// These tests focus on the testable model classes used by the ViewModel.
    /// </summary>
    [TestClass]
    public class GPUStatusModelTests
    {
        #region GPUDeviceItem Model Tests

        [TestMethod]
        public void GPUDeviceItem_Constructor_SetsAllProperties()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "NVIDIA RTX 4090",
                Vendor = "NVIDIA",
                MemoryTotalMb = 24576, // 24 GB
                MemoryUsedMb = 8192,   // 8 GB
                MemoryFreeMb = 16384,  // 16 GB
                UtilizationPercent = 45.5,
                TemperatureCelsius = 65.3,
                PowerUsageWatts = 280.5,
                DriverVersion = "537.42",
                ComputeCapability = "8.9",
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("gpu-0", item.DeviceId);
            Assert.AreEqual("NVIDIA RTX 4090", item.Name);
            Assert.AreEqual("NVIDIA", item.Vendor);
            Assert.AreEqual(24576, item.MemoryTotalMb);
            Assert.AreEqual(8192, item.MemoryUsedMb);
            Assert.AreEqual(16384, item.MemoryFreeMb);
            Assert.AreEqual(45.5, item.UtilizationPercent);
            Assert.AreEqual(65.3, item.TemperatureCelsius);
            Assert.AreEqual(280.5, item.PowerUsageWatts);
            Assert.AreEqual("537.42", item.DriverVersion);
            Assert.AreEqual("8.9", item.ComputeCapability);
            Assert.IsTrue(item.IsAvailable);
        }

        [TestMethod]
        public void GPUDeviceItem_MemoryTotalDisplay_FormatsAsGB()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 24576, // 24 GB
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("24.0 GB", item.MemoryTotalDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_MemoryUsedDisplay_FormatsAsGB()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 24576,
                MemoryUsedMb = 8192, // 8 GB
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("8.0 GB", item.MemoryUsedDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_MemoryFreeDisplay_FormatsAsGB()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 24576,
                MemoryFreeMb = 16384, // 16 GB
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("16.0 GB", item.MemoryFreeDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_MemoryUsagePercent_CalculatesCorrectly()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 24576,
                MemoryUsedMb = 8192, // 8/24 = 33.33%
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            StringAssert.Contains(item.MemoryUsagePercent, "33.");
        }

        [TestMethod]
        public void GPUDeviceItem_UtilizationDisplay_FormatsAsPercentage()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                UtilizationPercent = 45.5,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("45.5%", item.UtilizationDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_TemperatureDisplay_FormatsWithCelsius()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                TemperatureCelsius = 65.3,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("65.3°C", item.TemperatureDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_TemperatureDisplay_WhenNull_ReturnsNA()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                TemperatureCelsius = null,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("N/A", item.TemperatureDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_PowerDisplay_FormatsWithWatts()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                PowerUsageWatts = 280.5,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("280.5W", item.PowerDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_PowerDisplay_WhenNull_ReturnsNA()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                PowerUsageWatts = null,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("N/A", item.PowerDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_StatusDisplay_WhenAvailable_ReturnsAvailable()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("Available", item.StatusDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_StatusDisplay_WhenUnavailable_ReturnsUnavailable()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 8192,
                IsAvailable = false
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual("Unavailable", item.StatusDisplay);
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void GPUDeviceItem_WithZeroMemory_HandlesGracefully()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "gpu-0",
                Name = "Test",
                Vendor = "Test",
                MemoryTotalMb = 0,
                MemoryUsedMb = 0,
                MemoryFreeMb = 0,
                IsAvailable = false
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert - Should not throw
            Assert.AreEqual("0.0 GB", item.MemoryTotalDisplay);
        }

        [TestMethod]
        public void GPUDeviceItem_WithEmptyStrings_HandlesGracefully()
        {
            // Arrange
            var device = new GPUStatusDevice
            {
                DeviceId = "",
                Name = "",
                Vendor = "",
                MemoryTotalMb = 8192,
                IsAvailable = true
            };

            // Act
            var item = new GPUDeviceItem(device);

            // Assert
            Assert.AreEqual(string.Empty, item.DeviceId);
            Assert.AreEqual(string.Empty, item.Name);
            Assert.AreEqual(string.Empty, item.Vendor);
        }

        #endregion
    }
}
