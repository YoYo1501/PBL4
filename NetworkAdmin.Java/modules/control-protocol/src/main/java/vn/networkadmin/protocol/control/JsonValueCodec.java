package vn.networkadmin.protocol.control;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.math.BigDecimal;
import java.math.BigInteger;

/** Minimal strict JSON value codec for the wire contract; supports objects, arrays and JSON primitives. */
public final class JsonValueCodec {
    private JsonValueCodec() { }

    public static String encode(Object value) {
        StringBuilder out = new StringBuilder();
        write(value, out, 0);
        return out.toString();
    }

    public static Object decode(String json) {
        Parser parser = new Parser(json);
        Object value = parser.value(0);
        parser.whitespace();
        if (!parser.end()) throw parser.error("Unexpected content after JSON value.");
        return value;
    }

    private static void write(Object value, StringBuilder out, int depth) {
        if (depth > 64) throw new IllegalArgumentException("JSON nesting limit exceeded.");
        if (value == null) { out.append("null"); return; }
        if (value instanceof String s) { quote(s, out); return; }
        if (value instanceof Boolean || value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long) { out.append(value); return; }
        if (value instanceof Number number) {
            double d = number.doubleValue();
            if (!Double.isFinite(d)) throw new IllegalArgumentException("JSON numbers must be finite.");
            out.append(number); return;
        }
        if (value instanceof Map<?, ?> map) {
            out.append('{'); boolean first = true;
            for (var entry : map.entrySet()) {
                if (!(entry.getKey() instanceof String key)) throw new IllegalArgumentException("JSON object keys must be strings.");
                if (!first) out.append(','); first = false;
                quote(key, out); out.append(':'); write(entry.getValue(), out, depth + 1);
            }
            out.append('}'); return;
        }
        if (value instanceof Iterable<?> iterable) {
            out.append('['); boolean first = true;
            for (Object item : iterable) { if (!first) out.append(','); first = false; write(item, out, depth + 1); }
            out.append(']'); return;
        }
        if (value.getClass().isArray()) {
            out.append('['); int length = java.lang.reflect.Array.getLength(value);
            for (int i = 0; i < length; i++) { if (i > 0) out.append(','); write(java.lang.reflect.Array.get(value, i), out, depth + 1); }
            out.append(']'); return;
        }
        throw new IllegalArgumentException("Unsupported JSON value type: " + value.getClass().getName());
    }

    private static void quote(String value, StringBuilder out) {
        out.append('"');
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            switch (c) {
                case '"' -> out.append("\\\""); case '\\' -> out.append("\\\\");
                case '\b' -> out.append("\\b"); case '\f' -> out.append("\\f");
                case '\n' -> out.append("\\n"); case '\r' -> out.append("\\r"); case '\t' -> out.append("\\t");
                default -> { if (c < 0x20) out.append(String.format("\\u%04x", (int) c)); else out.append(c); }
            }
        }
        out.append('"');
    }

    private static final class Parser {
        private final String input; private int position;
        private Parser(String input) { this.input = input == null ? "" : input; }
        private boolean end() { return position == input.length(); }
        private IllegalArgumentException error(String message) { return new IllegalArgumentException(message + " At character " + position + "."); }
        private void whitespace() { while (!end() && (input.charAt(position) == ' ' || input.charAt(position) == '\t' || input.charAt(position) == '\r' || input.charAt(position) == '\n')) position++; }
        private Object value(int depth) {
            if (depth > 64) throw error("JSON nesting limit exceeded.");
            whitespace(); if (end()) throw error("JSON value is required.");
            char c = input.charAt(position);
            if (c == '"') return string();
            if (c == '{') return object(depth + 1);
            if (c == '[') return array(depth + 1);
            if (c == 't') { literal("true"); return true; }
            if (c == 'f') { literal("false"); return false; }
            if (c == 'n') { literal("null"); return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return number();
            throw error("Invalid JSON value.");
        }
        private Map<String, Object> object(int depth) {
            position++; whitespace(); Map<String, Object> result = new LinkedHashMap<>();
            if (take('}')) return result;
            while (true) {
                whitespace(); if (end() || input.charAt(position) != '"') throw error("Object property name must be a string.");
                String key = string(); whitespace(); require(':'); Object value = value(depth); result.put(key, value); whitespace();
                if (take('}')) return result; require(',');
            }
        }
        private List<Object> array(int depth) {
            position++; whitespace(); List<Object> result = new ArrayList<>();
            if (take(']')) return result;
            while (true) { result.add(value(depth)); whitespace(); if (take(']')) return result; require(','); }
        }
        private String string() {
            require('"'); StringBuilder result = new StringBuilder();
            while (!end()) {
                char c = input.charAt(position++);
                if (c == '"') return result.toString();
                if (c < 0x20) throw error("Unescaped control character in JSON string.");
                if (c != '\\') { result.append(c); continue; }
                if (end()) throw error("Incomplete JSON escape.");
                char escape = input.charAt(position++);
                switch (escape) {
                    case '"', '\\', '/' -> result.append(escape); case 'b' -> result.append('\b'); case 'f' -> result.append('\f');
                    case 'n' -> result.append('\n'); case 'r' -> result.append('\r'); case 't' -> result.append('\t');
                    case 'u' -> result.append(unicode()); default -> throw error("Invalid JSON escape.");
                }
            }
            throw error("Unterminated JSON string.");
        }
        private char unicode() {
            if (position + 4 > input.length()) throw error("Incomplete Unicode escape.");
            int value = 0;
            for (int i = 0; i < 4; i++) { int digit = Character.digit(input.charAt(position++), 16); if (digit < 0) throw error("Invalid Unicode escape."); value = (value << 4) | digit; }
            return (char) value;
        }
        private Number number() {
            int start = position; if (take('-') && end()) throw error("Invalid JSON number.");
            if (take('0')) { if (!end() && Character.isDigit(input.charAt(position))) throw error("Leading zero in JSON number."); }
            else { digits(true); }
            boolean decimal = false;
            if (take('.')) { decimal = true; digits(true); }
            if (take('e') || take('E')) { decimal = true; if (!take('+')) take('-'); digits(true); }
            String text = input.substring(start, position);
            try { if (!decimal) { try { return Long.parseLong(text); } catch (NumberFormatException tooLargeForLong) { return new BigInteger(text); } } return new BigDecimal(text); }
            catch (NumberFormatException ex) { throw error("Invalid or unsupported JSON number."); }
        }
        private void digits(boolean required) { int start = position; while (!end() && input.charAt(position) >= '0' && input.charAt(position) <= '9') position++; if (required && start == position) throw error("JSON number requires digits."); }
        private void literal(String expected) { if (!input.startsWith(expected, position)) throw error("Invalid JSON literal."); position += expected.length(); }
        private boolean take(char expected) { if (!end() && input.charAt(position) == expected) { position++; return true; } return false; }
        private void require(char expected) { if (!take(expected)) throw error("Expected '" + expected + "'."); }
    }
}
