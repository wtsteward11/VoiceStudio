using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Utilities
{
  [TestClass]
  public class ActionableErrorTranslatorTests
  {
    [TestMethod]
    public void Translate_BackendValidationException_MapsToValidationInput()
    {
      var ex = new BackendValidationException("Bad SSML");
      var info = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.VoiceSynthesize);
      Assert.AreEqual(ActionableErrorClass.ValidationInput, info.Class);
      Assert.AreEqual("Bad SSML", info.PrimaryMessage);
      Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void Translate_BackendNotFound_VoiceSynthesize_UsesContextPrimary()
    {
      var ex = new BackendNotFoundException("missing");
      var info = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.VoiceSynthesize);
      Assert.AreEqual(ActionableErrorClass.ValidationInput, info.Class);
      StringAssert.Contains(info.PrimaryMessage, "missing");
    }

    [TestMethod]
    public void Translate_BackendUnavailable_IsRetryable()
    {
      var ex = new BackendUnavailableException("down");
      var info = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General);
      Assert.AreEqual(ActionableErrorClass.EnvironmentUnavailable, info.Class);
      Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Translate_HttpRequestException_UnknownStatusCode_NoRawCodeInPrimary()
    {
      var ex = new HttpRequestException("socket explosion details");
      ex.Data["StatusCode"] = "418";
      var info = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General);
      Assert.IsFalse(info.PrimaryMessage.Contains("418", StringComparison.Ordinal));
      Assert.IsFalse(info.PrimaryMessage.Contains("socket", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Translate_ServerException502_IsTransientClass()
    {
      var ex = new BackendServerException("bad gateway", 502);
      var info = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.VoiceSynthesize);
      Assert.AreEqual(ActionableErrorClass.TransientRetryable, info.Class);
      Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void BuildSsmlHandlingUserNotice_StrippedWarned_ReturnsWarning()
    {
      var h = new SsmlHandlingDiagnostics
      {
        Action = "stripped_warned",
        Warnings = new List<string> { "Removed <break>" }
      };
      var info = ActionableErrorTranslator.BuildSsmlHandlingUserNotice(h);
      Assert.IsNotNull(info);
      Assert.AreEqual(ActionableErrorSeverity.Warning, info.Severity);
      Assert.IsTrue(info.PrimaryMessage.Length > 10);
      StringAssert.Contains(info.SecondaryDetail ?? "", "Removed");
    }

    [TestMethod]
    public void BuildSsmlHandlingUserNotice_Preserved_ReturnsNull()
    {
      var h = new SsmlHandlingDiagnostics { Action = "preserved" };
      Assert.IsNull(ActionableErrorTranslator.BuildSsmlHandlingUserNotice(h));
    }
  }
}
