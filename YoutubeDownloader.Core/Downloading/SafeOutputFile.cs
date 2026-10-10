using System;
using System.IO;

namespace YoutubeDownloader.Core.Downloading;

public sealed class SafeOutputFile : IDisposable
{
    private readonly string _directory;
    private readonly bool _overwrite;
    public string Destination { get; }
    public string TemporaryPath { get; }

    public SafeOutputFile(string destination, bool overwrite, Guid? operationId = null)
    {
        Destination = Path.GetFullPath(destination);
        _overwrite = overwrite;
        var parent = Path.GetDirectoryName(Destination)!;
        Directory.CreateDirectory(parent);
        var id = operationId ?? Guid.NewGuid();
        _directory = Path.Combine(parent, $".yd-{id:N}");
        var marker = Path.Combine(_directory, "owner.txt");
        var ownership = $"MrHakan.YoutubeDownloader\n{id:N}\n{Destination}";
        if (Directory.Exists(_directory))
        {
            if (!File.Exists(marker) || File.ReadAllText(marker) != ownership)
                throw new IOException("Temporary directory is not owned by this download.");
            Directory.Delete(_directory, true);
        }
        Directory.CreateDirectory(_directory);
        File.WriteAllText(marker, ownership);
        TemporaryPath = Path.Combine(_directory, Path.GetFileName(Destination));
    }

    public void Commit()
    {
        if (!File.Exists(TemporaryPath) || new FileInfo(TemporaryPath).Length == 0)
            throw new IOException("The downloaded file is empty.");
        if (_overwrite && File.Exists(Destination))
            File.Replace(TemporaryPath, Destination, null);
        else
            File.Move(TemporaryPath, Destination, false);
    }

    public void Dispose()
    {
        // Only this transaction's randomly named directory is owned by us.
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
