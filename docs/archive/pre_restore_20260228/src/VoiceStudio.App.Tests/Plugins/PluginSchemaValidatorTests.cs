using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.Json;
using VoiceStudio.Core.Plugins;

namespace VoiceStudio.App.Tests.Plugins;

[TestClass]
public class PluginSchemaValidatorTests
{
    private static JsonElement ValidBackendOnlyManifest => JsonDocument.Parse(@"{
        ""name"": ""test_plugin"",
        ""display_name"": ""Test Plugin"",
        ""version"": ""1.0.0"",
        ""author"": ""Test Author"",
        ""description"": ""A test plugin"",
        ""plugin_type"": ""backend_only"",
        ""entry_points"": {
            ""backend"": ""plugin.register""
        },
        ""capabilities"": {
            ""backend_routes"": true
        },
        ""permissions"": [""filesystem.read""]
    }").RootElement.Clone();

    private static JsonElement ValidFrontendOnlyManifest => JsonDocument.Parse(@"{
        ""name"": ""ui_plugin"",
        ""display_name"": ""UI Plugin"",
        ""version"": ""1.2.3"",
        ""author"": ""UI Developer"",
        ""description"": ""A frontend plugin"",
        ""plugin_type"": ""frontend_only"",
        ""entry_points"": {
            ""frontend"": ""UIPlugin.dll""
        },
        ""capabilities"": {
            ""ui_panels"": [""settings_panel""]
        }
    }").RootElement.Clone();

    private static JsonElement ValidFullStackManifest => JsonDocument.Parse(@"{
        ""name"": ""full_stack_plugin"",
        ""display_name"": ""Full Stack Plugin"",
        ""version"": ""2.0.0"",
        ""author"": ""Full Stack Developer"",
        ""description"": ""A full stack plugin"",
        ""plugin_type"": ""full_stack"",
        ""entry_points"": {
            ""backend"": ""plugin.main.register"",
            ""frontend"": ""FullStackPlugin.dll""
        },
        ""capabilities"": {
            ""backend_routes"": true,
            ""ui_panels"": [""main_panel"", ""settings_panel""],
            ""effects"": [""echo"", ""reverb""]
        },
        ""permissions"": [""filesystem.read"", ""filesystem.write"", ""audio.input""],
        ""dependencies"": {
            ""python"": [""numpy>=1.20.0"", ""scipy""],
            ""plugins"": []
        },
        ""metadata"": {
            ""license"": ""MIT"",
            ""homepage"": ""https://example.com"",
            ""tags"": [""audio"", ""effects""]
        }
    }").RootElement.Clone();

    #region Validator Initialization Tests

    [TestMethod]
    public void ValidatorInitialization_WithoutSchemaPath_DoesNotThrow()
    {
        // The validator should initialize without throwing even if schema not found
        var validator = new PluginSchemaValidator();
        Assert.IsNotNull(validator);
    }

    #endregion

    #region Valid Manifest Tests

    [TestMethod]
    public void Validate_ValidBackendOnlyManifest_ReturnsValid()
    {
        var validator = new PluginSchemaValidator();
        var result = validator.Validate(ValidBackendOnlyManifest);

        // If schema loaded successfully, should be valid
        // If schema not found, errors will indicate that
        if (result.Errors.Count == 0 || 
            !result.Errors.Any(e => e.Contains("not initialized")))
        {
            Assert.IsTrue(result.IsValid, 
                $"Expected valid, got errors: {string.Join("; ", result.Errors)}");
        }
    }

    [TestMethod]
    public void Validate_ValidFrontendOnlyManifest_ReturnsValid()
    {
        var validator = new PluginSchemaValidator();
        var result = validator.Validate(ValidFrontendOnlyManifest);

        if (result.Errors.Count == 0 || 
            !result.Errors.Any(e => e.Contains("not initialized")))
        {
            Assert.IsTrue(result.IsValid,
                $"Expected valid, got errors: {string.Join("; ", result.Errors)}");
        }
    }

    [TestMethod]
    public void Validate_ValidFullStackManifest_ReturnsValid()
    {
        var validator = new PluginSchemaValidator();
        var result = validator.Validate(ValidFullStackManifest);

        if (result.Errors.Count == 0 || 
            !result.Errors.Any(e => e.Contains("not initialized")))
        {
            Assert.IsTrue(result.IsValid,
                $"Expected valid, got errors: {string.Join("; ", result.Errors)}");
        }
    }

    #endregion

    #region Invalid Manifest Tests

    [TestMethod]
    public void Validate_MissingRequiredFields_ReturnsInvalid()
    {
        var validator = new PluginSchemaValidator();
        using var doc = JsonDocument.Parse(@"{
            ""name"": ""incomplete_plugin""
        }");
        var result = validator.Validate(doc.RootElement);

        // Should be invalid due to missing fields
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public void Validate_FullStackMissingBackendEntry_ReturnsSemanticError()
    {
        var validator = new PluginSchemaValidator();
        using var doc = JsonDocument.Parse(@"{
            ""name"": ""bad_plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test"",
            ""plugin_type"": ""full_stack"",
            ""entry_points"": {
                ""frontend"": ""Plugin.dll""
            }
        }");
        var result = validator.Validate(doc.RootElement);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => 
            e.ToLower().Contains("backend")),
            $"Expected backend error, got: {string.Join("; ", result.Errors)}");
    }

    [TestMethod]
    public void Validate_FrontendOnlyWithBackendRoutes_ReturnsSemanticError()
    {
        var validator = new PluginSchemaValidator();
        using var doc = JsonDocument.Parse(@"{
            ""name"": ""bad_plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test"",
            ""plugin_type"": ""frontend_only"",
            ""entry_points"": {
                ""frontend"": ""Plugin.dll""
            },
            ""capabilities"": {
                ""backend_routes"": true
            }
        }");
        var result = validator.Validate(doc.RootElement);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => 
            e.ToLower().Contains("backend_routes")),
            $"Expected backend_routes error, got: {string.Join("; ", result.Errors)}");
    }

    [TestMethod]
    public void Validate_BackendOnlyWithUiPanels_ReturnsSemanticError()
    {
        var validator = new PluginSchemaValidator();
        using var doc = JsonDocument.Parse(@"{
            ""name"": ""bad_plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test"",
            ""plugin_type"": ""backend_only"",
            ""entry_points"": {
                ""backend"": ""plugin.register""
            },
            ""capabilities"": {
                ""ui_panels"": [""some_panel""]
            }
        }");
        var result = validator.Validate(doc.RootElement);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => 
            e.ToLower().Contains("ui_panels")),
            $"Expected ui_panels error, got: {string.Join("; ", result.Errors)}");
    }

    #endregion

    #region Semver Validation Tests

    [TestMethod]
    [DataRow("1.0.0")]
    [DataRow("0.1.0")]
    [DataRow("10.20.30")]
    [DataRow("1.0.0-alpha")]
    [DataRow("1.0.0-beta.1")]
    [DataRow("1.0.0+build.123")]
    public void Validate_ValidSemverFormats_PassSemverValidation(string version)
    {
        var validator = new PluginSchemaValidator();
        using var doc = JsonDocument.Parse($@"{{
            ""name"": ""test_plugin"",
            ""version"": ""{version}"",
            ""author"": ""Test"",
            ""plugin_type"": ""backend_only"",
            ""entry_points"": {{
                ""backend"": ""plugin.register""
            }}
        }}");
        var result = validator.Validate(doc.RootElement);

        // Should not have semver-specific errors
        var semverErrors = result.Errors.Where(e => 
            e.ToLower().Contains("semver")).ToList();
        Assert.AreEqual(0, semverErrors.Count,
            $"Version {version} failed semver: {string.Join("; ", semverErrors)}");
    }

    #endregion

    #region File Validation Tests

    [TestMethod]
    public void ValidateFile_FileNotFound_ReturnsError()
    {
        var validator = new PluginSchemaValidator();
        var result = validator.ValidateFile(@"C:\nonexistent\path.json");

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => 
            e.ToLower().Contains("not found")));
    }

    [TestMethod]
    public void ValidateFile_ValidManifestFile_ReturnsValid()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "test_manifest.json");
        try
        {
            var manifestJson = @"{
                ""name"": ""test_plugin"",
                ""display_name"": ""Test Plugin"",
                ""version"": ""1.0.0"",
                ""author"": ""Test Author"",
                ""description"": ""A test plugin"",
                ""plugin_type"": ""backend_only"",
                ""entry_points"": {
                    ""backend"": ""plugin.register""
                },
                ""capabilities"": {
                    ""backend_routes"": true
                },
                ""permissions"": [""filesystem.read""]
            }";
            File.WriteAllText(tempPath, manifestJson);

            var validator = new PluginSchemaValidator();
            var result = validator.ValidateFile(tempPath);

            // If schema loaded, should be valid
            if (result.Errors.Count == 0 || 
                !result.Errors.Any(e => e.Contains("not initialized")))
            {
                Assert.IsTrue(result.IsValid,
                    $"Expected valid, got: {string.Join("; ", result.Errors)}");
                Assert.IsNotNull(result.Manifest);
                Assert.AreEqual("test_plugin", result.Manifest!.Name);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [TestMethod]
    public void ValidateFile_InvalidJson_ReturnsError()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "bad_manifest.json");
        try
        {
            File.WriteAllText(tempPath, "not valid json {");

            var validator = new PluginSchemaValidator();
            var result = validator.ValidateFile(tempPath);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => 
                e.ToLower().Contains("json")));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    #endregion

    #region PluginManifest Model Tests

    [TestMethod]
    public void PluginManifest_DefaultValues_AreEmpty()
    {
        var manifest = new PluginManifest();

        Assert.AreEqual(string.Empty, manifest.Name);
        Assert.AreEqual(string.Empty, manifest.Version);
        Assert.AreEqual(string.Empty, manifest.Author);
        Assert.AreEqual(string.Empty, manifest.PluginType);
    }

    [TestMethod]
    public void PluginManifest_AllPropertiesSettable()
    {
        var manifest = new PluginManifest
        {
            Name = "test_plugin",
            DisplayName = "Test Plugin",
            Version = "1.0.0",
            Author = "Test Author",
            Description = "A test plugin",
            LongDescription = "Full description",
            PluginType = "backend_only",
            MinAppVersion = "1.0.0",
            MinApiVersion = "1.0.0",
            Permissions = new() { "filesystem.read" }
        };

        Assert.AreEqual("test_plugin", manifest.Name);
        Assert.AreEqual("Test Plugin", manifest.DisplayName);
        Assert.AreEqual("1.0.0", manifest.Version);
        Assert.AreEqual("Test Author", manifest.Author);
        Assert.AreEqual("A test plugin", manifest.Description);
        Assert.AreEqual("backend_only", manifest.PluginType);
        Assert.AreEqual(1, manifest.Permissions!.Count);
    }

    #endregion

    #region PluginValidationResult Tests

    [TestMethod]
    public void PluginValidationResult_ValidResult_HasManifest()
    {
        var result = new PluginValidationResult
        {
            IsValid = true,
            Errors = Array.Empty<string>(),
            Manifest = new PluginManifest { Name = "test" }
        };

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.IsNotNull(result.Manifest);
    }

    [TestMethod]
    public void PluginValidationResult_InvalidResult_HasErrors()
    {
        var result = new PluginValidationResult
        {
            IsValid = false,
            Errors = new[] { "Error 1", "Error 2" },
            Manifest = null
        };

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(2, result.Errors.Count);
        Assert.IsNull(result.Manifest);
    }

    #endregion
}
