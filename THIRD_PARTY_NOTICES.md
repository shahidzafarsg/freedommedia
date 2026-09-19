# Third party notices

FreedomMedia is distributed under the MIT licence. It builds on the following third party
components, each under its own licence.

## Avalonia
The cross-platform user-interface framework, including Avalonia.Controls.DataGrid and the
Inter font package.
Licence: MIT. https://github.com/AvaloniaUI/Avalonia

## LibVLCSharp and LibVLC (VideoLAN)
Used to decode and play video inside the application. The native LibVLC libraries for Windows
and macOS are the official VideoLAN builds, redistributed through the VideoLAN.LibVLC.Windows
and VideoLAN.LibVLC.Mac packages.
Licence: LGPL-2.1-or-later. https://www.videolan.org and https://code.videolan.org/videolan/LibVLCSharp

LibVLC is linked dynamically. In keeping with the LGPL, the LibVLC libraries shipped alongside
FreedomMedia may be replaced by a compatible build of your own; they are ordinary shared
libraries in the application folder (Windows) or the application bundle (macOS).

## Konscious.Security.Cryptography.Argon2
The Argon2id key-derivation implementation used to turn a passphrase into an encryption key.
Licence: MIT. https://github.com/kmaragon/Konscious.Security.Cryptography

## Inter typeface
Used for the application's text.
Licence: SIL Open Font License 1.1. https://rsms.me/inter/

All other cryptography (AES-256-GCM, HKDF-SHA256, SHA-256 and the secure random number
generator) is provided by the .NET base class library.
