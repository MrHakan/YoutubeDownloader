using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cogwheel;
using CommunityToolkit.Mvvm.ComponentModel;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Utils;
using YoutubeDownloader.Framework;
using Container = YoutubeExplode.Videos.Streams.Container;

namespace YoutubeDownloader.Services;

[ObservableObject]
public partial class SettingsService : SettingsBase
{
    private readonly string _filePath;
    private readonly string _vaultPath;
    private readonly bool _migrate;
    private readonly string _legacyPath;

    [JsonIgnore]
    public string DataDirectory { get; }

    [JsonIgnore]
    public string? PersistenceError { get; private set; }

    public SettingsService(string? dataDirectory = null, string? legacyFile = null)
        : base(
            Path.Combine(dataDirectory ?? AppPaths.DataDirectory, "Settings.dat"),
            SerializerContext.Default
        )
    {
        DataDirectory = dataDirectory ?? AppPaths.DataDirectory;
        _filePath = Path.Combine(DataDirectory, "Settings.dat");
        _vaultPath = Path.Combine(DataDirectory, "auth.protected");
        _migrate = dataDirectory is null || legacyFile is not null;
        _legacyPath = legacyFile ?? Path.Combine(AppContext.BaseDirectory, "Settings.dat");
    }

    [ObservableProperty]
    public partial string Language { get; set; } = "tr";

    [ObservableProperty]
    public partial string LastDownloadDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    [ObservableProperty]
    public partial DownloadProfile[] Profiles { get; set; } = DownloadProfile.Defaults;

    [ObservableProperty]
    public partial string SelectedProfileId { get; set; } = "best";

    [ObservableProperty]
    public partial bool IsUkraineSupportMessageEnabled { get; set; }

    [ObservableProperty]
    public partial ThemeVariant Theme { get; set; }

    [ObservableProperty]
    public partial bool IsAutoUpdateEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAuthPersisted { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectLanguageSpecificAudioStreams { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectSubtitles { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectTags { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldSkipExistingFiles { get; set; }

    [ObservableProperty]
    public partial string FileNameTemplate { get; set; } = "$title";

    [ObservableProperty]
    public partial int ParallelLimit { get; set; } = 2;

    [ObservableProperty]
    [JsonIgnore]
    public partial IReadOnlyList<Cookie>? LastAuthCookies { get; set; }

    [ObservableProperty]
    [JsonConverter(typeof(ContainerJsonConverter))]
    public partial Container LastContainer { get; set; } = Container.Mp4;

    [ObservableProperty]
    public partial VideoQualityPreference LastVideoQualityPreference { get; set; } =
        VideoQualityPreference.Highest;

    public override void Save()
    {
        try
        {
            CookieVault.Save(_vaultPath, IsAuthPersisted ? LastAuthCookies : null);
            var json = JsonSerializer.SerializeToUtf8Bytes(
                this,
                SerializerContext.Default.SettingsService
            );
            Directory.CreateDirectory(DataDirectory);
            var temporary = _filePath + ".tmp";
            File.WriteAllBytes(temporary, json);
            if (File.Exists(_filePath))
                File.Replace(temporary, _filePath, null);
            else
                File.Move(temporary, _filePath);
            PersistenceError = null;
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or System.Security.Cryptography.CryptographicException
                        or PlatformNotSupportedException
            )
        {
            PersistenceError = "SettingsSaveFailed";
            new DiagnosticLog(Path.Combine(DataDirectory, "diagnostics.log")).Write(
                "settings-save",
                ex
            );
        }
    }

    public override bool Load()
    {
        try
        {
            var legacy = _legacyPath;
            var importing = _migrate && !File.Exists(_filePath) && File.Exists(legacy);
            var source = importing ? legacy : _filePath;
            if (File.Exists(source) && new FileInfo(source).Length > 1024 * 1024)
                throw new JsonException("Settings file is too large.");
            JsonObject? document = File.Exists(source)
                ? JsonNode.Parse(File.ReadAllText(source)) as JsonObject
                : null;
            if (importing && document is not null)
            {
                Directory.CreateDirectory(DataDirectory);
                var cookies = document["LastAuthCookies"];
                if (cookies is not null && (document["IsAuthPersisted"]?.GetValue<bool>() ?? true))
                    LastAuthCookies = JsonSerializer.Deserialize<Cookie[]>(cookies.ToJsonString());
                document.Remove("LastAuthCookies");
                // A migrated legacy updater opt-in is never reused for the independent application.
                document["IsAutoUpdateEnabled"] = false;
                File.WriteAllText(_filePath, document.ToJsonString());
                SaveLegacyBackup(legacy, document);
            }
            var loaded = base.Load();
            Language = Language == "en" ? "en" : "tr";
            ParallelLimit = Math.Clamp(ParallelLimit, 1, 10);
            Profiles =
                Profiles
                    ?.Where(p =>
                        p is not null && new[] { "mp4", "webm", "mp3", "ogg" }.Contains(p.Container)
                    )
                    .Take(30)
                    .ToArray() ?? [];
            if (Profiles.Length == 0)
                Profiles = DownloadProfile.Defaults;
            if (!Profiles.Any(p => p.Id == SelectedProfileId))
                SelectedProfileId = Profiles[0].Id;
            if (IsAuthPersisted)
                LastAuthCookies ??= CookieVault.Load(_vaultPath);
            if (importing)
                Save();
            return loaded;
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or JsonException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or InvalidOperationException
                        or System.Security.Cryptography.CryptographicException
                        or PlatformNotSupportedException
            )
        {
            PersistenceError = "SettingsLoadFailed";
            new DiagnosticLog(Path.Combine(DataDirectory, "diagnostics.log")).Write(
                "settings-load",
                ex
            );
            try
            {
                if (File.Exists(_filePath))
                    File.Copy(_filePath, _filePath + ".unreadable", true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }
    }

    private void SaveLegacyBackup(string legacy, JsonObject document)
    {
        CookieVault.Save(_vaultPath, IsAuthPersisted ? LastAuthCookies : null);
        // Preserve preferences, but remove clear-text cookies from both the legacy file and its backup.
        File.WriteAllText(legacy + ".migration-backup", document.ToJsonString());
        File.WriteAllText(legacy, document.ToJsonString());
    }
}

public partial class SettingsService
{
    private class ContainerJsonConverter : JsonConverter<Container>
    {
        public override Container Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            Container? result = null;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (
                        reader.TokenType == JsonTokenType.PropertyName
                        && reader.GetString() == "Name"
                        && reader.Read()
                        && reader.TokenType == JsonTokenType.String
                    )
                    {
                        var name = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            result = new Container(name);
                    }
                }
            }

            return result
                ?? throw new InvalidOperationException(
                    $"Invalid JSON for type '{typeToConvert.FullName}'."
                );
        }

        public override void Write(
            Utf8JsonWriter writer,
            Container value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString("Name", value.Name);
            writer.WriteEndObject();
        }
    }
}

public partial class SettingsService
{
    [JsonSerializable(typeof(SettingsService))]
    private partial class SerializerContext : JsonSerializerContext;
}
