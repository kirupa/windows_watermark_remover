using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml.Serialization;

[assembly: System.Reflection.AssemblyTitle("Desktop Watermark Blanker")]
[assembly: System.Reflection.AssemblyDescription("Unsupported, vibe-coded Windows 11 watermark experiment. Use at your own risk.")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

[Serializable]
public sealed class BackupEntry
{
    public string Target, Backup, OriginalHash, PatchedHash, Security, Displaced;
}

[Serializable]
public sealed class BackupManifest
{
    public string Build;
    public List<BackupEntry> Files = new List<BackupEntry>();
}

internal sealed class StringBlock
{
    public int Offset, Size, FirstId;
    public List<string> Strings = new List<string>();
}

internal sealed class ResourceFile
{
    public readonly byte[] Bytes;
    public readonly List<StringBlock> Blocks = new List<StringBlock>();
    private int root, sections, count;

    public ResourceFile(byte[] bytes)
    {
        Bytes = (byte[])bytes.Clone();
        if (U16(0) != 0x5a4d) throw new InvalidDataException("Not a Windows resource file.");
        int pe = Checked(U32(0x3c));
        if (U32(pe) != 0x4550) throw new InvalidDataException("Invalid PE signature.");
        count = U16(pe + 6);
        int optional = pe + 24, magic = U16(optional);
        if (magic != 0x10b && magic != 0x20b) throw new InvalidDataException("Unsupported PE format.");
        sections = optional + U16(pe + 20);
        root = Rva(U32(optional + (magic == 0x20b ? 112 : 96) + 16));
        int type = Directory(0, 6);
        foreach (KeyValuePair<uint, uint> block in Entries(type))
        {
            if ((block.Key & 0x80000000) != 0) continue;
            if (block.Key == 0 || block.Key > 4096) throw new InvalidDataException("Invalid string block.");
            int directory = Subdirectory(block.Value);
            foreach (KeyValuePair<uint, uint> language in Entries(directory))
            {
                if ((language.Value & 0x80000000) != 0) throw new InvalidDataException("Invalid language entry.");
                int entry = root + Checked(language.Value);
                int start = Rva(U32(entry)), size = Checked(U32(entry + 4));
                Range(start, size);
                StringBlock result = new StringBlock { Offset = start, Size = size, FirstId = ((int)block.Key - 1) * 16 };
                int cursor = start;
                for (int i = 0; i < 16; i++)
                {
                    if (cursor + 2 > start + size) throw new InvalidDataException("Truncated string table.");
                    int length = U16(cursor) * 2;
                    cursor += 2;
                    if (cursor + length > start + size) throw new InvalidDataException("Truncated resource string.");
                    result.Strings.Add(Encoding.Unicode.GetString(Bytes, cursor, length));
                    cursor += length;
                }
                Blocks.Add(result);
            }
        }
    }

    private void Range(int offset, int size)
    {
        if (offset < 0 || size < 0 || offset > Bytes.Length - size) throw new InvalidDataException("Resource is out of bounds.");
    }
    private static int Checked(uint value)
    {
        if (value > Int32.MaxValue) throw new InvalidDataException("Resource offset is too large.");
        return (int)value;
    }
    private ushort U16(int offset) { Range(offset, 2); return BitConverter.ToUInt16(Bytes, offset); }
    private uint U32(int offset) { Range(offset, 4); return BitConverter.ToUInt32(Bytes, offset); }
    private int Rva(uint address)
    {
        for (int i = 0; i < count; i++)
        {
            int section = sections + i * 40;
            uint size = Math.Max(U32(section + 8), U32(section + 16)), start = U32(section + 12);
            if (address >= start && (ulong)address - start < size)
            {
                ulong result = (ulong)U32(section + 20) + address - start;
                if (result > Int32.MaxValue) throw new InvalidDataException("Resource offset is too large.");
                return (int)result;
            }
        }
        throw new InvalidDataException("Unmapped resource address.");
    }
    private List<KeyValuePair<uint, uint>> Entries(int relative)
    {
        int start = root + relative;
        int number = U16(start + 12) + U16(start + 14);
        Range(start + 16, number * 8);
        List<KeyValuePair<uint, uint>> entries = new List<KeyValuePair<uint, uint>>();
        for (int i = 0; i < number; i++)
            entries.Add(new KeyValuePair<uint, uint>(U32(start + 16 + i * 8), U32(start + 20 + i * 8)));
        return entries;
    }
    private static int Subdirectory(uint target)
    {
        if ((target & 0x80000000) == 0) throw new InvalidDataException("Missing resource directory.");
        return Checked(target & 0x7fffffff);
    }
    private int Directory(int relative, uint id)
    {
        List<KeyValuePair<uint, uint>> matches = Entries(relative).Where(e => e.Key == id).ToList();
        if (matches.Count != 1) throw new InvalidDataException("Required string resources are missing.");
        return Subdirectory(matches[0].Value);
    }
    public List<string> Values(int id)
    {
        return Blocks.Where(b => id >= b.FirstId && id < b.FirstId + 16)
            .Select(b => b.Strings[id - b.FirstId]).ToList();
    }
    public void Blank(IEnumerable<int> identifiers)
    {
        HashSet<int> ids = new HashSet<int>(identifiers);
        foreach (int id in ids)
            if (Values(id).Count == 0) throw new InvalidDataException("Missing string " + id + ".");
        foreach (StringBlock block in Blocks)
        {
            if (!ids.Any(id => id >= block.FirstId && id < block.FirstId + 16)) continue;
            using (MemoryStream memory = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memory, Encoding.Unicode))
            {
                for (int i = 0; i < 16; i++)
                {
                    string text = ids.Contains(block.FirstId + i) ? " " : block.Strings[i];
                    byte[] encoded = Encoding.Unicode.GetBytes(text);
                    writer.Write((ushort)text.Length);
                    writer.Write(encoded);
                }
                if (memory.Length > block.Size) throw new InvalidDataException("Replacement exceeds resource size.");
                Array.Clear(Bytes, block.Offset, block.Size);
                Array.Copy(memory.ToArray(), 0, Bytes, block.Offset, memory.Length);
            }
        }
    }
}

internal static class Program
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DesktopWatermarkBlanker");
    private static readonly string ManifestPath = Path.Combine(Root, "backup.xml");
    private static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static bool interactive;

    [DllImport("winbrand.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr BrandingFormatString(string format);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr pointer);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disable, ref TokenPrivileges privileges, int size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges { public uint Count; public long Luid; public uint Attributes; }

    private static int Main(string[] args)
    {
        interactive = args.Length == 0;
        int exitCode;
        try
        {
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(false, @"Global\DesktopWatermarkBlanker"))
            {
                if (!mutex.WaitOne(0)) throw new InvalidOperationException("Another copy of this tool is already running.");
                try { exitCode = Run(args); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("ERROR: " + error.Message);
            Console.Error.WriteLine("No success is claimed. Keep the backups in " + Root + ".");
            exitCode = 1;
        }
        if (interactive) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
        return exitCode;
    }

    private static int Run(string[] args)
    {
        Console.Title = "Desktop Watermark Blanker";
        if (args.Length > 0 && args[0] == "--self-test") { SelfTest(args.Skip(1).ToArray()); return 0; }
        if (args.Length > 0 && args[0] == "--help") { Help(); return 0; }
        string command = args.Length == 0 ? "" : args[0];
        if (args.Skip(1).Any(a => a != "--yes" && a != "--restart-explorer")) throw new ArgumentException("Unknown option. Use --help.");
        if (command != "" && command != "--apply" && command != "--restore" && command != "--status") throw new ArgumentException("Unknown command. Use --help.");
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Run this executable as administrator.");
        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            int build;
            if (key == null || !Int32.TryParse(Convert.ToString(key.GetValue("CurrentBuild")), out build) || build < 22000)
                throw new NotSupportedException("This tool supports Windows 11 only.");
        }
        if (interactive)
        {
            Console.WriteLine("Desktop Watermark Blanker - Windows 11\n");
            Console.WriteLine("1  Replace the Windows name and build watermark with spaces");
            Console.WriteLine("2  Restore originals saved by this tool");
            Console.WriteLine("3  Show status\n");
            Console.Write("Select 1, 2, or 3 (anything else cancels): ");
            string choice = Console.ReadLine();
            command = choice == "1" ? "--apply" : choice == "2" ? "--restore" : choice == "3" ? "--status" : "";
            if (command == "") { Console.WriteLine("Cancelled. No changes made."); return 0; }
        }
        if (command == "--status") { Status(); return 0; }
        Console.WriteLine("\nWARNING: Unsupported, vibe-coded experiment. Use at your own risk.");
        Console.WriteLine("This modifies protected Windows resource files, not Windows activation.");
        Console.WriteLine("The Windows edition name can also become blank in other Windows dialogs.");
        Console.WriteLine("Windows updates, language changes, and repair tools may restore the watermark.");
        if (!args.Contains("--yes") && !Confirm("Continue?"))
        {
            Console.WriteLine("Cancelled. No changes made.");
            return 0;
        }
        EnablePrivilege("SeTakeOwnershipPrivilege");
        EnablePrivilege("SeRestorePrivilege");
        bool changed = command == "--apply" ? Apply() : Restore();
        if (changed)
        {
            Console.WriteLine("Changes saved. Backups: " + Root);
            bool restart = args.Contains("--restart-explorer") || (interactive && Confirm("Restart Windows Explorer now? Taskbar and Explorer windows will close briefly."));
            if (restart) RestartExplorer();
            else Console.WriteLine("Sign out and back in, or restart Windows Explorer, to refresh the desktop.");
        }
        return 0;
    }

    private static void EnablePrivilege(string name)
    {
        IntPtr token;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out token)) throw new System.ComponentModel.Win32Exception();
        try
        {
            long luid;
            if (!LookupPrivilegeValue(null, name, out luid)) throw new System.ComponentModel.Win32Exception();
            TokenPrivileges privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot enable " + name + ".");
        }
        finally { CloseHandle(token); }
    }

    private static string Build()
    {
        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            return Convert.ToString(key.GetValue("CurrentBuild")) + "." + Convert.ToString(key.GetValue("UBR"));
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    private static bool Confirm(string message)
    {
        Console.Write(message + " [y/N] ");
        return String.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase);
    }
    private static void PrepareRoot()
    {
        if (Directory.Exists(Root) && (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backup directory cannot be a symbolic link.");
        Directory.CreateDirectory(Root);
        DirectorySecurity security = new DirectorySecurity();
        security.SetOwner(Admins);
        security.SetAccessRuleProtection(true, false);
        InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        Directory.SetAccessControl(Root, security);
    }
    private static List<string> Targets()
    {
        HashSet<string> cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in new[] { CultureInfo.CurrentUICulture, CultureInfo.InstalledUICulture })
            for (CultureInfo current = culture; current.Name.Length != 0; current = current.Parent) cultures.Add(current.Name);
        cultures.Add("en-US");
        List<string> files = new List<string>();
        foreach (string culture in cultures)
            foreach (string relative in new[] { Path.Combine("System32", culture, "shell32.dll.mui"), Path.Combine("Branding", "Basebrd", culture, "basebrd.dll.mui") })
            {
                string path = Path.Combine(Windows, relative);
                if (File.Exists(path)) files.Add(path);
            }
        if (!files.Any(p => Path.GetFileName(p) == "shell32.dll.mui") || !files.Any(p => Path.GetFileName(p) == "basebrd.dll.mui"))
            throw new NotSupportedException("Expected Windows language resources were not found.");
        return files;
    }
    private static string Brand()
    {
        IntPtr pointer = BrandingFormatString("%WINDOWS_LONG%");
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Windows branding could not be read.");
        try { return Marshal.PtrToStringUni(pointer); }
        finally { GlobalFree(pointer); }
    }
    private static List<int> BrandIds(ResourceFile resource, string brand)
    {
        List<KeyValuePair<int, string>> matches = new List<KeyValuePair<int, string>>();
        foreach (StringBlock block in resource.Blocks)
            for (int i = 0; i < block.Strings.Count; i++)
            {
                string template = block.Strings[i];
                if (!template.StartsWith("Windows ", StringComparison.Ordinal) || !template.EndsWith("%1%2", StringComparison.Ordinal)) continue;
                string name = template.Substring(0, template.Length - 4);
                if (brand.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    matches.Add(new KeyValuePair<int, string>(block.FirstId + i, name));
            }
        if (matches.Count == 0) throw new NotSupportedException("This Windows edition/language branding is not recognized. No files have been changed.");
        int longest = matches.Max(m => m.Value.Length);
        return matches.Where(m => m.Value.Length == longest).Select(m => m.Key).Distinct().ToList();
    }

    private static bool Apply()
    {
        if (File.Exists(ManifestPath)) throw new InvalidOperationException("A previous backup exists. Use Restore before applying again; backups are never overwritten.");
        string brand = Brand();
        if (String.IsNullOrWhiteSpace(brand))
        {
            if (Targets().Where(p => Path.GetFileName(p) == "shell32.dll.mui")
                .All(p => new ResourceFile(File.ReadAllBytes(p)).Values(33108).Count > 0 &&
                    new ResourceFile(File.ReadAllBytes(p)).Values(33108).All(v => v == " ")))
            {
                Console.WriteLine("The name and build resources are already blank. No changes made; this tool has no originals to restore.");
                return false;
            }
            throw new NotSupportedException("Windows branding is already modified. Restore its original resources before using this tool.");
        }
        List<KeyValuePair<string, byte[]>> patches = new List<KeyValuePair<string, byte[]>>();
        foreach (string target in Targets())
        {
            ResourceFile resource = new ResourceFile(File.ReadAllBytes(target));
            if (Path.GetFileName(target) == "shell32.dll.mui")
            {
                List<string> formats = resource.Values(33108);
                if (formats.Count == 0 || formats.Any(v => v != " " && !v.Contains("%ws")))
                    throw new NotSupportedException("The build watermark format is not recognized. No files have been changed.");
                if (formats.All(v => v == " ")) continue;
                resource.Blank(new[] { 33108 });
            }
            else resource.Blank(BrandIds(resource, brand));
            patches.Add(new KeyValuePair<string, byte[]>(target, resource.Bytes));
        }
        PrepareRoot();
        BackupManifest manifest = new BackupManifest { Build = Build() };
        string operation = Guid.NewGuid().ToString("N");
        foreach (KeyValuePair<string, byte[]> patch in patches)
        {
            byte[] original = File.ReadAllBytes(patch.Key);
            string backup = operation + "-" + manifest.Files.Count.ToString(CultureInfo.InvariantCulture) + ".original";
            using (FileStream output = new FileStream(Path.Combine(Root, backup), FileMode.CreateNew))
                output.Write(original, 0, original.Length);
            manifest.Files.Add(new BackupEntry { Target = patch.Key, Backup = backup, OriginalHash = Hash(original), PatchedHash = Hash(patch.Value),
                Security = File.GetAccessControl(patch.Key).GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group),
                Displaced = patch.Key + ".DWB-" + operation + ".bak" });
        }
        using (FileStream output = new FileStream(ManifestPath, FileMode.CreateNew))
            new XmlSerializer(typeof(BackupManifest)).Serialize(output, manifest);
        try
        {
            for (int i = 0; i < patches.Count; i++)
            {
                if (Hash(File.ReadAllBytes(manifest.Files[i].Target)) != manifest.Files[i].OriginalHash)
                    throw new IOException("A Windows resource changed during preparation.");
                Replace(manifest.Files[i], patches[i].Value, manifest.Files[i].Displaced);
                if (Hash(File.ReadAllBytes(manifest.Files[i].Target)) != manifest.Files[i].PatchedHash)
                    throw new IOException("Installed resource verification failed.");
            }
        }
        catch (Exception installError)
        {
            try { Restore(); }
            catch (Exception rollbackError) { throw new IOException("Install failed: " + installError.Message + " Rollback also failed: " + rollbackError.Message + " Run --restore.", rollbackError); }
            throw new IOException("Install failed; originals were restored: " + installError.Message, installError);
        }
        Console.WriteLine("Windows name and build watermark resources replaced with single spaces.");
        return true;
    }

    private static BackupManifest ReadManifest()
    {
        if (!File.Exists(ManifestPath)) throw new FileNotFoundException("No backup created by this tool exists.");
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(ManifestPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backup paths cannot be symbolic links.");
        BackupManifest manifest;
        using (FileStream input = File.OpenRead(ManifestPath))
            manifest = (BackupManifest)new XmlSerializer(typeof(BackupManifest)).Deserialize(input);
        if (manifest == null || manifest.Files == null || manifest.Files.Count == 0) throw new InvalidDataException("Backup manifest is empty.");
        HashSet<string> allowed = new HashSet<string>(Targets(), StringComparer.OrdinalIgnoreCase);
        foreach (BackupEntry entry in manifest.Files)
        {
            if (!allowed.Contains(entry.Target) || entry.Backup != Path.GetFileName(entry.Backup) ||
                String.IsNullOrEmpty(entry.Backup) || !entry.Backup.EndsWith(".original", StringComparison.Ordinal) ||
                entry.Displaced == null || !entry.Displaced.StartsWith(entry.Target + ".DWB-", StringComparison.OrdinalIgnoreCase) ||
                !entry.Displaced.EndsWith(".bak", StringComparison.Ordinal) || entry.Displaced.IndexOfAny(new[] { '\\', '/' }, entry.Target.Length) >= 0)
                throw new InvalidDataException("Unsafe backup manifest entry.");
            string backup = Path.Combine(Root, entry.Backup);
            if ((File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0 || Hash(File.ReadAllBytes(backup)) != entry.OriginalHash)
                throw new InvalidDataException("Backup integrity verification failed.");
        }
        return manifest;
    }
    private static bool Restore()
    {
        BackupManifest manifest = ReadManifest();
        foreach (BackupEntry entry in manifest.Files)
            if (File.Exists(entry.Target))
            {
                string current = Hash(File.ReadAllBytes(entry.Target));
                if (current != entry.OriginalHash && current != entry.PatchedHash)
                    throw new IOException("A Windows update or another tool changed " + entry.Target + ". Refusing to overwrite it with an older backup.");
            }
        foreach (BackupEntry entry in manifest.Files)
        {
            if (!File.Exists(entry.Target) || Hash(File.ReadAllBytes(entry.Target)) != entry.OriginalHash)
                Replace(entry, File.ReadAllBytes(Path.Combine(Root, entry.Backup)), entry.Target + ".DWB-" + Guid.NewGuid().ToString("N") + ".bak");
            if (Hash(File.ReadAllBytes(entry.Target)) != entry.OriginalHash) throw new IOException("Restore verification failed.");
        }
        string archive = Path.Combine(Root, "restored-" + Guid.NewGuid().ToString("N") + ".xml");
        File.Move(ManifestPath, archive);
        Console.WriteLine("Original resource files restored. Archived backup manifest: " + archive);
        return true;
    }

    private static void Replace(BackupEntry entry, byte[] replacement, string displaced)
    {
        FileSecurity security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm(entry.Security);
        bool moved = false;
        bool originalPresent = File.Exists(entry.Target);
        try
        {
            if (originalPresent)
            {
                Grant(entry.Target);
                File.Move(entry.Target, displaced);
                moved = true;
            }
            File.WriteAllBytes(entry.Target, replacement);
            File.SetAccessControl(entry.Target, security);
        }
        catch
        {
            if (moved)
            {
                if (File.Exists(entry.Target)) { Grant(entry.Target); File.Delete(entry.Target); }
                File.Move(displaced, entry.Target);
            }
            else if (!originalPresent && File.Exists(entry.Target))
            {
                Grant(entry.Target);
                File.Delete(entry.Target);
            }
            throw;
        }
        finally
        {
            if (File.Exists(entry.Target)) File.SetAccessControl(entry.Target, security);
            if (File.Exists(displaced)) File.SetAccessControl(displaced, security);
        }
    }
    private static void Grant(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Resource cannot be a symbolic link.");
        FileSecurity security = File.GetAccessControl(path);
        security.SetOwner(Admins);
        File.SetAccessControl(path, security);
        security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(path, security);
    }
    private static void Status()
    {
        string brand = Brand();
        Console.WriteLine("Windows branding: " + (String.IsNullOrWhiteSpace(brand) ? "(single space / blank)" : brand));
        foreach (string path in Targets().Where(p => Path.GetFileName(p) == "shell32.dll.mui"))
        {
            List<string> values = new ResourceFile(File.ReadAllBytes(path)).Values(33108);
            Console.WriteLine(path + ": " + (values.Count > 0 && values.All(v => v == " ") ? "single space" : "not blank"));
        }
        Console.WriteLine("Managed backup: " + (File.Exists(ManifestPath) ? ManifestPath : "none"));
    }
    private static void RestartExplorer()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (Process explorer in Process.GetProcessesByName("explorer"))
            using (explorer) { if (explorer.SessionId == session) explorer.Kill(); }
        System.Threading.Thread.Sleep(5000);
        bool running = false;
        foreach (Process explorer in Process.GetProcessesByName("explorer"))
            using (explorer) { if (explorer.SessionId == session) running = true; }
        if (!running) throw new InvalidOperationException("Resources were saved, but Explorer did not restart automatically. Sign out and back in.");
        Console.WriteLine("Windows Explorer restarted.");
    }
    private static void Help()
    {
        Console.WriteLine("DesktopWatermarkBlanker.exe                  Interactive menu");
        Console.WriteLine("DesktopWatermarkBlanker.exe --apply [--yes] [--restart-explorer]");
        Console.WriteLine("DesktopWatermarkBlanker.exe --restore [--yes] [--restart-explorer]");
        Console.WriteLine("DesktopWatermarkBlanker.exe --status");
        Console.WriteLine("DesktopWatermarkBlanker.exe --self-test [original-shell32.mui original-basebrd.mui]");
        Console.WriteLine("Windows 11; administrator rights; .NET Framework 4.8 (included with Windows 11).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Self-test failed: " + message);
    }
    private static void SelfTest(string[] fixtures)
    {
        if (fixtures.Length != 0 && fixtures.Length != 2) throw new ArgumentException("Pass both original resource files, or neither.");
        byte[] shellFixture = Fixture(new Dictionary<int, string> { {33108, "%ws Build %ws"}, {33109, "Evaluation copy."} });
        byte[] brandFixture = Fixture(new Dictionary<int, string> {
            {314, "Windows 11 Enterprise%1%2"}, {315, "Windows 11 Enterprise%1%2"},
            {332, "Windows 11 Pro%1%2"}, {333, "Windows 11 Pro%1%2"},
            {358, "Windows 11 Pro for Workstations%1%2"}, {359, "Windows 11 Pro for Workstations%1%2"} });
        bool rejected = false;
        try { new ResourceFile(new byte[64]); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "malformed files must be rejected");
        foreach (byte[][] pair in fixtures.Length == 2
            ? new[] { new[] { shellFixture, brandFixture }, new[] { File.ReadAllBytes(fixtures[0]), File.ReadAllBytes(fixtures[1]) } }
            : new[] { new[] { shellFixture, brandFixture } })
        {
            CheckPatch(pair[0], new[] { 33108 });
            ResourceFile brand = new ResourceFile(pair[1]);
            List<int> selected = BrandIds(brand, "Windows 11 Enterprise Insider Preview");
            Assert(selected.Count == 2 && selected.Contains(314) && selected.Contains(315), "edition selection");
            CheckPatch(pair[1], selected);
            List<int> pro = BrandIds(brand, "Windows 11 Pro for Workstations Insider Preview");
            Assert(pro.Contains(358) && pro.Contains(359) && !pro.Contains(332), "longest edition name wins");
            rejected = false;
            try { brand.Blank(new[] { 65535 }); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "missing resources must be rejected");
            rejected = false;
            try { BrandIds(brand, "Unrecognized Windows Edition"); }
            catch (NotSupportedException) { rejected = true; }
            Assert(rejected, "unknown editions must be rejected");
            rejected = false;
            try { new ResourceFile(pair[0].Take(200).ToArray()); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "truncated resources must be rejected");
            ReplacementTest(pair[0]);
        }
        Console.WriteLine("Self-tests passed: resource round trips, edition matching, malformed inputs, file replacement, restore, locked files, and permissions.");
        Console.WriteLine("No Windows files were modified.");
    }
    private static void CheckPatch(byte[] original, IEnumerable<int> identifiers)
    {
        HashSet<int> ids = new HashSet<int>(identifiers);
        ResourceFile before = new ResourceFile(original), patched = new ResourceFile(original);
        patched.Blank(ids);
        ResourceFile after = new ResourceFile(patched.Bytes);
        Assert(original.Length == patched.Bytes.Length && before.Blocks.Count == after.Blocks.Count, "resource structure preserved");
        for (int b = 0; b < before.Blocks.Count; b++)
            for (int i = 0; i < 16; i++)
                Assert(after.Blocks[b].Strings[i] == (ids.Contains(before.Blocks[b].FirstId + i) ? " " : before.Blocks[b].Strings[i]), "only requested strings change");
        foreach (int id in ids) Assert(after.Values(id).All(v => v.Length == 1 && v[0] == ' '), "exactly one U+0020");
    }
    private static void ReplacementTest(byte[] original)
    {
        EnablePrivilege("SeTakeOwnershipPrivilege");
        EnablePrivilege("SeRestorePrivilege");
        string directory = Path.Combine(Path.GetTempPath(), "DWB-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "fixture.mui");
        try
        {
            File.WriteAllBytes(target, original);
            ResourceFile patched = new ResourceFile(original);
            patched.Blank(new[] { 33108 });
            FileSecurity security = File.GetAccessControl(target);
            BackupEntry entry = new BackupEntry { Target = target, OriginalHash = Hash(original), PatchedHash = Hash(patched.Bytes),
                Security = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group) };
            Replace(entry, patched.Bytes, Path.Combine(directory, "before.bak"));
            Assert(Hash(File.ReadAllBytes(target)) == entry.PatchedHash, "file install");
            Replace(entry, original, Path.Combine(directory, "patched.bak"));
            Assert(Hash(File.ReadAllBytes(target)) == entry.OriginalHash, "file restore");
            bool rejected = false;
            using (FileStream held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { Replace(entry, patched.Bytes, Path.Combine(directory, "blocked.bak")); }
                catch (IOException) { rejected = true; }
            }
            Assert(rejected && Hash(File.ReadAllBytes(target)) == entry.OriginalHash, "locked files must remain unchanged");
            Assert(File.GetAccessControl(target).GetOwner(typeof(SecurityIdentifier)).Equals(security.GetOwner(typeof(SecurityIdentifier))), "ownership restored");
            Assert(File.GetAccessControl(target).GetAccessRules(true, true, typeof(SecurityIdentifier)).Count ==
                security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Count, "permissions restored");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
    private static byte[] Fixture(Dictionary<int, string> values)
    {
        int[] blocks = values.Keys.Select(id => id / 16 + 1).Distinct().OrderBy(id => id).ToArray();
        int languages = 40 + blocks.Length * 8, entries = languages + blocks.Length * 24;
        int payload = entries + blocks.Length * 16;
        byte[] data = new byte[8192];
        using (MemoryStream memory = new MemoryStream(data))
        using (BinaryWriter writer = new BinaryWriter(memory))
        {
            writer.Write((ushort)0x5a4d);
            memory.Position = 0x3c; writer.Write(0x80);
            memory.Position = 0x80; writer.Write(0x4550);
            memory.Position = 0x86; writer.Write((ushort)1);
            memory.Position = 0x94; writer.Write((ushort)240);
            memory.Position = 0x98; writer.Write((ushort)0x20b);
            memory.Position = 0x98 + 112 + 16; writer.Write(0x1000);
            memory.Position = 0x98 + 240 + 8;
            writer.Write(0x1000); writer.Write(0x1000); writer.Write(0x1000); writer.Write(0x400);
            memory.Position = 0x400 + 14; writer.Write((ushort)1);
            writer.Write(6); writer.Write(0x80000018u);
            memory.Position = 0x400 + 24 + 14; writer.Write((ushort)blocks.Length);
            for (int block = 0; block < blocks.Length; block++)
            {
                memory.Position = 0x400 + 40 + block * 8;
                writer.Write(blocks[block]); writer.Write(0x80000000u | (uint)(languages + block * 24));
                memory.Position = 0x400 + languages + block * 24 + 14;
                writer.Write((ushort)1); writer.Write(1033); writer.Write(entries + block * 16);
                int start = payload;
                memory.Position = 0x400 + payload;
                for (int index = 0; index < 16; index++)
                {
                    string value;
                    if (!values.TryGetValue((blocks[block] - 1) * 16 + index, out value)) value = "";
                    byte[] encoded = Encoding.Unicode.GetBytes(value);
                    writer.Write((ushort)value.Length); writer.Write(encoded);
                }
                payload = (int)memory.Position - 0x400;
                memory.Position = 0x400 + entries + block * 16;
                writer.Write(0x1000 + start); writer.Write(payload - start);
            }
        }
        return data;
    }
}
