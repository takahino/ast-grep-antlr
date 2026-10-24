import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Tiny JSON helpers for the sample (no third-party JSON library). */
final class SimpleJson {
  private SimpleJson() {}

  static String quote(String s) {
    StringBuilder sb = new StringBuilder("\"");
    for (int i = 0; i < s.length(); i++) {
      char ch = s.charAt(i);
      switch (ch) {
        case '\\' -> sb.append("\\\\");
        case '"' -> sb.append("\\\"");
        case '\n' -> sb.append("\\n");
        case '\r' -> sb.append("\\r");
        case '\t' -> sb.append("\\t");
        default -> sb.append(ch);
      }
    }
    return sb.append('"').toString();
  }

  static String searchRequest(String pattern, String language, String path, int maxResults) {
    return "{"
        + "\"pattern\":"
        + quote(pattern)
        + ",\"language\":"
        + quote(language)
        + ",\"paths\":["
        + quote(path)
        + "],\"strictness\":\"smart\",\"maxResults\":"
        + maxResults
        + "}";
  }

  static String errorMessage(String json) {
    Object root = parse(json);
    if (root instanceof Map<?, ?> map && map.get("error") instanceof String err) {
      return err;
    }
    return json;
  }

  static List<Map<String, Object>> asObjectArray(String json) {
    Object root = parse(json);
    if (!(root instanceof List<?> list)) {
      throw new IllegalArgumentException("Expected JSON array, got: " + json);
    }
    List<Map<String, Object>> out = new ArrayList<>();
    for (Object item : list) {
      if (item instanceof Map<?, ?> map) {
        @SuppressWarnings("unchecked")
        Map<String, Object> cast = (Map<String, Object>) map;
        out.add(cast);
      }
    }
    return out;
  }

  static Object parse(String json) {
    return new Parser(json).parseValue();
  }

  private static final class Parser {
    private final String s;
    private int i;

    Parser(String s) {
      this.s = s.trim();
    }

    Object parseValue() {
      skipWs();
      if (i >= s.length()) {
        throw new IllegalArgumentException("Unexpected end of JSON");
      }
      char c = s.charAt(i);
      if (c == '{') {
        return parseObject();
      }
      if (c == '[') {
        return parseArray();
      }
      if (c == '"') {
        return parseString();
      }
      if (c == 't' || c == 'f' || c == 'n' || c == '-' || Character.isDigit(c)) {
        return parseLiteral();
      }
      throw new IllegalArgumentException("Unexpected '" + c + "' at " + i);
    }

    private Map<String, Object> parseObject() {
      expect('{');
      Map<String, Object> map = new LinkedHashMap<>();
      skipWs();
      if (peek('}')) {
        i++;
        return map;
      }
      while (true) {
        skipWs();
        String key = parseString();
        skipWs();
        expect(':');
        Object val = parseValue();
        map.put(key, val);
        skipWs();
        if (peek('}')) {
          i++;
          return map;
        }
        expect(',');
      }
    }

    private List<Object> parseArray() {
      expect('[');
      List<Object> list = new ArrayList<>();
      skipWs();
      if (peek(']')) {
        i++;
        return list;
      }
      while (true) {
        list.add(parseValue());
        skipWs();
        if (peek(']')) {
          i++;
          return list;
        }
        expect(',');
      }
    }

    private String parseString() {
      expect('"');
      StringBuilder sb = new StringBuilder();
      while (i < s.length()) {
        char c = s.charAt(i++);
        if (c == '"') {
          return sb.toString();
        }
        if (c == '\\') {
          if (i >= s.length()) {
            throw new IllegalArgumentException("Bad escape");
          }
          char e = s.charAt(i++);
          sb.append(
              switch (e) {
                case '"', '\\', '/' -> e;
                case 'b' -> '\b';
                case 'f' -> '\f';
                case 'n' -> '\n';
                case 'r' -> '\r';
                case 't' -> '\t';
                case 'u' -> (char) Integer.parseInt(s.substring(i, i + 4), 16);
                default -> throw new IllegalArgumentException("Bad escape: " + e);
              });
          if (e == 'u') {
            i += 4;
          }
        } else {
          sb.append(c);
        }
      }
      throw new IllegalArgumentException("Unterminated string");
    }

    private Object parseLiteral() {
      int start = i;
      while (i < s.length()) {
        char c = s.charAt(i);
        if (c == ',' || c == '}' || c == ']' || Character.isWhitespace(c)) {
          break;
        }
        i++;
      }
      String lit = s.substring(start, i);
      return switch (lit) {
        case "true" -> Boolean.TRUE;
        case "false" -> Boolean.FALSE;
        case "null" -> null;
        default -> {
          if (lit.contains(".") || lit.contains("e") || lit.contains("E")) {
            yield Double.parseDouble(lit);
          }
          yield Long.parseLong(lit);
        }
      };
    }

    private void skipWs() {
      while (i < s.length() && Character.isWhitespace(s.charAt(i))) {
        i++;
      }
    }

    private boolean peek(char c) {
      return i < s.length() && s.charAt(i) == c;
    }

    private void expect(char c) {
      skipWs();
      if (i >= s.length() || s.charAt(i) != c) {
        throw new IllegalArgumentException("Expected '" + c + "' at " + i);
      }
      i++;
    }
  }
}
