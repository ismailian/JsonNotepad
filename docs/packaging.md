# Packaging and releasing JsonNotepad

JsonNotepad ships in two forms:

| Format | Audience | Script | Output |
|---|---|---|---|
| **MSIX** (x64 + arm64, optional `.msixbundle`) | Microsoft Store, sideloading | `build/package-msix.ps1` | `artifacts/JsonNotepad_<ver>_<arch>.msix`, `artifacts/JsonNotepad_<ver>.msixbundle` |
| **Inno Setup** `setup.exe` (x64) | GitHub Releases, classic installs | `build/package-inno.ps1` | `artifacts/JsonNotepad_<ver>_x64_setup.exe` |

Both are built from a **self-contained, non-single-file** `dotnet publish` of `src/JsonNotepad.App`
(`artifacts/publish/<rid>`), so users do not need the .NET runtime installed.

All scripts run on Windows PowerShell 5.1 or PowerShell 7+. If script execution is blocked, run them with
`powershell -ExecutionPolicy Bypass -File build\<script>.ps1 ...`.

## Prerequisites

- .NET 9 SDK (`global.json` rolls forward to newer SDKs).
- **MSIX:** Windows 10/11 SDK (provides `makeappx.exe`, `makepri.exe`, `signtool.exe`). The scripts locate the
  newest installed SDK under `Windows Kits\10\bin\<version>\<arch>` automatically and tell you what to install if
  it is missing.
- **Inno Setup:** Inno Setup 6.3+ from <https://jrsoftware.org/isdl.php> (`winget install --id JRSoftware.InnoSetup -e`).

## Versioning

- **Single source of truth:** `<VersionPrefix>` in `Directory.Build.props`, always `Major.Minor.Patch`
  (semantic versioning). Assemblies, the About box, the MSIX manifest and the Inno Setup installer all derive from it.
  Never put a version anywhere else.
- **MSIX needs a 4-part version whose revision is 0.** The packaging script stamps `Major.Minor.Patch.0`
  automatically; the Store reserves the revision field and rejects packages where it is non-zero.
  Every Store submission must have a higher version than the previous one, so bump before each release.
- Pre-release builds can add a suffix for the assemblies only: `dotnet build -p:VersionSuffix=beta.1`
  (MSIX/Inno versions ignore the suffix).
- Bump with the helper (it edits only `VersionPrefix`):

```powershell
./build/version.ps1            # show the current version
./build/version.ps1 patch      # 1.4.2 -> 1.4.3  (bug fixes)
./build/version.ps1 minor      # 1.4.3 -> 1.5.0  (features)
./build/version.ps1 major      # 1.5.0 -> 2.0.0  (breaking changes)
./build/version.ps1 -Set 2.1.0
```

## Build and test

```powershell
./build/build.ps1                    # restore + Release build of JsonNotepad.sln + tests/JsonNotepad.Core.Tests (dotnet run)
./build/build.ps1 -SkipTests
```

Warnings are errors (`TreatWarningsAsErrors` in `Directory.Build.props`).

## Branding assets

The logo is drawn procedurally by `tools/IconGen` (GDI+, no image files checked in by hand). Regenerate everything after
changing the design:

```powershell
./build/generate-icons.ps1
```

It writes:

- `src/JsonNotepad.App/Resources/AppIcon.ico` (16, 20, 24, 32, 40, 48, 64, 128, 256 px; BMP frames below 256, PNG at 256)
- `installer/msix/Assets/*` (Square44x44Logo + `targetsize-*_altform-unplated` variants, Square150x150Logo,
  Wide310x150Logo, StoreLogo, SplashScreen, `SmallTile/Square71x71Logo`, `LargeTile/Square310x310Logo`)
- `docs/images/logo.png` (256 px, used by the README)

Because some assets carry MRT qualifiers (`targetsize-16`, `altform-unplated`), the packaging script also generates a
`resources.pri` with `makepri`. It indexes only the manifest and `Assets/`, not the runtime DLLs.

## MSIX

Package identity is in `installer/msix/package.config.psd1`; the manifest template is
`installer/msix/AppxManifest.template.xml`. The template declares:

- a full-trust desktop app (`Windows.FullTrustApplication` + `runFullTrust`), Windows 10 1809 (10.0.17763) or later
- file type associations for `.json`, `.jsonc`, `.geojson`, `.har`, `.webmanifest` (JsonNotepad appears in *Open with*)
- the app execution alias `jsonnotepad.exe`, so `jsonnotepad file.json` works from any terminal

### Building

```powershell
./build/package-msix.ps1                         # x64 + arm64, unsigned
./build/package-msix.ps1 -Architectures x64      # one architecture
./build/package-msix.ps1 -Bundle                 # + artifacts/JsonNotepad_<ver>.msixbundle (use this for the Store)
./build/package-msix.ps1 -SkipPublish            # re-pack an existing artifacts/publish/<rid>
```

Steps the script performs per architecture: read the version (`dotnet msbuild -getProperty:VersionPrefix`, falling back to
parsing `Directory.Build.props`) -> `dotnet publish -r win-<arch> --self-contained` to `artifacts/publish/win-<arch>` ->
stage `artifacts/msix/win-<arch>` (publish output without `.pdb`, `Assets/`, stamped `AppxManifest.xml`) ->
`makepri new` -> `makeappx pack`. With `-Bundle`, `makeappx bundle` combines all architectures.

Unsigned packages are fine for Store upload but cannot be installed locally.

### Sideloading with a development certificate

```powershell
./build/package-msix.ps1 -Architectures x64 -CreateDevCertificate
```

This creates (or reuses) a self-signed code-signing certificate in `Cert:\CurrentUser\My` whose Subject equals the
`Publisher` in `package.config.psd1`, signs the packages with SHA-256 and exports the public certificate to
`artifacts/JsonNotepad-dev.cer`. Then, once per machine, from an **elevated** PowerShell:

```powershell
Import-Certificate -FilePath artifacts\JsonNotepad-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path artifacts\JsonNotepad_1.0.0_x64.msix     # or double-click the .msix
```

Uninstall from Settings > Apps, or `Get-AppxPackage *JsonNotepad* | Remove-AppxPackage`.

To sign with a real certificate instead (Subject must equal `Publisher` exactly):

```powershell
./build/package-msix.ps1 -Bundle -Sign -CertificatePath C:\keys\jsonnotepad.pfx -CertificatePassword $env:PFX_PASSWORD `
    -TimestampUrl http://timestamp.digicert.com
```

## Microsoft Store submission

1. **Reserve the name** in [Partner Center](https://partner.microsoft.com/dashboard) > Apps and games > New product >
   MSIX or PWA app > reserve "JsonNotepad" (or your chosen name).
2. Open the product > **Product management > Product identity** and copy:
   - `Package/Identity/Name` -> `IdentityName`
   - `Package/Identity/Publisher` (`CN=<GUID>`) -> `Publisher`
   - `Package/Properties/PublisherDisplayName` -> `PublisherDisplayName`

   into `installer/msix/package.config.psd1` (or pass them as `-IdentityName/-Publisher/-PublisherDisplayName`).
   A mismatch makes the upload fail validation.
3. Bump the version (`./build/version.ps1 patch|minor|major`) - every submission needs a higher version.
4. Build the bundle, **unsigned** (the Store signs the package with its own certificate):

   ```powershell
   ./build/package-msix.ps1 -Bundle
   ```

5. Optionally run the Windows App Certification Kit on it locally (`appcert.exe`, installed with the Windows SDK).
6. Create a submission: upload `artifacts/JsonNotepad_<ver>.msixbundle` under **Packages** (a `.msixupload`, which is a
   zip of the bundle plus symbol files, is also accepted but not required). Fill in pricing, properties
   (category: Developer tools), age ratings and the store listing (description, screenshots, `docs/images/logo.png`).
7. `runFullTrust` is a restricted capability: in the submission's notes for certification explain that JsonNotepad is a
   desktop (WinForms) application that opens and saves user-selected files.
8. Submit. After certification the Store re-signs and publishes the package; Store users get updates automatically
   for every later submission with a higher version.

## Inno Setup installer

```powershell
./build/package-inno.ps1                    # publish win-x64 + compile installer/inno/JsonNotepad.iss
./build/package-inno.ps1 -SkipPublish -IsccPath 'D:\Tools\Inno Setup 6\ISCC.exe'
```

The script passes `/DAppVersion=<VersionPrefix> /DSourceDir=<publish dir> /DOutputDir=<artifacts>` to `ISCC.exe`; if
Inno Setup is not installed it stops with download instructions. The installer:

- installs per-user (no admin, `%LOCALAPPDATA%\Programs\JsonNotepad`) by default, or per-machine
  (`Program Files\JsonNotepad`) when chosen in the dialog or with `setup.exe /ALLUSERS`
- x64-compatible Windows 10 1809+ only (also runs on arm64 Windows through x64 emulation)
- creates a Start Menu shortcut, an optional desktop icon, and optionally registers the `JsonNotepad.json` ProgID with
  an *Open with JsonNotepad* verb for `.json` files (Windows 10+ lets the user pick the default app)
- registers `App Paths\JsonNotepad.exe`, removes everything on uninstall and offers to delete user settings
- keeps a fixed `AppId` GUID - never change it, or upgrades will install side by side

Silent install: `JsonNotepad_<ver>_x64_setup.exe /VERYSILENT /CURRENTUSER /TASKS="fileassoc"`.
Silent uninstall: `"%LOCALAPPDATA%\Programs\JsonNotepad\unins000.exe" /VERYSILENT`.

### Signing the setup

When a certificate is given, the script signs `JsonNotepad.exe` before compiling, then signs the finished setup:

```powershell
./build/package-inno.ps1 -UseDevCertificate                      # self-signed dev cert (local testing only)
./build/package-inno.ps1 -CertificateThumbprint <sha1> -TimestampUrl http://timestamp.digicert.com   # store / hardware token
./build/package-inno.ps1 -CertificatePath cert.pfx -CertificatePassword *** -TimestampUrl http://timestamp.digicert.com
```

`-UseDevCertificate` reuses the certificate created by `package-msix.ps1 -CreateDevCertificate`. Windows does not
trust that certificate, so SmartScreen still warns users. A publicly trusted certificate (or Azure Trusted Signing)
is required for public downloads. Always pass `-TimestampUrl` for releases so that signatures stay valid after the
certificate expires. The uninstaller (`unins000.exe`) is not signed. To sign it as well, configure Inno's `SignTool`
directive in `JsonNotepad.iss`.

## Release checklist

1. `git pull` on `main`; working tree clean.
2. Bump the version: `./build/version.ps1 patch|minor|major`; update the changelog / release notes.
3. `./build/build.ps1` - build is warning-free and all tests pass.
4. Smoke-test the app (open, edit, save, search; open a large file; `.jsonc` with comments).
5. `./build/package-msix.ps1 -Architectures x64 -CreateDevCertificate`; install the `.msix`, check the Start menu
   icon, *Open with* for `.json`, and `jsonnotepad some.json` from a new terminal. Uninstall.
6. `./build/package-inno.ps1`; install, test the file association and uninstall.
7. Commit (`Bump version to x.y.z`), tag `vx.y.z`, push the tag.
8. GitHub Release: attach `JsonNotepad_<ver>_x64_setup.exe` (and optionally the dev-signed `.msix` + `.cer` for testers).
9. Store: `./build/package-msix.ps1 -Bundle` with the Partner Center identity, upload the `.msixbundle`, submit.
10. After publication, verify the Store listing installs and updates from the previous version.
