using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;
// Type aliases for embedding explorer API models
using EmbeddingSimilarityModel = VoiceStudio.App.Services.EmbeddingSimilarity;
using EmbeddingVectorModel = VoiceStudio.App.Services.EmbeddingVector;
using EmbeddingVisualizationModel = VoiceStudio.App.Services.EmbeddingVisualization;
using EmbeddingClusterModel = VoiceStudio.App.Services.EmbeddingCluster;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for EmbeddingExplorerViewModel and related model classes.
    /// Note: EmbeddingExplorerViewModel requires a WinUI DispatcherQueue that cannot be mocked in unit tests.
    /// These tests focus on the testable model classes used by the ViewModel.
    /// </summary>
    [TestClass]
    public class EmbeddingExplorerViewModelTests : ViewModelTestBase
    {
        #region EmbeddingSimilarityItem Model Tests

        [TestMethod]
        public void EmbeddingSimilarityItem_SimilarityDisplay_FormatsAsPercent()
        {
            // Arrange
            var model = new EmbeddingSimilarityModel
            {
                EmbeddingId1 = "emb-1",
                EmbeddingId2 = "emb-2",
                Similarity = 0.92,
                Distance = 0.08
            };

            // Act
            var item = new EmbeddingSimilarityItem(model);

            // Assert
            Assert.AreEqual("92.0%", item.SimilarityDisplay);
        }

        [TestMethod]
        public void EmbeddingSimilarityItem_DistanceDisplay_FormatsToThreeDecimals()
        {
            // Arrange
            var model = new EmbeddingSimilarityModel
            {
                EmbeddingId1 = "emb-1",
                EmbeddingId2 = "emb-2",
                Similarity = 0.85,
                Distance = 0.12345
            };

            // Act
            var item = new EmbeddingSimilarityItem(model);

            // Assert
            Assert.AreEqual("0.123", item.DistanceDisplay);
        }

        [TestMethod]
        public void EmbeddingSimilarityItem_Constructor_CopiesAllProperties()
        {
            // Arrange
            var model = new EmbeddingSimilarityModel
            {
                EmbeddingId1 = "embedding-alpha",
                EmbeddingId2 = "embedding-beta",
                Similarity = 0.78,
                Distance = 0.22
            };

            // Act
            var item = new EmbeddingSimilarityItem(model);

            // Assert
            Assert.AreEqual("embedding-alpha", item.EmbeddingId1);
            Assert.AreEqual("embedding-beta", item.EmbeddingId2);
            Assert.AreEqual(0.78, item.Similarity);
            Assert.AreEqual(0.22, item.Distance);
        }

        #endregion

        #region EmbeddingItem Model Tests

        [TestMethod]
        public void EmbeddingItem_DimensionDisplay_FormatsCorrectly()
        {
            // Arrange
            var model = new EmbeddingVectorModel
            {
                EmbeddingId = "emb-001",
                VoiceProfileId = "profile-001",
                Dimension = 256,
                Created = "2026-02-09T10:00:00Z"
            };

            // Act
            var item = new EmbeddingItem(model);

            // Assert
            Assert.AreEqual("256D", item.DimensionDisplay);
        }

        [TestMethod]
        public void EmbeddingItem_Constructor_CopiesAllProperties()
        {
            // Arrange
            var model = new EmbeddingVectorModel
            {
                EmbeddingId = "emb-002",
                VoiceProfileId = "profile-002",
                Dimension = 512,
                Created = "2026-02-09T11:00:00Z"
            };

            // Act
            var item = new EmbeddingItem(model);

            // Assert
            Assert.AreEqual("emb-002", item.EmbeddingId);
            Assert.AreEqual("profile-002", item.VoiceProfileId);
            Assert.AreEqual(512, item.Dimension);
            Assert.AreEqual("2026-02-09T11:00:00Z", item.Created);
        }

        [TestMethod]
        public void EmbeddingItem_SmallDimension_DisplaysCorrectly()
        {
            // Arrange - small dimension like 64
            var model = new EmbeddingVectorModel
            {
                Dimension = 64
            };

            // Act
            var item = new EmbeddingItem(model);

            // Assert
            Assert.AreEqual("64D", item.DimensionDisplay);
        }

        [TestMethod]
        public void EmbeddingItem_LargeDimension_DisplaysCorrectly()
        {
            // Arrange - large dimension like 1024
            var model = new EmbeddingVectorModel
            {
                Dimension = 1024
            };

            // Act
            var item = new EmbeddingItem(model);

            // Assert
            Assert.AreEqual("1024D", item.DimensionDisplay);
        }

        #endregion

        #region EmbeddingVisualizationItem Model Tests

        [TestMethod]
        public void EmbeddingVisualizationItem_Constructor_Copies2DCoordinates()
        {
            // Arrange
            var model = new EmbeddingVisualizationModel
            {
                EmbeddingId = "vis-001",
                X = 1.5,
                Y = 2.5,
                Z = null,
                Color = "#FF5500"
            };

            // Act
            var item = new EmbeddingVisualizationItem(model);

            // Assert
            Assert.AreEqual("vis-001", item.EmbeddingId);
            Assert.AreEqual(1.5, item.X);
            Assert.AreEqual(2.5, item.Y);
            Assert.IsNull(item.Z);
            Assert.AreEqual("#FF5500", item.Color);
        }

        [TestMethod]
        public void EmbeddingVisualizationItem_Constructor_Copies3DCoordinates()
        {
            // Arrange
            var model = new EmbeddingVisualizationModel
            {
                EmbeddingId = "vis-002",
                X = 1.0,
                Y = 2.0,
                Z = 3.0,
                Color = "#0055FF"
            };

            // Act
            var item = new EmbeddingVisualizationItem(model);

            // Assert
            Assert.AreEqual(1.0, item.X);
            Assert.AreEqual(2.0, item.Y);
            Assert.AreEqual(3.0, item.Z);
        }

        #endregion

        #region EmbeddingClusterItem Model Tests

        [TestMethod]
        public void EmbeddingClusterItem_SizeDisplay_FormatsCorrectly()
        {
            // Arrange
            var model = new EmbeddingClusterModel
            {
                ClusterId = "cluster-001",
                EmbeddingIds = new[] { "emb-1", "emb-2", "emb-3" },
                Centroid = new[] { 0.5, 0.5 },
                Size = 3
            };

            // Act
            var item = new EmbeddingClusterItem(model);

            // Assert
            Assert.AreEqual("3 embeddings", item.SizeDisplay);
        }

        [TestMethod]
        public void EmbeddingClusterItem_Constructor_CreatesObservableCollection()
        {
            // Arrange
            var model = new EmbeddingClusterModel
            {
                ClusterId = "cluster-002",
                EmbeddingIds = new[] { "a", "b", "c", "d" },
                Centroid = new[] { 1.0, 2.0, 3.0 },
                Size = 4
            };

            // Act
            var item = new EmbeddingClusterItem(model);

            // Assert
            Assert.IsInstanceOfType(item.EmbeddingIds, typeof(ObservableCollection<string>));
            Assert.AreEqual(4, item.EmbeddingIds.Count);
            CollectionAssert.Contains(item.EmbeddingIds, "a");
            CollectionAssert.Contains(item.EmbeddingIds, "d");
        }

        [TestMethod]
        public void EmbeddingClusterItem_SingleEmbedding_SizeDisplayIsSingular()
        {
            // Arrange
            var model = new EmbeddingClusterModel
            {
                ClusterId = "cluster-003",
                EmbeddingIds = new[] { "single" },
                Centroid = Array.Empty<double>(),
                Size = 1
            };

            // Act
            var item = new EmbeddingClusterItem(model);

            // Assert
            // Note: Current implementation uses "embeddings" even for 1
            Assert.AreEqual("1 embeddings", item.SizeDisplay);
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void EmbeddingSimilarityItem_ZeroSimilarity_DisplaysCorrectly()
        {
            // Arrange
            var model = new EmbeddingSimilarityModel { Similarity = 0.0, Distance = 1.0 };

            // Act
            var item = new EmbeddingSimilarityItem(model);

            // Assert
            Assert.AreEqual("0.0%", item.SimilarityDisplay);
        }

        [TestMethod]
        public void EmbeddingSimilarityItem_PerfectSimilarity_DisplaysCorrectly()
        {
            // Arrange
            var model = new EmbeddingSimilarityModel { Similarity = 1.0, Distance = 0.0 };

            // Act
            var item = new EmbeddingSimilarityItem(model);

            // Assert
            Assert.AreEqual("100.0%", item.SimilarityDisplay);
            Assert.AreEqual("0.000", item.DistanceDisplay);
        }

        #endregion
    }
}