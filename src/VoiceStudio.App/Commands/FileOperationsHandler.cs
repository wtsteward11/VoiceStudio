using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Audio;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Commands
{
    /// <summary>
    /// Handles all file-related commands: new, open, save, saveAs, import, export.
    /// </summary>
    public sealed class FileOperationsHandler
    {
        private readonly IUnifiedCommandRegistry _registry;
        private readonly IProjectRepository _projectRepository;
        private readonly IDialogService _dialogService;
        private readonly IBackendClient? _backendClient;
        private readonly ToastNotificationService? _toastService;

        private Project? _currentProject;
        private string? _currentProjectPath;

        public event EventHandler<Project?>? CurrentProjectChanged;

        public FileOperationsHandler(
            IUnifiedCommandRegistry registry,
            IProjectRepository projectRepository,
            IDialogService dialogService,
            IBackendClient? backendClient = null,
            ToastNotificationService? toastService = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _backendClient = backendClient;
            _toastService = toastService;

            RegisterCommands();
        }

        public Project? CurrentProject => _currentProject;
        public string? CurrentProjectPath => _currentProjectPath;
        public bool HasUnsavedChanges { get; private set; }

        private void RegisterCommands()
        {
            // file.new
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.new",
                    Title = "New Project",
                    Description = "Create a new project",
                    Category = "File",
                    Icon = "📄",
                    KeyboardShortcut = "Ctrl+N"
                },
                async (param, ct) => await NewProjectAsync(ct),
                _ => true
            );

            // file.open
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.open",
                    Title = "Open Project",
                    Description = "Open an existing project",
                    Category = "File",
                    Icon = "📂",
                    KeyboardShortcut = "Ctrl+O"
                },
                async (param, ct) => await OpenProjectAsync(param as string, ct),
                _ => true
            );

            // file.save
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.save",
                    Title = "Save Project",
                    Description = "Save the current project",
                    Category = "File",
                    Icon = "💾",
                    KeyboardShortcut = "Ctrl+S"
                },
                async (param, ct) => await SaveProjectAsync(ct),
                _ => _currentProject != null
            );

            // file.saveAs
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.saveAs",
                    Title = "Save Project As",
                    Description = "Save the current project with a new name",
                    Category = "File",
                    Icon = "💾",
                    KeyboardShortcut = "Ctrl+Shift+S"
                },
                async (param, ct) => await SaveProjectAsAsync(ct),
                _ => _currentProject != null
            );

            // file.import
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.import",
                    Title = "Import Audio",
                    Description = "Import audio files into the project",
                    Category = "File",
                    Icon = "📥",
                    KeyboardShortcut = "Ctrl+I"
                },
                async (param, ct) => await ImportAudioAsync(ct),
                _ => _currentProject != null
            );

            // file.export
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.export",
                    Title = "Export Audio",
                    Description = "Export audio from the project",
                    Category = "File",
                    Icon = "📤",
                    KeyboardShortcut = "Ctrl+Shift+E"
                },
                async (param, ct) => await ExportAudioAsync(ct),
                _ => _currentProject != null
            );

            // file.close
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "file.close",
                    Title = "Close Project",
                    Description = "Close the current project",
                    Category = "File",
                    Icon = "❌"
                },
                async (param, ct) => await CloseProjectAsync(ct),
                _ => _currentProject != null
            );

            Debug.WriteLine("[FileOperationsHandler] Registered 7 file commands");
        }

        public async Task NewProjectAsync(CancellationToken ct = default)
        {
            // Check for unsaved changes
            if (HasUnsavedChanges)
            {
                var save = await _dialogService.ShowConfirmationAsync(
                    "Unsaved Changes",
                    "Do you want to save changes to the current project?",
                    "Save", "Don't Save");

                if (save)
                {
                    await SaveProjectAsync(ct);
                }
            }

            var name = await _dialogService.ShowInputAsync(
                "New Project",
                "Enter project name:",
                "Untitled Project",
                "Project name");

            if (string.IsNullOrWhiteSpace(name))
            {
                return; // User cancelled
            }

            _currentProject = new Project
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o")
            };
            _currentProjectPath = null;
            HasUnsavedChanges = true;

            CurrentProjectChanged?.Invoke(this, _currentProject);
            _toastService?.ShowInfo($"Created new project: {name}");

            Debug.WriteLine($"[FileOperationsHandler] New project created: {name}");
        }

        public async Task OpenProjectAsync(string? projectPath = null, CancellationToken ct = default)
        {
            // Check for unsaved changes first
            if (HasUnsavedChanges)
            {
                var save = await _dialogService.ShowConfirmationAsync(
                    "Unsaved Changes",
                    "Do you want to save changes before opening another project?",
                    "Save", "Don't Save");

                if (save)
                {
                    await SaveProjectAsync(ct);
                }
            }

            string? selectedPath = projectPath;
            if (string.IsNullOrEmpty(selectedPath))
            {
                selectedPath = await _dialogService.ShowOpenFileAsync(
                    "Open Project",
                    ".vsproj", ".json");

                if (string.IsNullOrEmpty(selectedPath))
                {
                    return; // User cancelled
                }
            }

            try
            {
                // Try to load from repository (if it's a project ID)
                var project = await _projectRepository.OpenAsync(selectedPath, ct);
                if (project != null)
                {
                    _currentProject = project;
                    _currentProjectPath = selectedPath;
                    HasUnsavedChanges = false;

                    CurrentProjectChanged?.Invoke(this, _currentProject);
                    _toastService?.ShowSuccess($"Opened project: {project.Name}");

                    Debug.WriteLine($"[FileOperationsHandler] Opened project: {project.Name}");
                }
                else
                {
                    await _dialogService.ShowErrorAsync(
                        "Open Failed",
                        "Could not open the selected project.",
                        "The project file may be corrupted or in an unsupported format.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileOperationsHandler] Open failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Open Failed",
                    $"Failed to open project: {ex.Message}");
            }
        }

        public async Task SaveProjectAsync(CancellationToken ct = default)
        {
            if (_currentProject == null)
            {
                await _dialogService.ShowMessageAsync("No Project", "No project is currently open.");
                return;
            }

            try
            {
                _currentProject.UpdatedAt = DateTime.UtcNow.ToString("o");
                await _projectRepository.SaveAsync(_currentProject, ct);
                HasUnsavedChanges = false;

                _toastService?.ShowSuccess("Project saved");
                Debug.WriteLine($"[FileOperationsHandler] Project saved: {_currentProject.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileOperationsHandler] Save failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Save Failed",
                    $"Failed to save project: {ex.Message}");
            }
        }

        public async Task SaveProjectAsAsync(CancellationToken ct = default)
        {
            if (_currentProject == null)
            {
                await _dialogService.ShowMessageAsync("No Project", "No project is currently open.");
                return;
            }

            var name = await _dialogService.ShowInputAsync(
                "Save Project As",
                "Enter new project name:",
                _currentProject.Name,
                "Project name");

            if (string.IsNullOrWhiteSpace(name))
            {
                return; // User cancelled
            }

            // Create a copy with new ID and name
            var newProject = new Project
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                // Copy other properties as needed
            };

            try
            {
                await _projectRepository.SaveAsync(newProject, ct);
                _currentProject = newProject;
                _currentProjectPath = null;
                HasUnsavedChanges = false;

                CurrentProjectChanged?.Invoke(this, _currentProject);
                _toastService?.ShowSuccess($"Project saved as: {name}");

                Debug.WriteLine($"[FileOperationsHandler] Project saved as: {name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileOperationsHandler] Save As failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Save As Failed",
                    $"Failed to save project: {ex.Message}");
            }
        }

        private static void FileLog(string msg)
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceStudio", "import_debug.log");
            // ALLOWED: empty catch - Best effort debug logging, failure is acceptable
            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
        }

        public async Task ImportAudioAsync(CancellationToken ct = default)
        {
            Debug.WriteLine("[FileOperationsHandler] ImportAudioAsync called");
            FileLog("[FileOperationsHandler] ImportAudioAsync called");
            
            string[]? files;
            try
            {
                Debug.WriteLine("[FileOperationsHandler] Calling ShowOpenFilesAsync...");
                FileLog("[FileOperationsHandler] Calling ShowOpenFilesAsync...");
                // Use centralized format list for all supported audio formats
                files = await _dialogService.ShowOpenFilesAsync(
                    "Import Audio Files",
                    AudioFileFormats.ImportExtensions.ToArray());
                Debug.WriteLine($"[FileOperationsHandler] ShowOpenFilesAsync returned: {(files == null ? "null" : $"{files.Length} files")}");
                FileLog($"[FileOperationsHandler] ShowOpenFilesAsync returned: {(files == null ? "null" : $"{files.Length} files")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileOperationsHandler] ShowOpenFilesAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                FileLog($"[FileOperationsHandler] ShowOpenFilesAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                FileLog($"[FileOperationsHandler] Stack: {ex.StackTrace}");
                if (ex is System.Runtime.InteropServices.COMException comEx)
                {
                    FileLog($"[FileOperationsHandler] COM HResult: 0x{comEx.HResult:X8}");
                }
                throw;
            }

            if (files == null || files.Length == 0)
            {
                return; // User cancelled
            }

            var progress = await _dialogService.ShowProgressAsync(
                "Importing Audio",
                $"Importing {files.Length} file(s)...",
                true);

            try
            {
                var successCount = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    if (progress.IsCancellationRequested)
                    {
                        break;
                    }

                    var fileName = Path.GetFileName(files[i]);
                    progress.SetMessage($"Importing: {fileName}");
                    progress.SetProgress((double)(i + 1) / files.Length);

                    try
                    {
                        if (_backendClient != null)
                        {
                            // Upload to backend for processing
                            var result = await _backendClient.UploadAudioFileAsync(files[i], ct);
                            Debug.WriteLine($"[FileOperationsHandler] Uploaded: {fileName} -> {result.Id}");

                            // If project exists, save to project
                            if (_currentProject != null)
                            {
                                await _backendClient.SaveAudioToProjectAsync(_currentProject.Id, result.Id, fileName, ct);
                            }

                            // Publish AssetAddedEvent so Library and other panels refresh
                            var eventAggregator = AppServices.TryGetEventAggregator();
                            eventAggregator?.Publish(new AssetAddedEvent(
                                "file-operations",
                                result.Id,
                                "audio",
                                files[i]));
                        }
                        else
                        {
                            // Fallback: copy to local project folder
                            Debug.WriteLine($"[FileOperationsHandler] Backend not available, skipping upload for: {fileName}");
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FileOperationsHandler] Failed to import {fileName}: {ex.Message}");
                        // Continue with other files
                    }
                }

                if (successCount > 0)
                {
                    HasUnsavedChanges = true;
                    _toastService?.ShowSuccess($"Imported {successCount} of {files.Length} audio file(s)");
                }
                else
                {
                    _toastService?.ShowWarning("Import", "No files were imported");
                }
                Debug.WriteLine($"[FileOperationsHandler] Imported {successCount}/{files.Length} files");
            }
            finally
            {
                progress.Close();
            }
        }

        public async Task ExportAudioAsync(CancellationToken ct = default)
        {
            if (_currentProject == null)
            {
                await _dialogService.ShowMessageAsync("No Project", "No project is currently open.");
                return;
            }

            // Use centralized format list for all supported export formats
            var path = await _dialogService.ShowSaveFileAsync(
                "Export Audio",
                $"{_currentProject.Name}.wav",
                AudioFileFormats.ExportExtensions.ToArray());

            if (string.IsNullOrEmpty(path))
            {
                return; // User cancelled
            }

            var progress = await _dialogService.ShowProgressAsync(
                "Exporting Audio",
                "Preparing export...",
                true);

            try
            {
                progress.SetMessage("Exporting audio...");
                progress.SetProgress(0.25);

                if (_backendClient != null && _currentProject != null)
                {
                    // Get audio files from project
                    var audioFiles = await _backendClient.ListProjectAudioAsync(_currentProject.Id, ct);
                    progress.SetProgress(0.4);

                    if (audioFiles.Count > 0)
                    {
                        // Export the first/main audio file
                        var mainAudio = audioFiles[0];
                        
                        // Determine target format from the selected file extension
                        var targetExtension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                        var sourceFilename = mainAudio.Filename;
                        
                        progress.SetMessage($"Converting to {targetExtension.ToUpperInvariant()}...");
                        progress.SetProgress(0.5);

                        // Get default bitrate for lossy formats
                        var targetFormat = AudioFileFormats.GetFormatByExtension(targetExtension);
                        int? bitrateKbps = null;
                        if (targetFormat.HasValue)
                        {
                            var formatInfo = AudioFileFormats.GetInfo(targetFormat.Value);
                            bitrateKbps = formatInfo.DefaultBitrateKbps;
                        }

                        // Use the new export API with format conversion
                        using var audioStream = await _backendClient.ExportAudioAsync(
                            source: mainAudio.AudioId ?? sourceFilename,
                            targetFormat: targetExtension,
                            bitrateKbps: bitrateKbps,
                            cancellationToken: ct);
                        
                        progress.SetProgress(0.75);

                        // Write to file
                        using var fileStream = File.Create(path);
                        await audioStream.CopyToAsync(fileStream, ct);
                        progress.SetProgress(1.0);

                        _toastService?.ShowSuccess($"Audio exported to: {Path.GetFileName(path)}");
                        Debug.WriteLine($"[FileOperationsHandler] Exported to: {path} (format: {targetExtension})");
                    }
                    else
                    {
                        _toastService?.ShowWarning("Export", "No audio files in project to export");
                        Debug.WriteLine("[FileOperationsHandler] No audio files to export");
                    }
                }
                else
                {
                    // Backend not available - show message
                    progress.SetProgress(1.0);
                    _toastService?.ShowWarning("Export", "Backend service not available for export");
                    Debug.WriteLine("[FileOperationsHandler] Backend not available for export");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileOperationsHandler] Export failed: {ex.Message}");
                _toastService?.ShowError("Export Failed", ex.Message);
            }
            finally
            {
                progress.Close();
            }
        }

        public async Task CloseProjectAsync(CancellationToken ct = default)
        {
            if (_currentProject == null)
            {
                return;
            }

            if (HasUnsavedChanges)
            {
                var save = await _dialogService.ShowConfirmationAsync(
                    "Unsaved Changes",
                    "Do you want to save changes before closing?",
                    "Save", "Don't Save");

                if (save)
                {
                    await SaveProjectAsync(ct);
                }
            }

            var projectName = _currentProject.Name;
            _currentProject = null;
            _currentProjectPath = null;
            HasUnsavedChanges = false;

            CurrentProjectChanged?.Invoke(this, null);
            _toastService?.ShowInfo($"Closed project: {projectName}");

            Debug.WriteLine($"[FileOperationsHandler] Project closed: {projectName}");
        }

        public void MarkDirty()
        {
            HasUnsavedChanges = true;
        }
    }
}
