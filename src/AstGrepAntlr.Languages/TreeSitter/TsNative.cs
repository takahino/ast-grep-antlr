using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AstGrepAntlr.Languages.TreeSitter;

/// <summary>Minimal tree-sitter C API surface used by ag4 (own P/Invoke; natives from TreeSitter.DotNet).</summary>
internal static partial class TsNative
{
    public const string LibraryName = "tree-sitter";

    public enum InputEncoding
    {
        Utf8 = 0,
        Utf16Le = 1,
        Utf16Be = 2,
        Custom = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TSNode
    {
        public uint Context0;
        public uint Context1;
        public uint Context2;
        public uint Context3;
        public nint Id;
        public nint Tree;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TSTreeCursor
    {
        public nint Tree;
        public nint Id;
        public uint Context0;
        public uint Context1;
        public uint Context2;
    }

    private static int s_resolverRegistered;

    /// <summary>Register a DllImport resolver so <c>runtimes/&lt;rid&gt;/native</c> natives are found.</summary>
    public static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref s_resolverRegistered, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(TsNative).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (
            !libraryName.Equals(LibraryName, StringComparison.OrdinalIgnoreCase)
            && !libraryName.StartsWith("tree-sitter-", StringComparison.OrdinalIgnoreCase)
            && !libraryName.StartsWith("libtree-sitter", StringComparison.OrdinalIgnoreCase)
        )
        {
            return nint.Zero;
        }

        foreach (var candidate in CandidatePaths(libraryName))
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        // Fall back to default probing (app dir / PATH).
        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var h) ? h : nint.Zero;
    }

    private static IEnumerable<string> CandidatePaths(string libraryName)
    {
        var fileName = Path.GetFileName(libraryName);
        foreach (var name in NameVariations(fileName))
        {
            yield return Path.Combine(AppContext.BaseDirectory, name);
            yield return Path.Combine(AppContext.BaseDirectory, "runtimes", GetRid(), "native", name);
        }
    }

    private static IEnumerable<string> NameVariations(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                yield return name + ".dll";
            }

            yield return name;
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                if (!name.StartsWith("lib", StringComparison.Ordinal))
                {
                    yield return "lib" + name + ".dylib";
                }

                yield return name + ".dylib";
            }

            yield return name.StartsWith("lib", StringComparison.Ordinal) ? name : "lib" + name;
            yield return name;
            yield break;
        }

        // Linux
        if (!name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
        {
            if (!name.StartsWith("lib", StringComparison.Ordinal))
            {
                yield return "lib" + name + ".so";
            }

            yield return name + ".so";
        }

        yield return name.StartsWith("lib", StringComparison.Ordinal) ? name : "lib" + name;
        yield return name;
    }

    private static string GetRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };
        if (OperatingSystem.IsWindows())
        {
            return "win-" + arch;
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx-" + arch;
        }

        return "linux-" + arch;
    }

    /// <summary>Load a grammar DLL and return the <c>tree_sitter_*</c> language pointer.</summary>
    public static nint LoadLanguage(string libraryStem, string exportName)
    {
        EnsureResolver();
        nint handle = nint.Zero;
        foreach (var candidate in CandidatePaths(libraryStem))
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                break;
            }
        }

        if (handle == nint.Zero && !NativeLibrary.TryLoad(libraryStem, typeof(TsNative).Assembly, null, out handle))
        {
            throw new DllNotFoundException($"Unable to load tree-sitter grammar library '{libraryStem}'.");
        }

        if (!NativeLibrary.TryGetExport(handle, exportName, out var export) || export == nint.Zero)
        {
            throw new EntryPointNotFoundException($"Export '{exportName}' not found in '{libraryStem}'.");
        }

        // tree_sitter_* returns TSLanguage*
        var fn = Marshal.GetDelegateForFunctionPointer<GetLanguage>(export);
        return fn();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetLanguage();

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ts_parser_new();

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ts_parser_delete(nint self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_parser_set_language(nint self, nint language);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ts_parser_parse_string_encoding(
        nint self,
        nint oldTree,
        string text,
        uint length,
        InputEncoding encoding
    );

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ts_tree_delete(nint self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial TSNode ts_tree_root_node(nint self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial ushort ts_node_symbol(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial uint ts_node_start_byte(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial uint ts_node_end_byte(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_node_is_named(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_node_is_missing(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_node_is_extra(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_node_is_error(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_node_has_error(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_node_is_null(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial uint ts_node_child_count(TSNode self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial TSNode ts_node_child(TSNode self, uint childIndex);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial TSTreeCursor ts_tree_cursor_new(TSNode node);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ts_tree_cursor_delete(ref TSTreeCursor self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    public static partial TSNode ts_tree_cursor_current_node(ref TSTreeCursor self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_tree_cursor_goto_first_child(ref TSTreeCursor self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_tree_cursor_goto_next_sibling(ref TSTreeCursor self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ts_tree_cursor_goto_parent(ref TSTreeCursor self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint ts_language_symbol_count(nint self);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ts_language_symbol_name(nint self, ushort symbol);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ts_language_supertypes(nint self, out uint length);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ts_language_subtypes(nint self, ushort supertype, out uint length);

    [LibraryImport(LibraryName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial ushort ts_language_symbol_for_name(
        nint self,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        uint length,
        [MarshalAs(UnmanagedType.U1)] bool isNamed
    );

    public static string? MarshalAnsi(nint ptr) => ptr == nint.Zero ? null : Marshal.PtrToStringAnsi(ptr);
}
