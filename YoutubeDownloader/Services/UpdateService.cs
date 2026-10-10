using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Onova;
using Onova.Exceptions;
using Onova.Services;
using YoutubeDownloader.Core.Downloading;

namespace YoutubeDownloader.Services;

public class UpdateService(SettingsService settingsService) : IDisposable
{
    private readonly IUpdateManager? _updateManager =
        OperatingSystem.IsWindows() && !Program.IsDevelopmentBuild
            ? new UpdateManager(
                new GithubPackageResolver(
                    Program.ProjectOwner,
                    Program.ProjectRepository,
                    // Examples:
                    // YoutubeDownloader.win-arm64.zip
                    // YoutubeDownloader.win-x64.zip
                    // YoutubeDownloader.linux-x64.zip
                    // YoutubeDownloader.Bare.linux-x64.zip
                    GetAssetName(RuntimeInformation.RuntimeIdentifier, FFmpeg.IsBundled())
                ),
                new ZipPackageExtractor()
            )
            : null;

    private Version? _updateVersion;
    private bool _updatePrepared;
    private bool _updaterLaunched;

    public static string GetAssetName(string runtime, bool bundled) =>
        bundled ? $"YoutubeDownloader.{runtime}.zip" : $"YoutubeDownloader.Bare.{runtime}.zip";

    public async Task<Version?> CheckForUpdatesAsync(bool manual = false)
    {
        if (_updateManager is null)
            return null;

        if (!manual && !settingsService.IsAutoUpdateEnabled)
            return null;

        var check = await _updateManager.CheckForUpdatesAsync();
        return check.CanUpdate ? check.LastVersion : null;
    }

    public async Task<bool> PrepareUpdateAsync(Version version)
    {
        if (_updateManager is null)
            return false;

        try
        {
            await _updateManager.PrepareUpdateAsync(version);
            _updateVersion = version;
            _updatePrepared = true;
            return true;
        }
        catch (UpdaterAlreadyLaunchedException)
        {
            // Ignore race conditions
        }
        catch (LockFileNotAcquiredException)
        {
            // Ignore race conditions
        }
        return false;
    }

    public void FinalizeUpdate(bool needRestart)
    {
        if (_updateManager is null)
            return;

        if (_updateVersion is null || !_updatePrepared || _updaterLaunched)
            return;

        try
        {
            _updateManager.LaunchUpdater(_updateVersion, needRestart);
            _updaterLaunched = true;
        }
        catch (UpdaterAlreadyLaunchedException)
        {
            // Ignore race conditions
        }
        catch (LockFileNotAcquiredException)
        {
            // Ignore race conditions
        }
    }

    public void Dispose() => _updateManager?.Dispose();
}
