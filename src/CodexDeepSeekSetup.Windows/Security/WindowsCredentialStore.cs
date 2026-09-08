using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Windows.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public OperationResult<Unit> Write(string target, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var bytes = CredentialBlobCodec.Encode(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };

            return CredWrite(ref credential, 0)
                ? OperationResult<Unit>.Success(default)
                : Failure<Unit>("credential.write.failed", "无法写入 Windows 凭据管理器。", Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (var index = 0; index < bytes.Length; index++)
            {
                Marshal.WriteByte(blob, index, 0);
            }
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public OperationResult<string> Read(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!CredRead(target, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? OperationResult<string>.Failure("credential.not_found", "Windows 凭据中尚未保存 DeepSeek API Key。")
                : Failure<string>("credential.read.failed", "无法读取 DeepSeek API Key。", error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return OperationResult<string>.Failure("credential.empty", "Windows 凭据中没有 API Key。");
            }

            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return OperationResult<string>.Success(CredentialBlobCodec.Decode(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public OperationResult<Unit> Delete(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (CredDelete(target, GenericCredential, 0))
        {
            return OperationResult<Unit>.Success(default);
        }

        var error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound
            ? OperationResult<Unit>.Success(default)
            : Failure<Unit>("credential.delete.failed", "无法删除 Windows 凭据。", error);
    }

    private static OperationResult<T> Failure<T>(string code, string message, int nativeError) =>
        OperationResult<T>.Failure(code, $"{message}（Windows 错误 {nativeError}: {new Win32Exception(nativeError).Message}）");

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

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
