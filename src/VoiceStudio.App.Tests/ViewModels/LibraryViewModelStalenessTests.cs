using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Pure logic tests for LibraryViewModel staleness guard.
  /// Tests HasFilterStateChanged directly without ViewModel instantiation, mocks, or threading.
  /// Uses reflection (InternalsVisibleTo direct call failed at compile in this environment).
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class LibraryViewModelStalenessTests
  {
    private static bool InvokeHasFilterStateChanged(string? qStart, string? fStart, string? tStart, string? qCur, string? fCur, string? tCur)
    {
      var method = typeof(LibraryViewModel).GetMethod("HasFilterStateChanged", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
          ?? typeof(LibraryViewModel).GetMethod("HasFilterStateChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
      Assert.IsNotNull(method, "HasFilterStateChanged method must exist");
      var result = method.Invoke(null, new object?[] { qStart, fStart, tStart, qCur, fCur, tCur });
      return (bool)result!;
    }

    [TestMethod]
    public void HasFilterStateChanged_ReturnsTrue_WhenQueryChanged()
    {
      var result = InvokeHasFilterStateChanged("initial", "folder-a", "audio", "changed", "folder-a", "audio");
      Assert.IsTrue(result, "Filter state changed when query changed");
    }

    [TestMethod]
    public void HasFilterStateChanged_ReturnsTrue_WhenFolderChanged()
    {
      var result = InvokeHasFilterStateChanged("q", "folder-a", "audio", "q", "folder-b", "audio");
      Assert.IsTrue(result, "Filter state changed when folder changed");
    }

    [TestMethod]
    public void HasFilterStateChanged_ReturnsTrue_WhenAssetTypeChanged()
    {
      var result = InvokeHasFilterStateChanged("q", "folder-a", "audio", "q", "folder-a", "voice");
      Assert.IsTrue(result, "Filter state changed when asset type changed");
    }

    [TestMethod]
    public void HasFilterStateChanged_ReturnsFalse_WhenNoChange()
    {
      var result = InvokeHasFilterStateChanged("q", "folder-a", "audio", "q", "folder-a", "audio");
      Assert.IsFalse(result, "Filter state unchanged when all values match");
    }

    [TestMethod]
    public void HasFilterStateChanged_ReturnsFalse_WhenAllNull()
    {
      var result = InvokeHasFilterStateChanged(null, null, null, null, null, null);
      Assert.IsFalse(result, "Filter state unchanged when all null");
    }

    [TestMethod]
    public void HasFilterStateChanged_ReturnsTrue_WhenAnySingleValueDiffers()
    {
      Assert.IsTrue(InvokeHasFilterStateChanged("a", null, null, "b", null, null));
      Assert.IsTrue(InvokeHasFilterStateChanged(null, "f1", null, null, "f2", null));
      Assert.IsTrue(InvokeHasFilterStateChanged(null, null, "t1", null, null, "t2"));
    }
  }
}
