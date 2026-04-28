using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public class BackendClientHttpPipelineTests
  {
    private static BackendClientHttpPipeline CreatePipeline()
    {
      return new BackendClientHttpPipeline(
          new HttpClient(),
          JsonSerializerOptionsFactory.BackendApi,
          circuitStateProvider: null);
    }

    private static HttpResponseMessage ForbiddenJson(string jsonBody)
    {
      return new HttpResponseMessage(HttpStatusCode.Forbidden)
      {
        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
      };
    }

    [TestMethod]
    public async Task CreateExceptionFromResponseAsync_403_ConsentRequiredCode_YieldsConsentRequiredException()
    {
      var pipeline = CreatePipeline();
      using var response = ForbiddenJson(
          """{"message":"Need consent","error_code":"CONSENT_REQUIRED"}""");

      var ex = await pipeline.CreateExceptionFromResponseAsync(response);

      Assert.IsInstanceOfType(ex, typeof(ConsentRequiredException));
      Assert.AreEqual("CONSENT_REQUIRED", ex.ErrorCode);
    }

    [TestMethod]
    public async Task CreateExceptionFromResponseAsync_403_ConsentRequiredCode_IgnoresCase()
    {
      var pipeline = CreatePipeline();
      using var response = ForbiddenJson(
          """{"message":"Need consent","error_code":"consent_required"}""");

      var ex = await pipeline.CreateExceptionFromResponseAsync(response);

      Assert.IsInstanceOfType(ex, typeof(ConsentRequiredException));
    }

    [TestMethod]
    public async Task CreateExceptionFromResponseAsync_403_AuthorizationFailed_YieldsBackendExceptionNotConsent()
    {
      var pipeline = CreatePipeline();
      using var response = ForbiddenJson(
          """{"message":"Denied","error_code":"AUTHORIZATION_FAILED"}""");

      var ex = await pipeline.CreateExceptionFromResponseAsync(response);

      Assert.IsNotInstanceOfType(ex, typeof(ConsentRequiredException));
      Assert.IsInstanceOfType(ex, typeof(BackendException));
      Assert.AreEqual("AUTHORIZATION_FAILED", ex.ErrorCode);
      Assert.AreEqual(403, ex.StatusCode);
    }

    [TestMethod]
    public async Task CreateExceptionFromResponseAsync_403_NoErrorCode_YieldsBackendExceptionNotConsent()
    {
      var pipeline = CreatePipeline();
      using var response = ForbiddenJson("""{"message":"Forbidden"}""");

      var ex = await pipeline.CreateExceptionFromResponseAsync(response);

      Assert.IsNotInstanceOfType(ex, typeof(ConsentRequiredException));
      Assert.IsInstanceOfType(ex, typeof(BackendException));
      Assert.IsNull(ex.ErrorCode);
    }
  }
}
