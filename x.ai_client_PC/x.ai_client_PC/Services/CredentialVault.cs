using System.ComponentModel;
using System.IO;
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
    private const int CryptProtectUiForbidden = 0x1;
    private const string DpapiFileName = "xai_api_key.dpapi";

    public bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(ReadApiKey());
    }

    public string StorageDescription
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReadFromCredentialManager()))
            {
                return "Windows Credential Manager";
            }

            if (!string.IsNullOrWhiteSpace(ReadFromDpapiFile()))
            {
                return "Windows DPAPI";
            }

            return "not saved";
        }
    }

    public string? ReadApiKey()
    {
        return ReadFromCredentialManager() ?? ReadFromDpapiFile();
    }

    public void SaveApiKey(string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows secret storage is only available on Windows.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            DeleteApiKey();
            return;
        }

        var trimmedKey = apiKey.Trim();
        var savedToCredentialManager = TrySaveToCredentialManager(trimmedKey);
        if (savedToCredentialManager)
        {
            DeleteDpapiFile();
            return;
        }

        SaveToDpapiFile(trimmedKey);
    }

    public void DeleteApiKey()
    {
        DeleteFromCredentialManager();
        DeleteDpapiFile();
    }

    private string? ReadFromCredentialManager()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!CredRead(TargetName, CredTypeGeneric, 0, out var credentialPointer))
        {
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

    private bool TrySaveToCredentialManager(string apiKey)
    {
        var keyBytes = Encoding.Unicode.GetBytes(apiKey);
        if (keyBytes.Length > MaxCredentialBlobBytes)
        {
            return false;
        }

        var blobPointer = Marshal.StringToCoTaskMemUni(apiKey);
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
                return false;
            }

            return string.Equals(ReadFromCredentialManager(), apiKey, StringComparison.Ordinal);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blobPointer);
        }
    }

    private void DeleteFromCredentialManager()
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
                throw new Win32Exception(error, "Failed to delete API key from Windows Credential Manager.");
            }
        }
    }

    private static string DpapiFilePath => Path.Combine(AppStorage.AppDataPath, DpapiFileName);

    private static string? ReadFromDpapiFile()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(DpapiFilePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(DpapiFilePath);
            var bytes = UnprotectBytes(protectedBytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveToDpapiFile(string apiKey)
    {
        Directory.CreateDirectory(AppStorage.AppDataPath);
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = ProtectBytes(bytes);
        File.WriteAllBytes(DpapiFilePath, protectedBytes);
    }

    private static void DeleteDpapiFile()
    {
        if (File.Exists(DpapiFilePath))
        {
            File.Delete(DpapiFilePath);
        }
    }

    private static byte[] ProtectBytes(byte[] bytes)
    {
        var input = ToBlob(bytes);
        try
        {
            if (!CryptProtectData(ref input, "xAI Grok Chat PC API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to protect API key with Windows DPAPI.");
            }

            return FromBlobAndFree(output);
        }
        finally
        {
            FreeBlob(input);
        }
    }

    private static byte[] UnprotectBytes(byte[] bytes)
    {
        var input = ToBlob(bytes);
        try
        {
            if (!CryptUnprotectData(ref input, out var descriptionPointer, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to unprotect API key with Windows DPAPI.");
            }

            if (descriptionPointer != IntPtr.Zero)
            {
                LocalFree(descriptionPointer);
            }

            return FromBlobAndFree(output);
        }
        finally
        {
            FreeBlob(input);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var blob = new DataBlob
        {
            DataSize = bytes.Length,
            DataPointer = Marshal.AllocHGlobal(bytes.Length)
        };
        Marshal.Copy(bytes, 0, blob.DataPointer, bytes.Length);
        return blob;
    }

    private static byte[] FromBlobAndFree(DataBlob blob)
    {
        try
        {
            var bytes = new byte[blob.DataSize];
            Marshal.Copy(blob.DataPointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (blob.DataPointer != IntPtr.Zero)
            {
                LocalFree(blob.DataPointer);
            }
        }
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.DataPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.DataPointer);
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

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memoryPointer);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataSize;
        public IntPtr DataPointer;
    }
}
