using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap067Slice2Tests
{
  private static string FindRepoRoot()
  {
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "" })
    {
      if (string.IsNullOrEmpty(start))
      {
        continue;
      }

      var dir = new DirectoryInfo(start);
      for (var i = 0; i < 16 && dir != null; i++, dir = dir.Parent)
      {
        var sln = Path.Combine(dir.FullName, "VoiceStudio.sln");
        if (File.Exists(sln))
        {
          return dir.FullName;
        }
      }
    }

    throw new InvalidOperationException("VoiceStudio.sln not found.");
  }

  private static string JumpListServicePath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "JumpListService.cs");

  private static string JumpListInteropPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Interop", "JumpListInterop.cs");

  private static string AppServicesPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "AppServices.cs");

  private static string AppXamlCsPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "App.xaml.cs");

  private static string JumpListArgsPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "JumpListArgs.cs");

  [TestMethod]
  public void JumpListService_Exists()
  {
    Assert.IsTrue(File.Exists(JumpListServicePath));
  }

  [TestMethod]
  public void JumpListService_HasUpdateJumpList()
  {
    var text = File.ReadAllText(JumpListServicePath);
    StringAssert.Contains(text, "UpdateJumpList");
  }

  [TestMethod]
  public void JumpListService_HasClearJumpList()
  {
    var text = File.ReadAllText(JumpListServicePath);
    StringAssert.Contains(text, "ClearJumpList");
  }

  [TestMethod]
  public void JumpListService_SubscribesToRecentProjectsPropertyChanged()
  {
    var text = File.ReadAllText(JumpListServicePath);
    StringAssert.Contains(text, "PropertyChanged");
  }

  [TestMethod]
  public void JumpListInterop_UsesICustomDestinationList()
  {
    var text = File.ReadAllText(JumpListInteropPath);
    StringAssert.Contains(text, "ICustomDestinationList");
  }

  [TestMethod]
  public void JumpListService_IncludesStaticTaskArguments()
  {
    var text = File.ReadAllText(JumpListServicePath);
    StringAssert.Contains(text, "JumpListArgs.NewProject");
    StringAssert.Contains(text, "JumpListArgs.OpenDialog");
  }

  [TestMethod]
  public void JumpListArgs_DefinesCommandLineTokens()
  {
    var text = File.ReadAllText(JumpListArgsPath);
    StringAssert.Contains(text, "--jumplist-new");
    StringAssert.Contains(text, "--jumplist-open-dialog");
  }

  [TestMethod]
  public void JumpListService_ProjectsFromRecentProjectsService()
  {
    var text = File.ReadAllText(JumpListServicePath);
    StringAssert.Contains(text, "RecentProjectsService");
    StringAssert.Contains(text, "AllProjects");
  }

  [TestMethod]
  public void App_HandlesJumpListActivation()
  {
    var text = File.ReadAllText(AppXamlCsPath);
    StringAssert.Contains(text, "JumpListActivation");
    StringAssert.Contains(text, "SetPendingIfParsed");
  }

  [TestMethod]
  public void AppServices_RegistersJumpListService()
  {
    var text = File.ReadAllText(AppServicesPath);
    StringAssert.Contains(text, "JumpListService");
  }
}
