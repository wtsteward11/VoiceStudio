using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Diagnostics;

namespace VoiceStudio.App.Tests.Diagnostics;

[TestClass]
public sealed class ExceptionDiagnosticsTests
{
  [TestMethod]
  public void GetRootException_unwraps_TargetInvocationException()
  {
    var inner = new InvalidOperationException("root cause");
    var tip = (Exception)Activator.CreateInstance(
        typeof(TargetInvocationException),
        BindingFlags.Instance | BindingFlags.Public,
        null,
        new object?[] { "outer", inner },
        null)!;

    var root = ExceptionDiagnostics.GetRootException(tip);
    Assert.AreSame(inner, root);
  }

  [TestMethod]
  public void FormatPanelCreateUserMessage_uses_root_type_and_message()
  {
    var inner = new FormatException("bad xaml");
    var tip = (Exception)Activator.CreateInstance(
        typeof(TargetInvocationException),
        BindingFlags.Instance | BindingFlags.Public,
        null,
        new object?[] { "reflection", inner },
        null)!;

    var msg = ExceptionDiagnostics.FormatPanelCreateUserMessage("EffectsMixer", tip);
    StringAssert.Contains(msg, "EffectsMixer");
    StringAssert.Contains(msg, typeof(FormatException).Name);
    StringAssert.Contains(msg, "bad xaml");
  }
}
