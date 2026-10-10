using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace YoutubeDownloader.Services;

public static class CookieVault
{
    private record SavedCookie(
        string Name,
        string Value,
        string Path,
        string Domain,
        DateTime Expires,
        bool Secure,
        bool HttpOnly
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(
        ref Blob input,
        string? description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out Blob output
    );

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref Blob input,
        IntPtr description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out Blob output
    );

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Persistent authentication requires Windows user protection."
            );
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect
                ? CryptProtectData(
                    ref input,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1,
                    out output
                )
                : CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1,
                    out output
                );
            if (!success)
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static void Save(string path, IReadOnlyList<Cookie>? cookies)
    {
        if (cookies?.Count is not > 0)
        {
            File.Delete(path);
            return;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            cookies.Select(c => new SavedCookie(
                c.Name,
                c.Value,
                c.Path,
                c.Domain,
                c.Expires,
                c.Secure,
                c.HttpOnly
            ))
        );
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, Transform(bytes, true));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static IReadOnlyList<Cookie>? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new IOException("Cookie vault is too large.");
        var bytes = Transform(File.ReadAllBytes(path), false);
        try
        {
            return JsonSerializer
                .Deserialize<SavedCookie[]>(bytes)
                ?.Select(c => new Cookie(c.Name, c.Value, c.Path, c.Domain)
                {
                    Expires = c.Expires,
                    Secure = c.Secure,
                    HttpOnly = c.HttpOnly,
                })
                .Where(c => !c.Expired)
                .ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
