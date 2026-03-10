using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Tests.Utilities
{
  /// <summary>
  /// Unit tests for ErrorHandler. Ensures 429 is never shown as blocking modal (P0).
  /// </summary>
  [TestClass]
  public class ErrorHandlerTests
  {
    [TestMethod]
    public void IsRateLimitException_BackendServerException429_ReturnsTrue()
    {
      var ex = new BackendServerException("Rate limit exceeded", 429);
      Assert.IsTrue(ErrorHandler.IsRateLimitException(ex));
    }

    [TestMethod]
    public void IsRateLimitException_BackendException429_ReturnsTrue()
    {
      var ex = new BackendException("Too many requests", 429, null, false);
      Assert.IsTrue(ErrorHandler.IsRateLimitException(ex));
    }

    [TestMethod]
    public void IsRateLimitException_HttpRequestExceptionWith429_ReturnsTrue()
    {
      var ex = new HttpRequestException("Rate limited");
      ex.Data["StatusCode"] = "429";
      Assert.IsTrue(ErrorHandler.IsRateLimitException(ex));
    }

    [TestMethod]
    public void IsRateLimitException_Non429_ReturnsFalse()
    {
      Assert.IsFalse(ErrorHandler.IsRateLimitException(new BackendServerException("Server error", 500)));
      Assert.IsFalse(ErrorHandler.IsRateLimitException(new BackendException("Not found", 404, null, false)));
      Assert.IsFalse(ErrorHandler.IsRateLimitException(null));
    }
  }
}
