using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DialogHostAvalonia;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils.Extensions;

namespace YoutubeDownloader.Framework;

public class DialogManager(SettingsService settings) : IDisposable
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    public async Task<T?> ShowDialogAsync<T>(
        DialogViewModelBase<T> dialog,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = false;
        try
        {
            await _dialogLock.WaitAsync(cancellationToken);
            acquired = true;
            using var cancellation = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() => dialog.CloseCommand.Execute(default(T)))
            );
            cancellationToken.ThrowIfCancellationRequested();
            await DialogHost.Show(
                dialog,
                // It's fine to await in a void method here because it's an event handler
                // ReSharper disable once AsyncVoidLambda
                async (object _, DialogOpenedEventArgs args) =>
                {
                    await dialog.WaitForCloseAsync();

                    try
                    {
                        args.Session.Close();
                    }
                    catch (InvalidOperationException)
                    {
                        // Dialog host is already processing a close operation
                    }
                }
            );

            cancellationToken.ThrowIfCancellationRequested();
            return dialog.DialogResult;
        }
        finally
        {
            if (acquired)
                _dialogLock.Release();
            dialog.Dispose();
        }
    }

    public async Task<string?> PromptSaveFilePathAsync(
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string defaultFilePath = ""
    )
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                FileTypeChoices = fileTypes,
                SuggestedFileName = defaultFilePath,
                DefaultExtension = Path.GetExtension(defaultFilePath).TrimStart('.'),
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                    settings.LastDownloadDirectory
                ),
            }
        );

        return file?.TryGetLocalPath() ?? file?.Path.ToString();
    }

    public async Task<string?> PromptDirectoryPathAsync(string defaultDirPath = "")
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                    defaultDirPath
                ),
            }
        );

        var directory = result.FirstOrDefault();
        if (directory is null)
            return null;

        return directory.TryGetLocalPath() ?? directory.Path.ToString();
    }

    public void Dispose() => _dialogLock.Dispose();
}
