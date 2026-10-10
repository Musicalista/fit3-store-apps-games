package com.fit3.manager;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

/**
 * The watch's file-transfer protocol over the Bluetooth serial service (the same one the web flasher uses; see README):
 *   write a file : "300" -> "300"; "33bin,<path>,<len>" -> "330"; blocks of up to 39600 bytes + CRC32 (little endian), each acknowledged by "310"; "32" -> "320"; (250 ms) "34" -> "340"
 *   read a file  : "061<path>\0" -> 0x40 + u32 BE size; "062" per block -> 0x40 + u32 BE n, n bytes, 4 checksum bytes; a last "062" ends it
 *   start the OTA: "00AT^OTA_UPDATE"
 * The connection is opened and closed around every phase, exactly like the original.
 */
public final class Flash {
    public static final String REMOTE_PATH = "/user/ota/R390_WEB_FOTA.bin";
    public interface Events { void progress(String phase, long done, long total, long bytesPerSecond); void log(String line); }

    private final Conn.Factory factory; private volatile boolean aborted;
    public Flash(Conn.Factory f) { factory = f; }
    public void abort() { aborted = true; }
    public boolean isAborted() { return aborted; }
    private void check() throws IOException { if (aborted) throw new IOException(L.t("Operacao interrompida.", "Operation stopped.")); }
    private void sleep(long ms) throws IOException {
        long end = System.currentTimeMillis() + ms;
        while (System.currentTimeMillis() < end) { check(); try { Thread.sleep(Math.min(50, Math.max(1, end - System.currentTimeMillis()))); } catch (InterruptedException e) { throw new IOException("interrupted"); } }
    }

    private final class Link {
        final Conn c; final InputStream in; final OutputStream out;
        Link(Conn c) { this.c = c; in = c.in(); out = c.out(); }
        void send(byte[] d, int off, int len) throws IOException { check(); out.write(d, off, len); out.flush(); }
        void send(String s) throws IOException { byte[] b = s.getBytes(StandardCharsets.UTF_8); send(b, 0, b.length); }
        byte[] readExact(int n, int timeoutMs) throws IOException {
            byte[] b = new byte[n]; int got = 0; long end = System.currentTimeMillis() + timeoutMs;
            while (got < n) {
                check(); int av = in.available();
                if (av > 0) { int r = in.read(b, got, Math.min(av, n - got)); if (r < 0) throw new IOException(L.t("Conexao com o relogio perdida.", "The watch connection was lost.")); got += r; }
                else { if (System.currentTimeMillis() > end) throw new IOException(L.t("O relogio nao respondeu a tempo. Conecte de novo.", "The watch did not answer in time. Reconnect.")); try { Thread.sleep(4); } catch (InterruptedException e) { throw new IOException("interrupted"); } }
            }
            return b;
        }
        void expect(String text) throws IOException {
            String got = new String(readExact(text.length(), 30000), StandardCharsets.US_ASCII);
            if (!got.equals(text)) throw new IOException(L.t("O relogio recusou a transferencia. Conecte de novo.", "The watch rejected the transfer. Reconnect."));
        }
    }
    private interface Action { void run(Link l) throws IOException; }
    private void withLink(Action a) throws IOException {
        check(); Conn c = factory.open();
        try { a.run(new Link(c)); } finally { c.close(); }
    }

    /** Opens and closes the serial service once: proves the watch is reachable. */
    public void checkConnection() throws IOException { withLink(new Action() { public void run(Link l) {} }); }

    private static int be32(byte[] b, int o) { return ((b[o] & 255) << 24) | ((b[o + 1] & 255) << 16) | ((b[o + 2] & 255) << 8) | (b[o + 3] & 255); }

    /** Sends the package to REMOTE_PATH and checks that the watch holds a file of the same size (optionally reads everything back and compares). */
    public void stage(final byte[] bytes, final boolean fullReadback, final Events ev) throws IOException {
        withLink(new Action() { public void run(Link l) throws IOException {
            l.send("300"); l.expect("300");
            l.send("33bin," + REMOTE_PATH + "," + bytes.length); l.expect("330");
            byte[] frame = new byte[39604]; long t0 = System.currentTimeMillis(), lastP = t0;
            for (int off = 0; off < bytes.length; off += 39600) {
                int n = Math.min(39600, bytes.length - off); System.arraycopy(bytes, off, frame, 0, n);
                long crc = Fwd.crc32(bytes, off, n); frame[n] = (byte) crc; frame[n + 1] = (byte) (crc >>> 8); frame[n + 2] = (byte) (crc >>> 16); frame[n + 3] = (byte) (crc >>> 24);
                l.send(frame, 0, n + 4); l.expect("310");
                long now = System.currentTimeMillis(), done = off + n;
                if (done == bytes.length || now - lastP >= 200) { ev.progress("upload", done, bytes.length, done * 1000 / Math.max(1, now - t0)); lastP = now; }
            }
            l.send("32"); l.expect("320"); sleep(250); l.send("34"); l.expect("340");
        } });
        sleep(2200);
        withLink(new Action() { public void run(Link l) throws IOException {
            l.send(("061" + REMOTE_PATH + "\0")); byte[] h = l.readExact(5, 30000);
            if ((h[0] & 255) != 0x40) throw new IOException(L.t("Nao foi possivel conferir o arquivo no relogio.", "Could not check the file on the watch."));
            int total = be32(h, 1);
            if (total != bytes.length) throw new IOException(L.t("O tamanho do arquivo no relogio nao bate. Instalacao bloqueada.", "The file size on the watch does not match. Installation blocked."));
            ev.progress("verify", 0, total, 0);
            if (!fullReadback) return;
            sleep(250); int offset = 0;
            while (offset < total) {
                l.send("062"); byte[] hh = l.readExact(5, 30000); int n = be32(hh, 1);
                if ((hh[0] & 255) != 0x40 || n <= 0 || n > 1024 * 1024 || n > total - offset) throw new IOException(L.t("Bloco de conferencia invalido.", "Invalid verification block."));
                byte[] data = l.readExact(n, 180000), sum = l.readExact(4, 180000); long actual = 0;
                for (int i = 0; i < n; i++) { actual = (actual + (data[i] & 255)) & 0xffffffffL; if (data[i] != bytes[offset + i]) throw new IOException(L.t("O conteudo no relogio nao bate. Instalacao bloqueada.", "The content on the watch does not match. Installation blocked.")); }
                if (actual != (be32(sum, 0) & 0xffffffffL)) throw new IOException(L.t("Erro de soma de verificacao na leitura.", "Checksum error while reading back."));
                offset += n; ev.progress("verify", offset, total, 0); sleep(250);
            }
            l.send("062"); sleep(100);
        } });
    }

    /** Sends the command that makes the watch start the update. Returns the 4-byte reply or null; throws only before the command left (then nothing happened). */
    public String startOta(final Events ev, final boolean[] sending) throws IOException {
        sleep(2200);
        final String[] reply = { null };
        withLink(new Action() { public void run(Link l) throws IOException {
            sending[0] = true; ev.log(L.t("Enviando o comando OTA.", "Sending the OTA command."));
            l.send("00AT^OTA_UPDATE");
            try { reply[0] = new String(l.readExact(4, 6000), StandardCharsets.UTF_8); } catch (IOException e) { reply[0] = null; }
        } });
        return reply[0];
    }
}
