using System.Runtime.InteropServices;
using System.Text;

namespace x.ai_client_PC.Infrastructure;

public static class WindowsCredentialStore
{
    private const string TargetName = "xAI_Client_PC_ApiKey";

    public static void SaveApiKey(string apiKey)
    {
        var blob = Marshal.StringToCoTaskMemUni(apiKey);
        try
        {
            var credential = new NativeCredential
            {
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = "xAI API Key",
                TargetAlias = string.Empty,
                Type = CredentialType.Generic,
                Persist = CredentialPersistence.LocalMachine,
                CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(apiKey),
                CredentialBlob = blob,
                TargetName = TargetName,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException("Failed to save API key to Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static string? LoadApiKey()
    {
        if (!CredRead(TargetName, CredentialType.Generic, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)(credential.CredentialBlobSize / 2));
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public static void DeleteApiKey()
    {
        CredDelete(TargetName, CredentialType.Generic, 0);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, CredentialType type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr cred);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, CredentialType type, int flags);

    private enum CredentialType : uint
    {
        Generic = 1
    }

    private enum CredentialPersistence : uint
    {
        LocalMachine = 2
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public CredentialType Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CredentialPersistence Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}