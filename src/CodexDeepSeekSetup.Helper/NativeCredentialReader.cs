using System.Runtime.InteropServices;
using System.Text;

namespace CodexDeepSeekSetup.Helper;

public sealed class NativeCredentialReader : IHelperCredentialReader
{
    private const uint GenericCredential = 1;
    private const int MaximumBlobBytes = 2560;

    public string? Read(string target)
    {
        if (!CredRead(target, GenericCredential, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize is 0 or > MaximumBlobBytes ||
                credential.CredentialBlobSize % 2 != 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
