# FreedomMedia

A private, encrypted vault for your photographs and videos, with its own built-in photo viewer
and media player. FreedomMedia keeps personal media inside an encrypted file that is opened with
a passphrase known only to its owner, and it shows that media itself rather than handing it to
another program. A photograph is decrypted only into memory, for the moment it is displayed, and
a video is streamed to the built-in player a piece at a time, so no readable copy is written to a
temporary folder for another application to find.

FreedomMedia is free and open source, part of [FreedomSoft](https://freedomsoft.uk). It runs on
Windows and macOS, stores everything on your own computer, and needs no account and no internet
connection.

## Why it exists

Personal photographs and videos accumulate over many years, and they are ordinarily kept in plain
folders on a laptop, an external drive or a shared computer. Anyone who gains access to the device,
whether through loss, theft, repair or simple borrowing, also gains access to that material.
Encrypting individual files offers only partial protection, because the file must be decrypted to
an ordinary folder before it can be opened, and a readable copy is frequently left behind.

FreedomMedia keeps the media sealed. Albums may be browsed, photographs viewed and videos played,
and everything remains encrypted on disk when the vault is closed.

## Features

- Create as many vaults as you like. Each vault is a single `.dvault` file with its own passphrase.
- Import photographs and videos into a vault, and export them back out to ordinary files when needed.
- View photographs and play videos inside the application, never through another program.
- Browse the collection as icons, a list or a details table, with a status bar showing the number
  of items, the total size and the details of the current selection.
- Standard File, Edit, View and Help menus, with the usual keyboard shortcuts.
- No attempt limit and no hidden behaviour: a vault is a plain encrypted file that opens whenever
  the correct passphrase is given.

## Download

Installers for Windows and macOS are published on the
[releases page](https://github.com/shahidzafarsg/freedommedia/releases/latest).

- **Windows**: download `FreedomMedia-Setup-x64.exe` and run it. It installs per-user, so no
  administrator prompt is required. Windows SmartScreen may warn about an unrecognised publisher
  because the installer is not code-signed; choose More info, then Run anyway.
- **macOS**: download the `.dmg` for your processor (`arm64` for Apple Silicon, `x64` for Intel),
  open it, and drag FreedomMedia to Applications. The app is ad-hoc signed but not notarised, so
  the first time you open it, right-click it and choose Open, then confirm.

## How the encryption works

Each vault is a single file with a small plaintext header followed by an encrypted index and the
encrypted media.

- The passphrase is turned into a key with **Argon2id** (128 MiB of memory, 3 passes), which makes
  guessing attempts expensive.
- That key unwraps a random 256-bit **master key**, which never leaves memory.
- Each media file gets its own key by **HKDF-SHA256** from the master key, and is split into 1 MiB
  chunks. Every chunk is encrypted with **AES-256-GCM**, which also authenticates it, so any
  corruption or tampering is detected on reading.

There is no attempt counter and no self-destruct. The strength of a vault rests on the strength of
its passphrase; a long, memorable passphrase is far harder to guess than a short one. A single-pass
overwrite is not performed on removal, and no application-level overwrite can guarantee erasure on
flash or solid-state media, so treat export as producing an ordinary, unprotected file.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/shahidzafarsg/freedommedia.git
cd freedommedia
dotnet run --project tools/FreedomMedia.SelfTest -c Release   # verifies the vault core
dotnet run --project src/FreedomMedia.App -c Release          # launches the application
```

To produce a self-contained build for the current platform, for example Windows x64:

```
dotnet publish src/FreedomMedia.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true
```

Replace `win-x64` with `win-arm64`, `osx-arm64` or `osx-x64` as required. The macOS application
bundle is assembled by the release workflow in `.github/workflows/build.yml`.

## Project layout

```
src/FreedomMedia.Core   The vault format and all cryptography (no user interface).
src/FreedomMedia.App    The Avalonia application: gallery, photo viewer and media player.
tools/FreedomMedia.SelfTest   A round-trip check of the vault core.
tools/IconGen           Regenerates the application icon.
```

## Licence

MIT. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
