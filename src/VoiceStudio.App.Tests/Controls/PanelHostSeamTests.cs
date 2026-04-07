#nullable enable

using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;

namespace VoiceStudio.App.Tests.Controls;

[TestClass]
public sealed class PanelHostSeamTests
{
  private const BindingFlags DeclaredPublicStatic =
      BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static;

  [TestMethod]
  public void PanelHost_DoesNotShadowContentProperty()
  {
    var shadowField = typeof(PanelHost).GetField("ContentProperty", DeclaredPublicStatic);
    Assert.IsNull(shadowField, "PanelHost must not declare ContentProperty (avoid shadowing ContentControl).");
  }

  private const BindingFlags DeclaredPublicInstance =
      BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;

  [TestMethod]
  public void PanelHost_HostedPanelProperty_IsRegistered()
  {
    var field = typeof(PanelHost).GetField("HostedPanelProperty", DeclaredPublicStatic);
    Assert.IsNotNull(field, "HostedPanelProperty field must exist on PanelHost.");
    var value = field.GetValue(null);
    Assert.IsInstanceOfType(value, typeof(DependencyProperty));
  }

  [TestMethod]
  public void PanelHost_HostedPanelProperty_TypeIsUIElement()
  {
    var clr = typeof(PanelHost).GetProperty("HostedPanel", DeclaredPublicInstance);
    Assert.IsNotNull(clr, "HostedPanel CLR property must exist on PanelHost.");
    Assert.AreEqual(typeof(UIElement), clr.PropertyType);
  }
}
