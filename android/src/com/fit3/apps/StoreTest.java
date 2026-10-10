package com.fit3.apps;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.zip.CRC32;

/** PC test: Store against a mock watch speaking the file protocol over loopback TCP. */
public final class StoreTest {
    static final Map<String, byte[]> files = new HashMap<>();
    static int fails = 0;
    static void check(String n, boolean ok) { System.out.println((ok ? "  ok   " : "  FAIL ") + n); if (!ok) fails++; }

    static String cmd(InputStream in) throws IOException { while (in.available() == 0) { try { Thread.sleep(1); } catch (InterruptedException e) {} } try { Thread.sleep(3); } catch (InterruptedException e) {} byte[] b = new byte[in.available()]; int r = in.read(b); return new String(b, 0, r, StandardCharsets.US_ASCII); }
    static byte[] readN(InputStream in, int n) throws IOException { byte[] b = new byte[n]; int g = 0; while (g < n) { int r = in.read(b, g, n - g); if (r <= 0) throw new EOFException(); g += r; } return b; }
    static void serve(Socket s) {
        try {
            InputStream in = s.getInputStream(); OutputStream out = s.getOutputStream(); String c = cmd(in);
            if (c.equals("300")) {
                out.write("300".getBytes()); c = cmd(in); String[] p = c.split(","); int total = Integer.parseInt(p[2]); out.write("330".getBytes()); byte[] f = new byte[total]; int off = 0;
                while (off < total) { int n = Math.min(39600, total - off); byte[] fr = readN(in, n + 4); CRC32 k = new CRC32(); k.update(fr, 0, n); long got = (fr[n] & 255) | ((fr[n + 1] & 255) << 8) | ((fr[n + 2] & 255) << 16) | ((long) (fr[n + 3] & 255) << 24); if (k.getValue() != got) { out.write("FAIL".getBytes()); return; } System.arraycopy(fr, 0, f, off, n); off += n; out.write("310".getBytes()); }
                if (!cmd(in).equals("32")) return; out.write("320".getBytes()); if (!cmd(in).equals("34")) return; out.write("340".getBytes()); files.put(p[1], f);
            } else if (c.startsWith("061")) {
                byte[] f = files.get(c.substring(3).replace("\0", ""));
                if (f == null) { out.write(new byte[5]); return; }
                out.write(new byte[] { 0x40, (byte) (f.length >> 24), (byte) (f.length >> 16), (byte) (f.length >> 8), (byte) f.length }); int off = 0;
                while (true) { if (!cmd(in).equals("062") || off >= f.length) return; int n = Math.min(5000, f.length - off); long sum = 0; for (int i = 0; i < n; i++) sum += f[off + i] & 255;
                    ByteArrayOutputStream o = new ByteArrayOutputStream(); o.write(new byte[] { 0x40, 0, 0, (byte) (n >> 8), (byte) n }); o.write(f, off, n); o.write(new byte[] { (byte) (sum >> 24), (byte) (sum >> 16), (byte) (sum >> 8), (byte) sum }); out.write(o.toByteArray()); off += n; }
            }
        } catch (IOException e) { } finally { try { s.close(); } catch (IOException e) {} }
    }
    static byte[] app(int n) { byte[] b = new byte[n]; new Random(n).nextBytes(b); b[0] = 0x46; b[1] = 0x33; b[2] = 0x41; b[3] = 0x31; return b; }

    public static void main(String[] a) throws Exception {
        final ServerSocket ss = new ServerSocket(0, 50, InetAddress.getLoopbackAddress());
        new Thread(new Runnable() { public void run() { try { while (true) { final Socket s = ss.accept(); new Thread(new Runnable() { public void run() { serve(s); } }).start(); } } catch (IOException e) {} } }).start();
        Conn.Factory f = new Conn.Factory() { public Conn open() throws IOException { final Socket c = new Socket(InetAddress.getLoopbackAddress(), ss.getLocalPort()); return new Conn() { public InputStream in() { try { return c.getInputStream(); } catch (IOException e) { throw new RuntimeException(e); } } public OutputStream out() { try { return c.getOutputStream(); } catch (IOException e) { throw new RuntimeException(e); } } public void close() { try { c.close(); } catch (IOException e) {} } }; } };
        Store st = new Store(f); st.checkConnection(); check("connection check", true);
        check("empty index at start", st.readIndex().isEmpty());
        byte[] a1 = app(900), a2 = app(45000);
        List<String> idx = st.install("hello", a1, "hello;F8D8B0;A85A20;Ola;Hello", null); check("install hello", idx.size() == 1 && Arrays.equals(files.get("/user/apps/hello.f3a"), a1));
        idx = st.install("big", a2, "big;000000;FFFFFF;Grande;Big", null); check("install 45 KB (2 blocks)", idx.size() == 2 && Arrays.equals(files.get("/user/apps/big.f3a"), a2));
        check("index on the watch lists both", st.readIndex().size() == 2);
        idx = st.install("hello", a1, "hello;111111;222222;Ola2;Hello2", null); check("reinstall replaces the line, no duplicate", idx.size() == 2 && idx.get(0).contains("Ola2"));
        idx = st.remove("hello"); check("remove drops it from the index", idx.size() == 1 && idx.get(0).startsWith("big;") && st.readIndex().size() == 1);
        check("remove of an unknown app changes nothing", st.remove("nope").size() == 1);
        boolean bad = false; try { st.install("../x", a1, "x;0;0;a;b", null); } catch (IOException e) { bad = true; } check("bad id refused", bad);
        bad = false; try { st.install("okid", new byte[100], "okid;0;0;a;b", null); } catch (IOException e) { bad = true; } check("non-.f3a refused", bad);
        for (int i = 0; i < 11; i++) st.install("a" + i, a1, "a" + i + ";0;0;a;b", null);
        bad = false; try { st.install("extra", a1, "extra;0;0;a;b", null); } catch (IOException e) { bad = true; } check("13th app refused (list full)", bad);
        System.out.println(fails == 0 ? "ALL OK" : "FAILURES " + fails); System.exit(fails == 0 ? 0 : 1);
    }
}
