package com.fit3.apps;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.zip.CRC32;

/**
 * Light-app installer: talks the watch's file protocol (same as the flasher, user files only) and maintains /user/apps/index.txt.
 *   write: "300"->"300"; "33bin,<path>,<len>"->"330"; blocks of <=39600 bytes + CRC32 LE, each acked "310"; "32"->"320"; "34"->"340"
 *   read : "061<path>\0" -> 0x40 + u32 BE size; "062" per block -> 0x40 + u32 BE n, data, 4 sum bytes; final "062"
 * One operation per connection. It never writes outside /user/apps/.
 */
public final class Store {
    public static final String INDEX = "/user/apps/index.txt";
    public static final int MAX_APPS = 12;
    public interface Progress { void on(long done, long total, long bps); }

    private final Conn.Factory factory; private volatile boolean aborted;
    public Store(Conn.Factory f) { factory = f; }
    public void abort() { aborted = true; }
    public boolean isAborted() { return aborted; }
    private void check() throws IOException { if (aborted) throw new IOException(L.t("Operação interrompida.", "Operation stopped.")); }
    private void sleep(long ms) throws IOException {
        long end = System.currentTimeMillis() + ms;
        while (System.currentTimeMillis() < end) { check(); try { Thread.sleep(20); } catch (InterruptedException e) { throw new IOException("interrupted"); } }
    }

    private final class Link {
        final InputStream in; final OutputStream out;
        Link(Conn c) { in = c.in(); out = c.out(); }
        void send(byte[] d, int off, int len) throws IOException { check(); out.write(d, off, len); out.flush(); }
        void send(String s) throws IOException { byte[] b = s.getBytes(StandardCharsets.UTF_8); send(b, 0, b.length); }
        void drain() throws IOException { while (in.available() > 0) in.skip(in.available()); }
        byte[] readExact(int n, int timeoutMs) throws IOException {
            byte[] b = new byte[n]; int got = 0; long end = System.currentTimeMillis() + timeoutMs;
            while (got < n) {
                check(); int av = in.available();
                if (av > 0) { int r = in.read(b, got, Math.min(av, n - got)); if (r < 0) throw new IOException(L.t("A conexão com o relógio foi perdida.", "The watch connection was lost.")); got += r; }
                else { if (System.currentTimeMillis() > end) throw new IOException(L.t("O relógio não respondeu a tempo. Conecte de novo.", "The watch did not answer in time. Reconnect.")); try { Thread.sleep(4); } catch (InterruptedException e) { throw new IOException("interrupted"); } }
            }
            return b;
        }
        void expect(String text) throws IOException {
            String got = new String(readExact(text.length(), 30000), StandardCharsets.US_ASCII);
            if (!got.equals(text)) throw new IOException(L.t("O relógio recusou a transferência. Conecte de novo.", "The watch rejected the transfer. Reconnect."));
        }
        byte[] readFile(String path) throws IOException {
            drain(); send("061" + path + "\0"); byte[] h = readExact(5, 15000);
            if ((h[0] & 255) != 0x40) return null;
            int total = be32(h, 0 + 1); if (total < 0 || total > 4 * 1024 * 1024) throw new IOException("file too big");
            byte[] data = new byte[total]; int got = 0;
            while (got < total) {
                send("062"); byte[] hh = readExact(5, 15000); int n = be32(hh, 1);
                if ((hh[0] & 255) != 0x40 || n <= 0 || n > total - got) throw new IOException(L.t("Bloco inválido na leitura.", "Invalid block while reading."));
                byte[] blk = readExact(n, 60000); readExact(4, 60000); System.arraycopy(blk, 0, data, got, n); got += n;
            }
            send("062"); sleep(100); return data;
        }
        void writeFile(String path, byte[] data, Progress p) throws IOException {
            drain(); send("300"); expect("300"); send("33bin," + path + "," + data.length); expect("330");
            byte[] frame = new byte[39604]; long t0 = System.currentTimeMillis(), lastP = t0;
            for (int off = 0; off < data.length; off += 39600) {
                int n = Math.min(39600, data.length - off); System.arraycopy(data, off, frame, 0, n);
                CRC32 c = new CRC32(); c.update(data, off, n); long crc = c.getValue();
                frame[n] = (byte) crc; frame[n + 1] = (byte) (crc >>> 8); frame[n + 2] = (byte) (crc >>> 16); frame[n + 3] = (byte) (crc >>> 24);
                send(frame, 0, n + 4); expect("310");
                long now = System.currentTimeMillis(), done = off + n;
                if (p != null && (done == data.length || now - lastP >= 200)) { p.on(done, data.length, done * 1000 / Math.max(1, now - t0)); lastP = now; }
            }
            send("32"); expect("320"); sleep(250); send("34"); expect("340");
        }
    }
    private static int be32(byte[] b, int o) { return ((b[o] & 255) << 24) | ((b[o + 1] & 255) << 16) | ((b[o + 2] & 255) << 8) | (b[o + 3] & 255); }
    private interface Action { void run(Link l) throws IOException; }
    private void withLink(Action a) throws IOException { check(); Conn c = factory.open(); try { a.run(new Link(c)); } finally { c.close(); } }

    public void checkConnection() throws IOException { withLink(new Action() { public void run(Link l) {} }); }

    private byte[] readFile(final String path) throws IOException { final byte[][] r = new byte[1][]; withLink(new Action() { public void run(Link l) throws IOException { r[0] = l.readFile(path); } }); return r[0]; }
    private void writeFile(final String path, final byte[] data, final Progress p) throws IOException {
        if (!path.startsWith("/user/apps/") || path.contains("..") || path.length() > 60) throw new IOException("path not allowed");
        withLink(new Action() { public void run(Link l) throws IOException { l.writeFile(path, data, p); } });
    }

    // ---- index
    public static boolean plainId(String id) {
        if (id.length() == 0 || id.length() > 11) return false;
        for (int i = 0; i < id.length(); i++) { char c = id.charAt(i); if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false; }
        return true;
    }
    public static List<String> parseIndex(byte[] data) {
        List<String> out = new ArrayList<>(); if (data == null) return out;
        for (String l : new String(data, StandardCharsets.UTF_8).replace("\r", "").split("\n")) { l = l.trim(); if (l.length() > 0 && l.indexOf(';') > 0) out.add(l); }
        return out;
    }
    private static byte[] indexBytes(List<String> idx) {
        StringBuilder sb = new StringBuilder(); for (String s : idx) sb.append(s).append('\n'); if (idx.isEmpty()) sb.append('\n'); return sb.toString().getBytes(StandardCharsets.UTF_8);
    }
    public static boolean isF3a(byte[] b) { return b != null && b.length >= 64 && b.length <= 60000 && b[0] == 0x46 && b[1] == 0x33 && b[2] == 0x41 && b[3] == 0x31; }

    /** The watch's app list (index lines "id;BG;FG;Pt;En"). Empty when there is none yet. */
    public List<String> readIndex() throws IOException { return parseIndex(readFile(INDEX)); }

    /** Sends the app file first, then adds it to the index (so an interrupted install never lists a missing app). */
    public List<String> install(String id, byte[] f3a, String indexLine, Progress p) throws IOException {
        if (!plainId(id)) throw new IOException(L.t("Identificador de app inválido.", "Invalid app id."));
        if (!isF3a(f3a)) throw new IOException(L.t("Arquivo .f3a inválido.", "Invalid .f3a file."));
        List<String> idx = readIndex(); int at = -1; for (int i = 0; i < idx.size(); i++) if (idx.get(i).startsWith(id + ";")) at = i;
        if (at < 0 && idx.size() >= MAX_APPS) throw new IOException(L.t("A lista de apps do relógio está cheia (máx. 12). Remova algum.", "The watch's app list is full (max 12). Remove one."));
        sleep(1500); writeFile("/user/apps/" + id + ".f3a", f3a, p); sleep(1500);
        if (at >= 0) idx.set(at, indexLine); else idx.add(indexLine);
        writeFile(INDEX, indexBytes(idx), null); return idx;
    }

    /** Takes the app off the watch's list (the launcher no longer shows it). */
    public List<String> remove(String id) throws IOException {
        if (!plainId(id)) throw new IOException(L.t("Identificador de app inválido.", "Invalid app id."));
        List<String> idx = readIndex(); int before = idx.size(); for (int i = idx.size() - 1; i >= 0; i--) if (idx.get(i).startsWith(id + ";")) idx.remove(i);
        if (idx.size() == before) return idx;
        sleep(1500); writeFile(INDEX, indexBytes(idx), null); return idx;
    }
}
