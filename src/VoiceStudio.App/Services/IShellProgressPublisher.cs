namespace VoiceStudio.App.Services;

/// <summary>
/// Canonical seam for reporting long-running work to shell progress (taskbar) with coordinator arbitration.
/// </summary>
public interface IShellProgressPublisher
{
  void ReportProgress(string sourceId, double progress01);

  void ReportIndeterminate(string sourceId);

  void ReportError(string sourceId);

  void ReportComplete(string sourceId);

  void ReportCancelled(string sourceId);
}

/// <summary>Null object for tests and pre-DI construction paths.</summary>
public sealed class NullShellProgressPublisher : IShellProgressPublisher
{
  public static readonly NullShellProgressPublisher Instance = new();

  private NullShellProgressPublisher()
  {
  }

  public void ReportProgress(string sourceId, double progress01)
  {
  }

  public void ReportIndeterminate(string sourceId)
  {
  }

  public void ReportError(string sourceId)
  {
  }

  public void ReportComplete(string sourceId)
  {
  }

  public void ReportCancelled(string sourceId)
  {
  }
}
