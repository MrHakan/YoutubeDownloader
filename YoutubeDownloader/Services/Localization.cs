using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;

namespace YoutubeDownloader.Services;

public static class Localization
{
    private static readonly Dictionary<string, string[]> Texts = Load();
    public static string Language { get; private set; } = "tr";
    public static event Action? Changed;

    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream(
            "YoutubeDownloader.Localization.Texts.json"
        )!;
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
    }

    public static string Key(string text) =>
        "Ui" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    public static string Get(string text) =>
        Texts.TryGetValue(text, out var values) ? values[Language == "en" ? 0 : 1] : text;

    public static bool Contains(string text) => Texts.ContainsKey(text);

    public static void Apply(string language)
    {
        Language = language == "en" ? "en" : "tr";
        if (Application.Current is { } app)
            foreach (var text in Texts.Keys)
                app.Resources[Key(text)] = Get(text);
        Changed?.Invoke();
    }
}
