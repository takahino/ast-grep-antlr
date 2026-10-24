#ifndef AG4_NATIVE_H
#define AG4_NATIVE_H

#ifdef __cplusplus
extern "C" {
#endif

/*
 * ag4_native — C ABI for AstGrepAntlr NativeAOT shared library.
 *
 * Build:
 *   dotnet publish src/AstGrepAntlr.Native -c Release -r win-x64 -o samples/gui/lib
 *
 * Strings are UTF-8. Buffers returned by ag4_search must be freed with ag4_free.
 * ag4_version returns a process-lifetime pointer; do not free it.
 */

/** Process-lifetime UTF-8 version string (do not ag4_free). */
const char* ag4_version(void);

/**
 * Pattern search.
 * request_json — filesystem:
 *   {"pattern":"foo($$$ARGS)","language":"java","paths":["."],"strictness":"smart","maxResults":100}
 * request_json — in-memory buffers (frontend-collected strings; paths ignored when sources set):
 *   {"pattern":"foo($$$ARGS)","language":"java",
 *    "sources":[{"path":"Demo.java","content":"class Demo { void m(){ foo(1,2); } }"}],
 *    "strictness":"smart","maxResults":100}
 * On success (return 0): *out_json is a JSON array of matches (CLI-compatible shape).
 * On failure (non-zero): *out_json is {"error":"..."} when allocation succeeded.
 * Caller must ag4_free(*out_json) when non-NULL.
 */
int ag4_search(const char* request_json, char** out_json);

/** Free a buffer returned by ag4_search. */
void ag4_free(char* ptr);

#ifdef __cplusplus
}
#endif

#endif /* AG4_NATIVE_H */
