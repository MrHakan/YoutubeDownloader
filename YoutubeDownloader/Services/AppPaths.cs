using System;
using System.IO;
using System.Linq;

namespace YoutubeDownloader.Services;

public static class AppPaths
{
    public static bool IsPortable =>
        Environment.GetCommandLineArgs().Contains("--portable")
        || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    public static string DataDirectory =>
        IsPortable
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MrHakan",
                "YoutubeDownloader"
            );
}
