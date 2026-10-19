using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ipc.Platform;

// Non-authoritative editor input. Windows machine DPAPI permits cross-account
// maintenance; a private file ACL restricts decryption to runtime/maintenance.
public static class ProtectedDraftPayload
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, "Importer PC editor draft v1", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 5, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot protect/read local configuration draft");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    public static string Read(string path)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Draft quota exceeded");
        var bytes = File.ReadAllBytes(path);
        return System.Text.Encoding.UTF8.GetString(OperatingSystem.IsWindows() ? Transform(bytes, false) : bytes);
    }
    public static void Write(string path, string value, bool portableSynthetic, string? runtimeSid = null)
    {
        if (!OperatingSystem.IsWindows() && !portableSynthetic) throw new InvalidOperationException("Protected Production drafts require Windows");
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Draft quota exceeded");
        var temporary = path + "." + Wire.Id() + ".new";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(identity.User!);
                foreach (var sid in new[] { identity.User!.Value, runtimeSid, "S-1-5-18", "S-1-5-32-544" }.OfType<string>().Distinct())
                    acl.AddAccessRule(new(new SecurityIdentifier(sid), FileSystemRights.FullControl, AccessControlType.Allow));
                using var output = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, acl);
                output.Write(Transform(bytes, true)); output.Flush(true);
            }
            else { using var output = new FileStream(temporary, FileMode.CreateNew); output.Write(bytes); output.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
