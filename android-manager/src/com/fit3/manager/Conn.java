package com.fit3.manager;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/** One open serial (RFCOMM) connection to the watch. The Android implementation wraps a BluetoothSocket; tests use pipes. */
public interface Conn {
    InputStream in();
    OutputStream out();
    void close();
    interface Factory { Conn open() throws IOException; }
}
