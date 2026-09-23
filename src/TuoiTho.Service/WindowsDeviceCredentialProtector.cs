using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TuoiTho.Core.Remote;

namespace TuoiTho.Service;

/// <summary>Protects the durable device bearer credential with the current Windows account's DPAPI key.</summary>
public sealed class WindowsDeviceCredentialProtector : IDeviceCredentialProtector
{
    private const uint CryptprotectUiForbidden = 0x1;
    private static readonly byte[] Entropy = "TuoiTho.Remote.DeviceCredential.v1"u8.ToArray();

    public byte[] Protect(ReadOnlySpan<byte> secret) => Transform(secret, protect: true);
    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret) => Transform(protectedSecret, protect: false);

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI is required to protect the remote device credential.");
        var inputBytes = input.ToArray();
        var inputHandle = GCHandle.Alloc(inputBytes, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        DataBlob output = default;
        try
        {
            var data = new DataBlob(inputBytes.Length, inputHandle.AddrOfPinnedObject());
            var entropy = new DataBlob(Entropy.Length, entropyHandle.AddrOfPinnedObject());
            var success = protect
                ? CryptProtectData(ref data, null, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output)
                : CryptUnprotectData(ref data, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not protect/unprotect the remote device credential with DPAPI.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(inputBytes);
            inputHandle.Free();
            entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob(int length, IntPtr data)
    {
        public int Length = length;
        public IntPtr Data = data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob optionalEntropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob optionalEntropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
