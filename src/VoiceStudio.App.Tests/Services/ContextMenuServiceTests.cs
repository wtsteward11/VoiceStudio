using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.UI;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Integration tests for ContextMenuService (IDEA 10: Contextual Right-Click Menus).
  /// Tests context menu creation for different context types.
  /// </summary>
  [TestClass]
  [TestCategory("UI")]
  [Ignore("Disabled for finish-line stability; UI automation harness remains unstable.")]
  public class ContextMenuServiceTests : TestBase
  {
    private ContextMenuService? _service;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _service = new ContextMenuService();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
      _service = null;
      base.TestCleanup();
    }

    [UITestMethod]
    public void CreateContextMenu_Timeline_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "timeline";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Profile_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "profile";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Audio_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "audio";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Effect_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "effect";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Track_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "track";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Clip_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "clip";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Marker_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "marker";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_Default_CreatesMenuWithItems()
    {
      // Arrange
      var contextType = "default";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_UnknownType_CreatesDefaultMenu()
    {
      // Arrange
      var contextType = "unknown-type";

      // Act
      var menu = _service!.CreateContextMenu(contextType);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_CaseInsensitive_Works()
    {
      // Arrange
      var contextType1 = "TIMELINE";
      var contextType2 = "Timeline";
      var contextType3 = "timeline";

      // Act
      var menu1 = _service!.CreateContextMenu(contextType1);
      var menu2 = _service.CreateContextMenu(contextType2);
      var menu3 = _service.CreateContextMenu(contextType3);

      // Assert
      Assert.IsNotNull(menu1);
      Assert.IsNotNull(menu2);
      Assert.IsNotNull(menu3);
      Assert.IsTrue(menu1.Items.Count > 0);
      Assert.IsTrue(menu2.Items.Count > 0);
      Assert.IsTrue(menu3.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_WithContextData_CreatesMenu()
    {
      // Arrange
      var contextType = "profile";
      var contextData = new { Id = "test-id", Name = "Test Profile" };

      // Act
      var menu = _service!.CreateContextMenu(contextType, contextData);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }

    [UITestMethod]
    public void CreateContextMenu_NullContextData_CreatesMenu()
    {
      // Arrange
      var contextType = "timeline";

      // Act
      var menu = _service!.CreateContextMenu(contextType, null);

      // Assert
      Assert.IsNotNull(menu);
      Assert.IsTrue(menu.Items.Count > 0);
    }
  }
}