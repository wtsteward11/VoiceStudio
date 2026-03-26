using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Utilities
{
  [TestClass]
  public sealed class ScriptEditorSynthesisRequestBuilderTests
  {
    [TestMethod]
    public void Build_UsesDefaultEngineAndLanguage_WhenSegmentHasNoGenerationEngineAndNoMetadata()
    {
      var seg = new ScriptSegment { Id = "1", Text = "Hi", VoiceProfileId = "p1" };
      var req = ScriptEditorSynthesisRequestBuilder.Build(seg, null, "Hi", "p1");
      Assert.AreEqual("xtts", req.Engine);
      Assert.AreEqual("en", req.Language);
      Assert.AreEqual("Hi", req.Text);
      Assert.AreEqual("p1", req.ProfileId);
    }

    [TestMethod]
    public void Build_UsesGenerationEngineId_WhenSet()
    {
      var seg = new ScriptSegment
      {
        Id = "1",
        Text = "Hi",
        VoiceProfileId = "p1",
        GenerationEngineId = " piper "
      };
      var req = ScriptEditorSynthesisRequestBuilder.Build(seg, null, "Hi", "p1");
      Assert.AreEqual("piper", req.Engine);
    }

    [TestMethod]
    public void Build_PrefersSynthesisLanguageMetadataKey()
    {
      var seg = new ScriptSegment { Id = "1", Text = "Hi", VoiceProfileId = "p1" };
      var meta = new Dictionary<string, object>
      {
        ["language"] = "fr",
        ["synthesis_language"] = "de"
      };
      var req = ScriptEditorSynthesisRequestBuilder.Build(seg, meta, "Hi", "p1");
      Assert.AreEqual("de", req.Language);
    }

    [TestMethod]
    public void Build_FallsBackToLanguageMetadataKey()
    {
      var seg = new ScriptSegment { Id = "1", Text = "Hi", VoiceProfileId = "p1" };
      var meta = new Dictionary<string, object> { ["language"] = "fr" };
      var req = ScriptEditorSynthesisRequestBuilder.Build(seg, meta, "Hi", "p1");
      Assert.AreEqual("fr", req.Language);
    }
  }
}
