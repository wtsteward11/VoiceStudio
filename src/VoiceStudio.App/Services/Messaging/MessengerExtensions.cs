using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Messaging;

namespace VoiceStudio.App.Services.Messaging
{
  /// <summary>
  /// Extension methods for <see cref="IAppMessenger"/>.
  /// </summary>
  public static class MessengerExtensions
  {
    /// <summary>
    /// Registers a recipient and ensures the handler runs on the UI thread.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="messenger">The messenger.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="handler">The message handler.</param>
    public static void RegisterOnUI<TMessage>(
        this IAppMessenger messenger,
        object recipient,
        DispatcherQueue dispatcher,
        Action<TMessage> handler)
        where TMessage : class
    {
      if (messenger == null) throw new ArgumentNullException(nameof(messenger));
      if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
      if (handler == null) throw new ArgumentNullException(nameof(handler));

      messenger.Register<TMessage>(recipient, message =>
      {
        if (dispatcher.HasThreadAccess)
        {
          handler(message);
        }
        else
        {
          dispatcher.TryEnqueue(() => handler(message));
        }
      });
    }

    /// <summary>
    /// Registers a recipient with an async handler.
    /// Note: The handler runs asynchronously and exceptions should be handled within.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="messenger">The messenger.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="handler">The async message handler.</param>
    public static void RegisterAsync<TMessage>(
        this IAppMessenger messenger,
        object recipient,
        Func<TMessage, Task> handler)
        where TMessage : class
    {
      if (messenger == null) throw new ArgumentNullException(nameof(messenger));
      if (handler == null) throw new ArgumentNullException(nameof(handler));

      messenger.Register<TMessage>(recipient, message =>
      {
        // Fire and forget, but log exceptions
        _ = Task.Run(async () =>
        {
          try
          {
            await handler(message);
          }
          catch (Exception ex)
          {
            // Log the exception - in production this should use the error logging service
            System.Diagnostics.Debug.WriteLine($"[AppMessenger] Async handler error: {ex.Message}");
          }
        });
      });
    }

    /// <summary>
    /// Registers a recipient with an async handler and a cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="messenger">The messenger.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="handler">The async message handler.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void RegisterAsync<TMessage>(
        this IAppMessenger messenger,
        object recipient,
        Func<TMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
        where TMessage : class
    {
      if (messenger == null) throw new ArgumentNullException(nameof(messenger));
      if (handler == null) throw new ArgumentNullException(nameof(handler));

      messenger.Register<TMessage>(recipient, message =>
      {
        _ = Task.Run(async () =>
        {
          try
          {
            await handler(message, cancellationToken);
          }
          // ALLOWED: empty catch - cancellation is intentional, not an error
          catch (OperationCanceledException)
          {
            // Expected when cancelled
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"[AppMessenger] Async handler error: {ex.Message}");
          }
        }, cancellationToken);
      });
    }

    /// <summary>
    /// Sends a toast message through the messenger.
    /// </summary>
    /// <param name="messenger">The messenger.</param>
    /// <param name="title">The toast title.</param>
    /// <param name="message">The toast message.</param>
    /// <param name="severity">The toast severity.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    public static void ShowToast(
        this IAppMessenger messenger,
        string title,
        string message,
        ToastSeverity severity = ToastSeverity.Information,
        int durationMs = 3000)
    {
      if (messenger == null) throw new ArgumentNullException(nameof(messenger));

      messenger.Send(new ShowToastMessage(title, message)
      {
        Severity = severity,
        DurationMs = durationMs
      });
    }

    /// <summary>
    /// Sends a success toast through the messenger.
    /// </summary>
    public static void ShowSuccessToast(this IAppMessenger messenger, string title, string message)
    {
      messenger.ShowToast(title, message, ToastSeverity.Success);
    }

    /// <summary>
    /// Sends a warning toast through the messenger.
    /// </summary>
    public static void ShowWarningToast(this IAppMessenger messenger, string title, string message)
    {
      messenger.ShowToast(title, message, ToastSeverity.Warning);
    }

    /// <summary>
    /// Sends an error toast through the messenger.
    /// </summary>
    public static void ShowErrorToast(this IAppMessenger messenger, string title, string message)
    {
      messenger.ShowToast(title, message, ToastSeverity.Error);
    }

    /// <summary>
    /// Sends a navigation request through the messenger.
    /// </summary>
    /// <param name="messenger">The messenger.</param>
    /// <param name="target">The navigation target.</param>
    /// <param name="parameters">Optional navigation parameters.</param>
    public static void NavigateTo(
        this IAppMessenger messenger,
        string target,
        System.Collections.Generic.IReadOnlyDictionary<string, object>? parameters = null)
    {
      if (messenger == null) throw new ArgumentNullException(nameof(messenger));

      messenger.Send(new NavigationRequestMessage(target, parameters));
    }

    /// <summary>
    /// Sends a refresh request through the messenger.
    /// </summary>
    /// <param name="messenger">The messenger.</param>
    /// <param name="target">Optional target to refresh (null for all).</param>
    /// <param name="forceFullRefresh">Whether to force a full refresh.</param>
    public static void RequestRefresh(
        this IAppMessenger messenger,
        string? target = null,
        bool forceFullRefresh = false)
    {
      if (messenger == null) throw new ArgumentNullException(nameof(messenger));

      messenger.Send(new RefreshRequestMessage
      {
        Target = target,
        ForceFullRefresh = forceFullRefresh
      });
    }
  }
}
