using System;
using System.IO;
using System.Text.Json;

namespace YoutubeDownloader.Core.Utils;

public static class AtomicJsonFile
{
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        if (new FileInfo(path).Length > 8 * 1024 * 1024)
            throw new IOException("State file is too large.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
}
