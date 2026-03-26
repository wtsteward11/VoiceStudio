using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Test backend client for ViewModel tests. Search is now owned by ISearchClient; use MockSearchClient for GlobalSearchViewModel.
  /// </summary>
  public sealed class MockBackendClient : BackendClient, IBackendClient
  {
    public MockBackendClient()
        : base(new BackendClientConfig
        {
          BaseUrl = "http://localhost:8000",
          WebSocketUrl = string.Empty,
          RequestTimeout = TimeSpan.FromSeconds(30)
        })
    {
    }
  }
}