using System.Runtime.InteropServices;
using System.Text.Json;

namespace AstGrepAntlr.Native;

/// <summary>C ABI entry points for NativeAOT shared library consumers.</summary>
public static unsafe class NativeExports
{
    // Stable for process lifetime; do not pass to ag4_free.
    private static readonly nint VersionPtr = Marshal.StringToCoTaskMemUTF8("0.0.0-dev");

    [UnmanagedCallersOnly(EntryPoint = "ag4_version")]
    public static byte* Ag4Version() => (byte*)VersionPtr;

    [UnmanagedCallersOnly(EntryPoint = "ag4_free")]
    public static void Ag4Free(byte* buffer)
    {
        if (buffer is not null)
        {
            Marshal.FreeCoTaskMem((nint)buffer);
        }
    }

    /// <summary>
    /// Runs a pattern search. Returns 0 on success, non-zero on failure.
    /// On both success and failure, <paramref name="outJson"/> receives a UTF-8 JSON buffer
    /// that the caller must free with <c>ag4_free</c> (may be null only if allocation failed).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "ag4_search")]
    public static int Ag4Search(byte* requestJson, byte** outJson)
    {
        if (outJson is null)
        {
            return 2;
        }

        *outJson = null;
        try
        {
            if (requestJson is null)
            {
                *outJson = AllocUtf8(
                    JsonSerializer.Serialize(
                        new ErrorResponse { Error = "request_json is null" },
                        NativeJsonContext.Default.ErrorResponse
                    )
                );
                return 1;
            }

            var request = Marshal.PtrToStringUTF8((nint)requestJson) ?? string.Empty;
            var json = SearchHost.Search(request);
            *outJson = AllocUtf8(json);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                *outJson = AllocUtf8(
                    JsonSerializer.Serialize(
                        new ErrorResponse { Error = ex.Message },
                        NativeJsonContext.Default.ErrorResponse
                    )
                );
            }
            catch
            {
                *outJson = AllocUtf8("{\"error\":\"unknown error\"}");
            }

            return 1;
        }
    }

    private static byte* AllocUtf8(string s) => (byte*)Marshal.StringToCoTaskMemUTF8(s);
}
