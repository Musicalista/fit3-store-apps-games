package com.fit3.apps;

import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothSocket;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.UUID;

/** RFCOMM connection to the Galaxy Fit3's file-transfer service (the service the web flasher asks the browser for). */
final class BtConn implements Conn {
    static final UUID SERVICE = UUID.fromString("db764ac8-4b08-7f25-aafe-59d03c27bae3");
    private final BluetoothSocket sock;
    private BtConn(BluetoothSocket s) { sock = s; }
    static Conn.Factory factory(final BluetoothDevice dev) {
        return new Conn.Factory() { public Conn open() throws IOException {
            BluetoothAdapter ad = BluetoothAdapter.getDefaultAdapter(); try { if (ad != null) ad.cancelDiscovery(); } catch (SecurityException ignored) {}      /* optional: needs BLUETOOTH_SCAN on Android 12+ */
            String why = "";
            for (int attempt = 0; attempt < 2; attempt++) {                               // 1: secure link, 2: insecure link (some devices refuse the first)
                BluetoothSocket s = null;
                try { s = attempt == 0 ? dev.createRfcommSocketToServiceRecord(SERVICE) : dev.createInsecureRfcommSocketToServiceRecord(SERVICE); s.connect(); return new BtConn(s); }
                catch (SecurityException e) { throw new IOException(L.t("Sem permissao de Bluetooth.", "No Bluetooth permission.")); }
                catch (IOException e) { why += (attempt == 0 ? "secure: " : " | insecure: ") + e.getMessage(); try { if (s != null) s.close(); } catch (IOException ignored) {} try { Thread.sleep(600); } catch (InterruptedException ie) { break; } }
            }
            throw new IOException(why + L.t("  (o relogio nao aceitou a conexao serial)", "  (the watch did not accept the serial connection)"));
        } };
    }
    public InputStream in() { try { return sock.getInputStream(); } catch (IOException e) { throw new RuntimeException(e); } }
    public OutputStream out() { try { return sock.getOutputStream(); } catch (IOException e) { throw new RuntimeException(e); } }
    public void close() { try { sock.close(); } catch (IOException ignored) {} }
}
