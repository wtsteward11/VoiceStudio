using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Services;
using TemplateItem = VoiceStudio.App.ViewModels.TemplateItem;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a template.
  /// </summary>
  public class CreateTemplateAction : IUndoableAction
  {
    private readonly ObservableCollection<TemplateItem> _templates;
    private readonly ITemplateLibraryClient _templateLibraryClient;
    private readonly TemplateItem _template;
    private readonly Action<TemplateItem>? _onUndo;
    private readonly Action<TemplateItem>? _onRedo;

    public string ActionName => $"Create Template '{_template.Name ?? "Unnamed"}'";

    public CreateTemplateAction(
        ObservableCollection<TemplateItem> templates,
        ITemplateLibraryClient templateLibraryClient,
        TemplateItem template,
        Action<TemplateItem>? onUndo = null,
        Action<TemplateItem>? onRedo = null)
    {
      _templates = templates ?? throw new ArgumentNullException(nameof(templates));
      _templateLibraryClient = templateLibraryClient ?? throw new ArgumentNullException(nameof(templateLibraryClient));
      _template = template ?? throw new ArgumentNullException(nameof(template));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var templateToRemove = _templates.FirstOrDefault(t => t.Id == _template.Id);
      if (templateToRemove != null)
      {
        _templates.Remove(templateToRemove);
        _onUndo?.Invoke(templateToRemove);
      }
    }

    public void Redo()
    {
      if (!_templates.Any(t => t.Id == _template.Id))
      {
        _templates.Insert(0, _template);
        _onRedo?.Invoke(_template);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a template.
  /// </summary>
  public class DeleteTemplateAction : IUndoableAction
  {
    private readonly ObservableCollection<TemplateItem> _templates;
    private readonly ITemplateLibraryClient _templateLibraryClient;
    private readonly TemplateItem _template;
    private readonly int _originalIndex;
    private readonly Action<TemplateItem>? _onUndo;
    private readonly Action<TemplateItem>? _onRedo;

    public string ActionName => $"Delete Template '{_template.Name ?? "Unnamed"}'";

    public DeleteTemplateAction(
        ObservableCollection<TemplateItem> templates,
        ITemplateLibraryClient templateLibraryClient,
        TemplateItem template,
        int originalIndex,
        Action<TemplateItem>? onUndo = null,
        Action<TemplateItem>? onRedo = null)
    {
      _templates = templates ?? throw new ArgumentNullException(nameof(templates));
      _templateLibraryClient = templateLibraryClient ?? throw new ArgumentNullException(nameof(templateLibraryClient));
      _template = template ?? throw new ArgumentNullException(nameof(template));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_templates.Any(t => t.Id == _template.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _templates.Count)
        {
          _templates.Insert(_originalIndex, _template);
        }
        else
        {
          _templates.Add(_template);
        }
        _onUndo?.Invoke(_template);
      }
    }

    public void Redo()
    {
      var templateToRemove = _templates.FirstOrDefault(t => t.Id == _template.Id);
      if (templateToRemove != null)
      {
        _templates.Remove(templateToRemove);
        _onRedo?.Invoke(templateToRemove);
      }
    }
  }
}