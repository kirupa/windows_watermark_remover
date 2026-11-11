# Windows Watermark Remover

> [!WARNING]
> ⚠️ **Unsupported. A vibe-coded experiment. Use at your own risk.**
>
> 🧪 This is not an official Microsoft tool, and it has no support or guarantee.
> It changes protected Windows resource files. Review the source and try it on
> a noncritical machine before using it anywhere important.

A small, reversible Windows 11 utility that replaces the desktop's **Windows name**
and **build/version watermark** with single spaces.

**Great for cleaner screen captures, tutorials, demos, and recordings** when you
do not want the Windows edition and build information in the bottom-right corner.

## Before and after

Real screenshots of the same desktop area. No wallpaper edits or overlays.

**Before:** the Windows name, Insider Preview label, evaluation-copy text, and build number are visible.

![Before: Windows 11 Enterprise Insider Preview and its evaluation/build watermark in the bottom-right desktop corner.](docs/before.png)

**After:** the same area, without the Windows name or build watermark.

![After: the same desktop area with the Windows name and build watermark removed.](docs/after.png)

## Download and run

1. Download [**DesktopWatermarkBlanker.exe**](https://github.com/kirupa/windows_watermark_remover/releases/download/v1.0.0/DesktopWatermarkBlanker.exe) from the [experimental release](https://github.com/kirupa/windows_watermark_remover/releases/tag/v1.0.0), or download and extract the ZIP.
2. Double-click it and approve the Windows administrator prompt.
3. Choose **1**, read the warning, and type **y** to continue.
4. Restart Windows Explorer when prompted, or sign out and back in.

**Windows 11 only.** The executable is AnyCPU, intended for x64 and ARM64 Windows,
and uses the .NET Framework included with Windows 11. No installer, external
packages, or network connection is needed.

⚠️ **The executable is unsigned.** SmartScreen or organization policy may warn or
block it. Do not disable your security controls; inspect the source and respect
your organization's device policies.

## Restore

Run the same executable and choose **2** to restore its original backups.
Choose **3** to view status without changing Windows files.

Backups are stored under `%ProgramData%\DesktopWatermarkBlanker`, restricted to
administrators and SYSTEM. Keep them if you want to restore the change.
The tool never overwrites an existing active backup. After restoring, the old
backup manifest and original files are retained for recovery.

It **cannot restore changes made before it created its own backups**. Restore
also refuses to overwrite a resource changed by Windows Update or another tool:
an old backup should not replace a newer system file.

## What this does—and does not do

- Replaces the build-watermark format and the active Windows edition-name resource strings with exactly one ordinary space, `U+0020`.
- Backs up original files, verifies hashes, and restores original file ownership and access permissions.
- Handles the current Windows UI/install language and the English fallback when recognized. Unsupported edition/language formats are refused before changing Windows files.
- Does **not** activate Windows, change licensing, change the real build number, or remove activation notices.
- Does **not** disable Test Mode or Safe Mode warnings.

⚠️ **Important side effects:** Windows dialogs that share these branding
resources can also show a blank edition name. The modified resource files'
Microsoft signatures no longer validate. Windows Update, repair tools, or a
language change can restore the watermark. Restarting Explorer closes Explorer
windows and briefly hides the desktop/taskbar.

In-use resource copies with a `.DWB-*.bak` suffix are deliberately retained
beside the replaced files for recovery. There is no background service or
scheduled task to keep reapplying the changes.

## Command line

Run from an elevated terminal:

```powershell
.\DesktopWatermarkBlanker.exe --apply
.\DesktopWatermarkBlanker.exe --restore
.\DesktopWatermarkBlanker.exe --status
```

`--yes` skips the confirmation. `--restart-explorer` explicitly refreshes the
desktop; without it, sign out and back in later.

```powershell
.\DesktopWatermarkBlanker.exe --apply --yes --restart-explorer
```

Exit code `0` means success or cancellation. Exit code `1` means an error.

## Build from source

The source and administrator-request manifest are in [`Source`](Source).
No NuGet dependencies or SDK installation are required.

From the repository directory:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:anycpu /optimize+ /warnaserror+ /win32manifest:Source\app.manifest /out:DesktopWatermarkBlanker.exe Source\DesktopWatermarkBlanker.cs
```

Then run the self-tests from an elevated terminal:

```powershell
.\DesktopWatermarkBlanker.exe --self-test
```

The self-tests exercise synthetic resource files, exact single-space output,
preservation of unrelated strings, edition matching, malformed/truncated inputs,
file replacement/restore, locked-file failure, and permission restoration.
They do not change Windows files. Optionally pass original `shell32.dll.mui`
and `basebrd.dll.mui` copies to exercise their resource layouts too:

```powershell
.\DesktopWatermarkBlanker.exe --self-test original-shell32.mui original-basebrd.mui
```

🧪 **Experimental, not broadly certified.** End-to-end apply/restore was verified
on Windows 11 Enterprise Insider Preview **26310.28179, ARM64, en-US**.
The x64 architecture has not been tested. Do not assume every Windows build,
edition, or language is compatible.
