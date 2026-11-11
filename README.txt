Desktop Watermark Blanker
========================

WARNING: Unsupported. A vibe-coded experiment. Use at your own risk.
This is not an official Microsoft tool. No support or guarantee is provided.
Great for cleaner screen captures, tutorials, demos, and recordings.

Windows 11 only. Requires administrator approval. Uses the .NET Framework
included with Windows 11. One AnyCPU executable for x64 and ARM64 Windows.
No downloads, installation, network access, or third-party dependencies.

Double-click DesktopWatermarkBlanker.exe, accept the Windows administrator
prompt, select 1, and confirm with y. Optionally restart Windows Explorer
when prompted, or sign out and back in later.

Select 2 to restore files previously backed up by this executable.
Select 3 to inspect status without modifying Windows files.

This replaces the Windows edition name and the build-watermark format with
exactly one ordinary space (U+0020) in Windows language resource files.
It does NOT activate Windows, change licensing, remove activation notices,
change the actual build number, or disable Test Mode or Safe Mode warnings.

Important limitations
---------------------
- This is an unsigned utility. Windows SmartScreen or organization policy
  may warn or block it. Review the included source; do not disable security
  controls to run it. Respect your organization's device policy.
- Modifies protected, Microsoft-signed resource files; their original
  signatures will no longer validate. Windows repair tools and updates
  can restore them. The Windows edition name can also be blank in other
  dialogs that share these branding resources.
- Edition/language formats that are not recognized are refused before any
  Windows file is changed. Not every future Windows build is guaranteed.
- Handles the current UI/install language and en-US fallback. A subsequent
  language change or feature update can restore the displayed text.
- Restarting Explorer closes Explorer windows and briefly hides the taskbar.
- Original ownership and access permissions are restored after replacement.
- Backups are retained in C:\ProgramData\DesktopWatermarkBlanker (or the
  system's configured ProgramData folder), restricted to administrators
  and SYSTEM. Do not delete these backups if you want to restore.
- In-use displaced resources stay beside the original with a .DWB-*.bak
  suffix. They are intentionally retained for recovery, not overwritten.
- Restore refuses to overwrite a file changed by Windows Update or another
  utility, preventing an old backup from replacing a newer system file.
- It cannot restore changes made before this utility created its backups.
- Test on a noncritical machine first. Only the build and editions covered
  by the supplied verification have been tested, not every Windows system.

Command line
------------
DesktopWatermarkBlanker.exe --apply [--yes] [--restart-explorer]
DesktopWatermarkBlanker.exe --restore [--yes] [--restart-explorer]
DesktopWatermarkBlanker.exe --status
DesktopWatermarkBlanker.exe --help
DesktopWatermarkBlanker.exe --self-test
DesktopWatermarkBlanker.exe --self-test original-shell32.mui original-basebrd.mui

--yes skips the confirmation. Explorer is restarted only when explicitly
requested; otherwise sign out/back in to apply the visible change.
Exit code 0 indicates success or cancellation; 1 indicates an error.

Build from source
-----------------
From this directory in an elevated PowerShell:

& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:anycpu /optimize+ /win32manifest:Source\app.manifest /out:DesktopWatermarkBlanker.exe Source\DesktopWatermarkBlanker.cs

Source is included in the Source directory. The package does not include
Microsoft DLLs or another machine's original resource files.
