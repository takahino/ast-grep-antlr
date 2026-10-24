import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.ptr.PointerByReference;
import java.io.File;

/** JNA binding for ag4_native.dll / .so */
public final class Ag4Native {
  public interface Lib extends Library {
    Pointer ag4_version();

    int ag4_search(String requestJson, PointerByReference outJson);

    void ag4_free(Pointer buffer);
  }

  private static Lib INSTANCE;

  private Ag4Native() {}

  public static synchronized Lib load() {
    if (INSTANCE != null) {
      return INSTANCE;
    }
    String dir = resolveLibDir();
    System.setProperty("jna.library.path", dir);
    INSTANCE = Native.load("ag4_native", Lib.class);
    return INSTANCE;
  }

  public static String version() {
    Pointer p = load().ag4_version();
    return p == null ? "" : p.getString(0);
  }

  public static Result search(String requestJson) {
    PointerByReference out = new PointerByReference();
    int rc = load().ag4_search(requestJson, out);
    Pointer ptr = out.getValue();
    try {
      String json = ptr == null ? "" : ptr.getString(0);
      return new Result(rc, json);
    } finally {
      if (ptr != null) {
        load().ag4_free(ptr);
      }
    }
  }

  public static String resolveLibDir() {
    String env = System.getenv("AG4_NATIVE_DIR");
    if (env != null && !env.isBlank() && new File(env).isDirectory()) {
      return new File(env).getAbsolutePath();
    }
    File here = new File(System.getProperty("user.dir"));
    File[] candidates =
        new File[] {
          new File(here, "lib"),
          new File(here, "../lib"),
          new File(here, "../../lib"),
          new File(here, "samples/gui/lib"),
        };
    for (File c : candidates) {
      File dll = new File(c, "ag4_native.dll");
      File so = new File(c, "ag4_native.so");
      if (dll.isFile() || so.isFile()) {
        return c.getAbsolutePath();
      }
    }
    return new File(here, "../lib").getAbsolutePath();
  }

  public static String defaultFixturesDir() {
    File fixtures = new File(new File(resolveLibDir()).getParentFile(), "fixtures");
    return fixtures.isDirectory() ? fixtures.getAbsolutePath() : resolveLibDir();
  }

  public record Result(int code, String json) {}
}
