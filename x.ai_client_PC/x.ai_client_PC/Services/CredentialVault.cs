using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace x.ai_client_PC.Services;

public sealed class CredentialVault
{
    private const string TargetName = "xAI Grok Chat PC:xai_api_key";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxCredentialBlobBytes = 512;

    public bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(ReadApiKey());
    }

    public string? ReadApiKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!CredRead(TargetName, CredTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void SaveApiKey(string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager доступен только в Windows.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            DeleteApiKey();
            return;
        }

        var trimmedKey = apiKey.Trim();
        var keyBytes = Encoding.Unicode.GetBytes(trimmedKey);
        if (keyBytes.Length > MaxCredentialBlobBytes)
        {
            throw new InvalidOperationException($"API key слишком длинный для Windows Credential Manager ({keyBytes.Length} bytes).");
        }

        var blobPointer = Marshal.StringToCoTaskMemUni(trimmedKey);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = (uint)keyBytes.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName,
                Comment = "xAI API key for xAI Grok Chat PC"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось сохранить API key в Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blobPointer);
        }
    }

    public void DeleteApiKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!CredDelete(TargetName, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Не удалось удалить API key из Windows Credential Manager.");
            }
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredWriteW", SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW", SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPointer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }
}
