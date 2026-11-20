using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AppTunnel.Core;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class UserProtection
{
    private const int UiForbidden = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Length;
        public nint Data;
    }

    public static byte[] Transform(byte[] input, bool encrypt)
    {
        var blob = new Blob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(input, 0, blob.Data, blob.Length);
            var success = encrypt
                ? CryptProtectData(ref blob, null, 0, 0, 0, UiForbidden, out output)
                : CryptUnprotectData(ref blob, 0, 0, 0, 0, UiForbidden, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            if (!encrypt && output.Data != 0)
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
            if (output.Data != 0) LocalFree(output.Data);
            if (encrypt) Marshal.Copy(new byte[input.Length], 0, blob.Data, input.Length);
            Marshal.FreeHGlobal(blob.Data);
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);
}
