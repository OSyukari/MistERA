using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Encrypts/decrypts strings at rest using Windows DPAPI (CurrentUser scope), via direct
/// P/Invoke to crypt32.dll rather than System.Security.Cryptography.ProtectedData, since that
/// type isn't part of the .NET Standard 2.1 surface this project targets.
/// </summary>
public static class LLM_KeyProtector
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr hMem);

    static byte[] BlobToBytes(DATA_BLOB blob)
    {
        var bytes = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
        LocalFree(blob.pbData);
        return bytes;
    }

    public static string EncryptToBase64(string plaintext)
    {
        var input = Encoding.UTF8.GetBytes(plaintext);
        var inBlob = new DATA_BLOB { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
        Marshal.Copy(input, 0, inBlob.pbData, input.Length);
        try
        {
            if (!CryptProtectData(ref inBlob, "MistEra LLM Setting", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
                throw new InvalidOperationException("CryptProtectData failed");
            return Convert.ToBase64String(BlobToBytes(outBlob));
        }
        finally { Marshal.FreeHGlobal(inBlob.pbData); }
    }

    public static string DecryptFromBase64(string base64)
    {
        var input = Convert.FromBase64String(base64);
        var inBlob = new DATA_BLOB { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
        Marshal.Copy(input, 0, inBlob.pbData, input.Length);
        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
                throw new InvalidOperationException("CryptUnprotectData failed");
            return Encoding.UTF8.GetString(BlobToBytes(outBlob));
        }
        finally { Marshal.FreeHGlobal(inBlob.pbData); }
    }
}
