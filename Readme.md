# YoutubeDownloader (custom build)

This is the custom version maintained in [MrHakan/YoutubeDownloader](https://github.com/MrHakan/YoutubeDownloader), based on [Tyrrrz/YoutubeDownloader](https://github.com/Tyrrrz/YoutubeDownloader).
Automatic updates are disabled by default. If enabled, they only install releases from the custom repository.
The links below refer to this custom app; the original project's attribution is retained.

## Build and run

Install the .NET 9 SDK and PowerShell 7 (`pwsh`), then run:

```powershell
dotnet run --project YoutubeDownloader/YoutubeDownloader.csproj
```

FFmpeg is downloaded and bundled automatically during the build. To create a standalone Windows build:

```powershell
dotnet publish YoutubeDownloader/YoutubeDownloader.csproj -c Release -r win-x64 --self-contained true -o artifacts/custom-win-x64
pwsh -NoProfile -File scripts/Verify-Package.ps1 -PublishDir artifacts/custom-win-x64
```

## Custom version 1.17.0

- Turkish and English interface, remembered download folder, editable video/audio profiles and quick downloads.
- Persistent download queue with ordering, duplicate detection and explicit restart of interrupted jobs.
- Fresh stream metadata on each attempt, up to three attempts for temporary connection errors, and cancelable resolution/downloads.
- Temporary output on the destination disk; an existing file is replaced only after the new download succeeds.
- Download/conversion stages, transfer speed, remaining time and warnings for unavailable optional subtitles or tags.
- Manual update consent showing the version and custom repository. New installations and migrated legacy settings keep automatic checks disabled.

Settings and queue data live in `%LOCALAPPDATA%\MrHakan\YoutubeDownloader` on Windows. Existing `Settings.dat` preferences are migrated once. Persisted authentication cookies use Windows DPAPI for the current user and are excluded from the settings JSON, queue and logs. Disable **Persist authentication session** or log out to remove the stored session. Authentication UI and automatic update installation are Windows-only.

For portable settings, start `YoutubeDownloader.exe --portable` or create an empty `portable.flag` beside the executable. Data is then stored in the adjacent `Data` folder, which must be writable. Interrupted downloads restart from the beginning after you select **Restart unfinished**, using fresh stream information. Profile resolution values are maximums; an explicitly selected single-video quality is required unless you choose another format.

## Verification

```powershell
# Offline regression tests
dotnet run --project YoutubeDownloader.Tests --configuration Release

# Windows interface initialization and both languages
dotnet run --project YoutubeDownloader.Tests --configuration Release -- --ui

# Explicit public YouTube integration tests: MP4, MP3, playlist and transfer cancellation
$env:PATH = "$PWD\YoutubeDownloader;$env:PATH"
dotnet run --project YoutubeDownloader.Tests --configuration Release -- --live
```

Normal tests do not contact YouTube. Live tests use public fixtures without login and report service failures separately. CI checks formatting, offline tests and package contents before publishing; `Verify-Package.ps1` produces `VERSION.json` and `SHA256SUMS.txt`. Live tests also have a separate manual workflow. Enable Discord release notifications only by setting your own `DISCORD_WEBHOOK` secret and `ENABLE_DISCORD_NOTIFICATIONS=true` repository variable.

See [development plan](GELISTIRME_PLANI.md) and [release notes](CHANGELOG.md). Windows x64 is validated locally; Linux/macOS packages require their own runtime verification.

[![Status](https://img.shields.io/badge/status-maintenance-ffd700.svg)](https://github.com/Tyrrrz/.github/blob/master/docs/project-status.md)
[![Made in Ukraine](https://img.shields.io/badge/made_in-ukraine-ffd700.svg?labelColor=0057b7)](https://tyrrrz.me/ukraine)
[![Build](https://img.shields.io/github/actions/workflow/status/MrHakan/YoutubeDownloader/main.yml?branch=master)](https://github.com/MrHakan/YoutubeDownloader/actions)
[![Release](https://img.shields.io/github/release/MrHakan/YoutubeDownloader.svg)](https://github.com/MrHakan/YoutubeDownloader/releases)
[![Downloads](https://img.shields.io/github/downloads/MrHakan/YoutubeDownloader/total.svg)](https://github.com/MrHakan/YoutubeDownloader/releases)
[![Discord](https://img.shields.io/discord/869237470565392384?label=discord)](https://discord.gg/2SUWKFnHSm)
[![Fuck Russia](https://img.shields.io/badge/fuck-russia-e4181c.svg?labelColor=000000)](https://twitter.com/tyrrrz/status/1495972128977571848)

<table>
    <tr>
        <td width="99999" align="center">Development of this project is entirely funded by the community. <b><a href="https://tyrrrz.me/donate">Consider donating to support!</a></b></td>
    </tr>
</table>

<p align="center">
    <img src="favicon.png" alt="Icon" />
</p>

**YoutubeDownloader** is an application that lets you download videos from YouTube.
You can copy-paste URL of any video, playlist or channel and download it directly in a format of your choice.
It also supports searching by keywords, which is helpful if you want to quickly look up and download videos.

> [!NOTE]
> This application uses [**YoutubeExplode**](https://github.com/Tyrrrz/YoutubeExplode) under the hood to interact with YouTube.
> You can [read this article](https://tyrrrz.me/blog/reverse-engineering-youtube-revisited) to learn more about how it works.

## Terms of use<sup>[[?]](https://github.com/Tyrrrz/.github/blob/master/docs/why-so-political.md)</sup>

By using this project or its source code, for any purpose and in any shape or form, you grant your **implicit agreement** to all the following statements:

- You **condemn Russia and its military aggression against Ukraine**
- You **recognize that Russia is an occupant that unlawfully invaded a sovereign state**
- You **support Ukraine's territorial integrity, including its claims over temporarily occupied territories of Crimea and Donbas**
- You **reject false narratives perpetuated by Russian state propaganda**

To learn more about the war and how you can help, [click here](https://tyrrrz.me/ukraine). Glory to Ukraine! 🇺🇦

## Download

- 🟢 **[Custom releases](https://github.com/MrHakan/YoutubeDownloader/releases)**
- 🟠 [Custom CI build](https://github.com/MrHakan/YoutubeDownloader/actions/workflows/main.yml)

> [!IMPORTANT]
> To launch the app on MacOS, you need to first remove the downloaded file from quarantine.
> You can do that by running the following command in the terminal: `xattr -rd com.apple.quarantine YoutubeDownloader.app`.

> [!NOTE]
> If you're unsure which build is right for your system, consult with [this page](https://useragent.cc) to determine your OS and CPU architecture.

> [!NOTE]
> **YoutubeDownloader** comes bundled with [FFmpeg](https://ffmpeg.org) which is used for processing videos.
> You can also download a version of **YoutubeDownloader** that doesn't include FFmpeg (`YoutubeDownloader.Bare.*` builds) if you prefer to use your own installation.

## Features

- Cross-platform graphical user interface
- Download videos by URL
- Download videos from playlists or channels
- Download videos by search query
- Selectable video quality and format
- Automatically embed audio tracks in alternative languages
- Automatically embed subtitles
- Automatically inject media tags
- Log in with a YouTube account to access private content

## Screenshots

![list](.assets/list.png)
![single](.assets/single.png)
![multiple](.assets/multiple.png)

