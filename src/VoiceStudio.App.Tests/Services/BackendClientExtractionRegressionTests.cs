using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Regression tests to guard against IBackendClient/BackendClient re-acquiring extracted methods.
/// PR-9: macro methods must remain on IMacroClient only.
/// PR-10: workflow methods must remain on IWorkflowAutomationClient only.
/// PR-11: effect chain methods must remain on IEffectChainClient only.
/// PR-12: effect preset methods must remain on IEffectChainClient only.
/// PR-13: pipeline methods must remain on IPipelineConversationClient only.
/// PR-14: backup methods must remain on IBackupRestoreClient only.
/// PR-15: model methods must remain on IModelManagerClient only.
/// </summary>
[TestClass]
public class BackendClientExtractionRegressionTests
{
    private static readonly string[] MacroMethodNames =
    {
        "GetMacrosAsync", "GetMacroAsync", "CreateMacroAsync", "UpdateMacroAsync",
        "DeleteMacroAsync", "ExecuteMacroAsync", "GetMacroExecutionStatusAsync",
        "GetAutomationCurvesAsync", "CreateAutomationCurveAsync", "UpdateAutomationCurveAsync",
        "DeleteAutomationCurveAsync"
    };

    private static readonly string[] WorkflowMethodNames =
    {
        "GetWorkflowsAsync", "GetWorkflowAsync", "CreateWorkflowAsync", "UpdateWorkflowAsync",
        "DeleteWorkflowAsync", "ExecuteWorkflowAsync"
    };

    private static readonly string[] EffectChainMethodNames =
    {
        "GetEffectChainsAsync", "GetEffectChainAsync", "CreateEffectChainAsync", "UpdateEffectChainAsync",
        "DeleteEffectChainAsync", "ProcessAudioWithChainAsync", "GetEffectPresetsAsync"
    };

    private static readonly string[] EffectPresetMethodNames =
    {
        "CreateEffectPresetAsync", "DeleteEffectPresetAsync"
    };

    private static readonly string[] PipelineMethodNames =
    {
        "GetPipelineProvidersAsync", "ProcessPipelineAsync"
    };

    private static readonly string[] BackupMethodNames =
    {
        "GetBackupsAsync", "GetBackupAsync", "CreateBackupAsync", "DownloadBackupAsync",
        "RestoreBackupAsync", "UploadBackupAsync", "DeleteBackupAsync"
    };

    private static readonly string[] ModelMethodNames =
    {
        "GetModelsAsync", "GetModelAsync", "RegisterModelAsync", "VerifyModelAsync",
        "UpdateModelChecksumAsync", "DeleteModelAsync", "ExportModelAsync", "ImportModelAsync",
        "GetStorageStatsAsync"
    };

    [TestMethod]
    public void IBackendClient_DoesNotExposeMacroMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in MacroMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-9 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeMacroMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in MacroMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-9 extraction.");
        }
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposeWorkflowMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in WorkflowMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-10 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeWorkflowMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in WorkflowMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-10 extraction.");
        }
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposeEffectChainMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in EffectChainMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-11 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeEffectChainMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in EffectChainMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-11 extraction.");
        }
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposeEffectPresetMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in EffectPresetMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-12 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeEffectPresetMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in EffectPresetMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-12 extraction.");
        }
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposePipelineMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in PipelineMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-13 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposePipelineMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in PipelineMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-13 extraction.");
        }
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposeBackupMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in BackupMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-14 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeBackupMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in BackupMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-14 extraction.");
        }
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposeModelMethods()
    {
        var iface = typeof(IBackendClient);
        foreach (var name in ModelMethodNames)
        {
            var m = iface.GetMethod(name);
            Assert.IsNull(m, $"IBackendClient must not expose {name} after PR-15 extraction.");
        }
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeModelMethods()
    {
        var backendType = typeof(BackendClient);
        foreach (var name in ModelMethodNames)
        {
            var m = backendType.GetMethod(name);
            Assert.IsNull(m, $"BackendClient must not expose {name} after PR-15 extraction.");
        }
    }
}
