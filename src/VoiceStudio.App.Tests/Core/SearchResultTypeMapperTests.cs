using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Core;

[TestClass]
public sealed class SearchResultTypeMapperTests
{
    [TestMethod]
    public void ToPanelResultTypeString_KnownType_ReturnsCanonical()
    {
        Assert.AreEqual("profile", SearchResultTypeMapper.ToPanelResultTypeString("profile"));
        Assert.AreEqual("marker", SearchResultTypeMapper.ToPanelResultTypeString("MARKER"));
    }

    [TestMethod]
    public void ToPanelResultTypeString_UnknownType_PassesThroughLowercased()
    {
        Assert.AreEqual("future_type", SearchResultTypeMapper.ToPanelResultTypeString("Future_Type"));
    }
}
