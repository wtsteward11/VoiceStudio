using System;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Response models for the library API.
  /// </summary>
  public class LibraryFoldersResponse
  {
    public LibraryFolder[]? Folders { get; set; }
  }

  public class AssetSearchResponse
  {
    public LibraryAsset[]? Assets { get; set; }
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
  }

  public class AssetTypesResponse
  {
    public AssetTypeInfo[]? Types { get; set; }
  }

  public class AssetTypeInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }
}
