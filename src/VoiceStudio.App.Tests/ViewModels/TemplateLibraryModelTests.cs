using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class TemplateLibraryModelTests
    {
        #region Template Model Tests

        [TestMethod]
        public void Template_DefaultValues()
        {
            var template = new TemplateLibraryTemplate();

            Assert.AreEqual(string.Empty, template.Id);
            Assert.AreEqual(string.Empty, template.Name);
            Assert.AreEqual(string.Empty, template.Category);
            Assert.IsNull(template.Description);
            Assert.IsNull(template.ThumbnailUrl);
            Assert.IsNotNull(template.ProjectData);
            Assert.AreEqual(0, template.ProjectData.Count);
            Assert.IsNotNull(template.Tags);
            Assert.AreEqual(0, template.Tags.Count);
            Assert.IsNull(template.Author);
            Assert.AreEqual("1.0", template.Version);
            Assert.IsFalse(template.IsPublic);
            Assert.AreEqual(0, template.UsageCount);
            Assert.AreEqual(string.Empty, template.Created);
            Assert.AreEqual(string.Empty, template.Modified);
        }

        [TestMethod]
        public void Template_PropertiesSetCorrectly()
        {
            var projectData = new Dictionary<string, object> { { "key1", "value1" } };
            var tags = new List<string> { "tag1", "tag2" };

            var template = new TemplateLibraryTemplate
            {
                Id = "template123",
                Name = "My Template",
                Category = "Voice Cloning",
                Description = "A test template",
                ThumbnailUrl = "https://example.com/thumb.jpg",
                ProjectData = projectData,
                Tags = tags,
                Author = "Test Author",
                Version = "2.0",
                IsPublic = true,
                UsageCount = 42,
                Created = "2026-01-01",
                Modified = "2026-01-15"
            };

            Assert.AreEqual("template123", template.Id);
            Assert.AreEqual("My Template", template.Name);
            Assert.AreEqual("Voice Cloning", template.Category);
            Assert.AreEqual("A test template", template.Description);
            Assert.AreEqual("https://example.com/thumb.jpg", template.ThumbnailUrl);
            Assert.AreSame(projectData, template.ProjectData);
            Assert.AreSame(tags, template.Tags);
            Assert.AreEqual("Test Author", template.Author);
            Assert.AreEqual("2.0", template.Version);
            Assert.IsTrue(template.IsPublic);
            Assert.AreEqual(42, template.UsageCount);
            Assert.AreEqual("2026-01-01", template.Created);
            Assert.AreEqual("2026-01-15", template.Modified);
        }

        #endregion

        #region TemplateItem Model Tests

        [TestMethod]
        public void TemplateItem_CreatedFromTemplate()
        {
            var template = new TemplateLibraryTemplate
            {
                Id = "t1",
                Name = "Test Template",
                Category = "Audio",
                Description = "Test description",
                ThumbnailUrl = "http://test.com/img.png",
                Tags = new List<string> { "music", "voice" },
                Author = "John Doe",
                IsPublic = true,
                UsageCount = 100
            };

            var item = new TemplateItem(template);

            Assert.AreEqual("t1", item.Id);
            Assert.AreEqual("Test Template", item.Name);
            Assert.AreEqual("Audio", item.Category);
            Assert.AreEqual("Test description", item.Description);
            Assert.AreEqual("http://test.com/img.png", item.ThumbnailUrl);
            Assert.AreEqual(2, item.Tags.Count);
            Assert.AreEqual("John Doe", item.Author);
            Assert.IsTrue(item.IsPublic);
            Assert.AreEqual(100, item.UsageCount);
        }

        [TestMethod]
        public void TemplateItem_UpdateFrom_UpdatesProperties()
        {
            var original = new TemplateLibraryTemplate
            {
                Id = "t1",
                Name = "Original Name",
                Category = "Cat1",
                Description = "Original Desc",
                Tags = new List<string> { "old" },
                IsPublic = false
            };
            var item = new TemplateItem(original);

            var updated = new TemplateLibraryTemplate
            {
                Name = "Updated Name",
                Category = "Cat2",
                Description = "Updated Desc",
                Tags = new List<string> { "new1", "new2" },
                IsPublic = true
            };
            item.UpdateFrom(updated);

            Assert.AreEqual("Updated Name", item.Name);
            Assert.AreEqual("Cat2", item.Category);
            Assert.AreEqual("Updated Desc", item.Description);
            Assert.AreEqual(2, item.Tags.Count);
            Assert.IsTrue(item.IsPublic);
            // Id should not change
            Assert.AreEqual("t1", item.Id);
        }

        [TestMethod]
        public void TemplateItem_NullablePropertiesAllowNull()
        {
            var template = new TemplateLibraryTemplate
            {
                Id = "t1",
                Name = "Name",
                Category = "Cat",
                Description = null,
                ThumbnailUrl = null,
                Author = null
            };

            var item = new TemplateItem(template);

            Assert.IsNull(item.Description);
            Assert.IsNull(item.ThumbnailUrl);
            Assert.IsNull(item.Author);
        }

        #endregion
    }
}
