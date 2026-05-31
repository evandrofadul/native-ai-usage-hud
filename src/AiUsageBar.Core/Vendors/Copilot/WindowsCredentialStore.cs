using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AiUsageBar.Core.Vendors.Copilot;

/// <summary>
/// Thin P/Invoke wrapper over the Windows Credential Manager (advapi32 <c>CredRead</c>
/// / <c>CredEnumerate</c>). The GitHub Copilot CLI stores its OAuth token here rather
/// than in a plaintext file, so this is how we locate it. Returns the raw secret
/// bytes — the caller decides how to decode them (the store keeps no encoding hint).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCredentialStore
{
    private const int CRED_TYPE_GENERIC = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredEnumerateW")]
    private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>Read one generic credential's secret blob, or null if it doesn't exist.</summary>
    public static byte[]? Read(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try { return CopyBlob(ptr); }
        finally { CredFree(ptr); }
    }

    /// <summary>
    /// Enumerate generic credentials matching <paramref name="filter"/> (e.g.
    /// <c>"copilot-cli/*"</c>) and return their secret blobs. Empty when none match.
    /// </summary>
    public static IReadOnlyList<byte[]> Enumerate(string filter)
    {
        if (!CredEnumerate(filter, 0, out var count, out var array) || count == 0)
            return [];

        var result = new List<byte[]>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var entryPtr = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                var blob = CopyBlob(entryPtr);
                if (blob is not null) result.Add(blob);
            }
        }
        finally { CredFree(array); }
        return result;
    }

    private static byte[]? CopyBlob(IntPtr credPtr)
    {
        if (credPtr == IntPtr.Zero) return null;
        var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
        if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero) return null;
        var bytes = new byte[cred.CredentialBlobSize];
        Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
        return bytes;
    }
}
