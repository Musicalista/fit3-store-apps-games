using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Fit3Manager
{
    /// <summary>One open serial connection (a Bluetooth RFCOMM socket on Windows, a TCP socket in the tests).</summary>
    public abstract class Conn : IDisposable
    {
        public abstract int Available { get; }
        public abstract int Read(byte[] b, int o, int n);
        public abstract void Write(byte[] b, int o, int n);
        public abstract void Close();
        public void Dispose() { Close(); }
    }
    public sealed class SockConn : Conn
    {
        readonly Socket s;
        public SockConn(Socket sock) { s = sock; }
        public override int Available { get { return s.Available; } }
        public override int Read(byte[] b, int o, int n) { return s.Receive(b, o, n, SocketFlags.None); }
        public override void Write(byte[] b, int o, int n) { int sent = 0; while (sent < n) sent += s.Send(b, o + sent, n - sent, SocketFlags.None); }
        public override void Close() { try { s.Close(); } catch (Exception) { } }
    }
    public delegate Conn ConnFactory();

    // ------------------------------------------------------------------------------------------------ Bluetooth (Winsock AF_BTH + the device list of bthprops.cpl)
    public sealed class BtDevice
    {
        public string Name; public ulong Address; public bool Connected;
        public static string Fmt(ulong a) { return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}", (a >> 40) & 255, (a >> 32) & 255, (a >> 24) & 255, (a >> 16) & 255, (a >> 8) & 255, a & 255); }
        public static bool TryParse(string s, out ulong addr)
        {
            addr = 0; string h = s.Replace(":", "").Replace("-", "").Trim(); if (h.Length != 12) return false;
            try { addr = Convert.ToUInt64(h, 16); return true; } catch (Exception) { return false; }
        }
        public override string ToString() { return Name + "  (" + Fmt(Address) + ")" + (Connected ? "  [conectado]" : ""); }
    }
    sealed class BtEndPoint : EndPoint
    {
        readonly ulong addr; readonly Guid svc;
        public BtEndPoint(ulong a, Guid g) { addr = a; svc = g; }
        public override AddressFamily AddressFamily { get { return (AddressFamily)32; } }
        public override SocketAddress Serialize()
        {
            SocketAddress sa = new SocketAddress((AddressFamily)32, 30);
            byte[] a = BitConverter.GetBytes(addr); for (int i = 0; i < 8; i++) sa[2 + i] = a[i];
            byte[] g = svc.ToByteArray(); for (int i = 0; i < 16; i++) sa[10 + i] = g[i];
            for (int i = 26; i < 30; i++) sa[i] = 0;                       // port 0: Windows resolves the channel from the service class id (SDP)
            return sa;
        }
        public override EndPoint Create(SocketAddress s) { return this; }
    }
    public static class Bt
    {
        public static readonly Guid Service = new Guid("db764ac8-4b08-7f25-aafe-59d03c27bae3");
        [StructLayout(LayoutKind.Sequential)] struct SEARCH { public uint dwSize; public int fAuth, fRemembered, fUnknown, fConnected, fInquiry; public byte timeout; public IntPtr hRadio; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct INFO { public uint dwSize; public ulong Address; public uint cod; public int fConnected, fRemembered, fAuthenticated; public long t1, t2, t3, t4; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName; }
        [DllImport("bthprops.cpl", SetLastError = true)] static extern IntPtr BluetoothFindFirstDevice(ref SEARCH p, ref INFO i);
        [DllImport("bthprops.cpl", SetLastError = true)] static extern bool BluetoothFindNextDevice(IntPtr h, ref INFO i);
        [DllImport("bthprops.cpl", SetLastError = true)] static extern bool BluetoothFindDeviceClose(IntPtr h);

        public static List<BtDevice> Paired()
        {
            List<BtDevice> list = new List<BtDevice>();
            SEARCH p = new SEARCH(); p.dwSize = (uint)Marshal.SizeOf(typeof(SEARCH)); p.fAuth = 1; p.fRemembered = 1; p.fConnected = 1; p.fUnknown = 0; p.fInquiry = 0; p.timeout = 1; p.hRadio = IntPtr.Zero;
            INFO i = new INFO(); i.dwSize = (uint)Marshal.SizeOf(typeof(INFO));
            IntPtr h = BluetoothFindFirstDevice(ref p, ref i);
            if (h == IntPtr.Zero) return list;
            do { BtDevice d = new BtDevice(); d.Name = i.szName; d.Address = i.Address; d.Connected = i.fConnected != 0; list.Add(d); i.dwSize = (uint)Marshal.SizeOf(typeof(INFO)); }
            while (BluetoothFindNextDevice(h, ref i));
            BluetoothFindDeviceClose(h); return list;
        }
        public static ConnFactory Factory(ulong address)
        {
            return delegate()
            {
                Socket s = new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)3);
                s.SendTimeout = 30000; s.ReceiveTimeout = 30000;
                try { s.Connect(new BtEndPoint(address, Service)); }
                catch (SocketException e)
                {
                    s.Close();
                    string why = e.SocketErrorCode == SocketError.ConnectionRefused ? Lang.T("o relógio recusou (canal ocupado?)") : e.SocketErrorCode == SocketError.TimedOut ? Lang.T("tempo esgotado (relógio longe, dormindo ou ocupado)") : e.SocketErrorCode == SocketError.HostDown ? Lang.T("relógio fora de alcance") : e.SocketErrorCode.ToString();
                    throw new IOException(Lang.T("Não consegui abrir o serviço serial do relógio: ") + why + " [Winsock " + e.ErrorCode + "].");
                }
                return new SockConn(s);
            };
        }
    }

    // ------------------------------------------------------------------------------------------------ the watch's file-transfer / AT protocol
    public sealed class Watch
    {
        public const string RemotePath = "/user/ota/R390_WEB_FOTA.bin";
        readonly ConnFactory factory; volatile bool aborted;
        public Action<string> Log = delegate(string s) { };
        public Action<string, long, long, long> Progress = delegate(string a, long b, long c, long d) { };
        public Watch(ConnFactory f) { factory = f; }
        public void Abort() { aborted = true; }
        public bool IsAborted { get { return aborted; } }
        void Check() { if (aborted) throw new IOException(Lang.T("Operação interrompida.")); }
        void Sleep(int ms) { int end = Environment.TickCount + ms; while (unchecked(end - Environment.TickCount) > 0) { Check(); Thread.Sleep(20); } }

        public sealed class Link
        {
            readonly Conn c; readonly Watch w;
            public Link(Watch w, Conn c) { this.w = w; this.c = c; }
            public void Send(byte[] d, int off, int len) { w.Check(); c.Write(d, off, len); }
            public void Send(string s) { byte[] b = Encoding.UTF8.GetBytes(s); Send(b, 0, b.Length); }
            public byte[] ReadExact(int n, int timeoutMs)
            {
                byte[] b = new byte[n]; int got = 0; int end = Environment.TickCount + timeoutMs;
                while (got < n)
                {
                    w.Check(); int av = c.Available;
                    if (av > 0) { int r = c.Read(b, got, Math.Min(av, n - got)); if (r <= 0) throw new IOException(Lang.T("A conexão com o relógio foi perdida.")); got += r; }
                    else { if (unchecked(end - Environment.TickCount) < 0) throw new IOException(Lang.T("O relógio não respondeu a tempo. Conecte de novo.")); Thread.Sleep(4); }
                }
                return b;
            }
            public void Expect(string text)
            {
                string got = Encoding.ASCII.GetString(ReadExact(text.Length, 30000));
                if (got != text) throw new IOException(Lang.T("O relógio recusou a transferência (esperava \"") + text + Lang.T("\", veio \"") + got + Lang.T("\"). Conecte de novo."));
            }
            public void Drain() { while (c.Available > 0) { byte[] t = new byte[c.Available]; c.Read(t, 0, t.Length); } }
            /// <summary>Sends "00AT^cmd" and returns whatever the watch prints, waiting until it goes quiet.</summary>
            public string At(string cmd, int maxWaitMs)
            {
                Drain(); Send("00AT^" + cmd); MemoryStream bo = new MemoryStream(); int start = Environment.TickCount, last = start;
                for (;;)
                {
                    w.Check(); int av = c.Available;
                    if (av > 0) { byte[] b = new byte[av]; int r = c.Read(b, 0, av); if (r > 0) { bo.Write(b, 0, r); last = Environment.TickCount; } }
                    int now = Environment.TickCount;
                    if (bo.Length > 0 && now - last > 450) break;
                    if (now - start > maxWaitMs) break;
                    Thread.Sleep(15);
                }
                return Encoding.UTF8.GetString(bo.ToArray()).Trim();
            }
            static int Be32(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }
            /// <summary>The file contents, or null if it does not exist.</summary>
            public byte[] ReadFile(string path, Action<long, long> progress)
            {
                Drain(); Send(path.Insert(0, "061") + "\0"); byte[] h = ReadExact(5, 15000);
                if (h[0] != 0x40) return null;
                int total = Be32(h, 1); if (total < 0 || total > 16 * 1024 * 1024) throw new IOException(Lang.T("Arquivo grande demais (") + total + " bytes).");
                byte[] data = new byte[total]; int got = 0;
                while (got < total)
                {
                    Send("062"); byte[] hh = ReadExact(5, 15000); int n = Be32(hh, 1);
                    if (hh[0] != 0x40 || n <= 0 || n > total - got) throw new IOException(Lang.T("Bloco inválido na leitura."));
                    byte[] blk = ReadExact(n, 60000); ReadExact(4, 60000); Buffer.BlockCopy(blk, 0, data, got, n); got += n; if (progress != null) progress(got, total);
                }
                Send("062"); Thread.Sleep(100); return data;
            }
            public void WriteFile(string path, byte[] data, Action<long, long, long> progress)
            {
                Drain(); Send("300"); Expect("300"); Send("33bin," + path + "," + data.Length); Expect("330");
                byte[] frame = new byte[39604]; int t0 = Environment.TickCount, lastP = t0;
                for (int off = 0; off < data.Length; off += 39600)
                {
                    int n = Math.Min(39600, data.Length - off); Buffer.BlockCopy(data, off, frame, 0, n);
                    uint crc = Fwd.Crc32(data, off, n); frame[n] = (byte)crc; frame[n + 1] = (byte)(crc >> 8); frame[n + 2] = (byte)(crc >> 16); frame[n + 3] = (byte)(crc >> 24);
                    Send(frame, 0, n + 4); Expect("310");
                    int now = Environment.TickCount; long done = off + n;
                    if (progress != null && (done == data.Length || now - lastP >= 200)) { progress(done, data.Length, done * 1000 / Math.Max(1, now - t0)); lastP = now; }
                }
                Send("32"); Expect("320"); w.Sleep(250); Send("34"); Expect("340");
            }
        }
        public void WithLink(Action<Link> a)
        {
            Check(); Conn c = factory();
            try { a(new Link(this, c)); } finally { c.Close(); }
        }

        public void CheckConnection() { WithLink(delegate(Link l) { }); }
        public string[] AtMany(string[] cmds, int waitMs)
        {
            string[] r = new string[cmds.Length];
            string[] holder = r; WithLink(delegate(Link l) { for (int i = 0; i < cmds.Length; i++) holder[i] = l.At(cmds[i], waitMs); });
            return r;
        }
        public byte[] ReadFile(string path, Action<long, long> progress) { byte[] res = null; WithLink(delegate(Link l) { res = l.ReadFile(path, progress); }); return res; }
        /// <summary>Writes a user file. Refuses system paths: firmware goes through Stage().</summary>
        public void WriteUserFile(string path, byte[] data, Action<long, long, long> progress)
        {
            if (!path.StartsWith("/user/") || path.StartsWith("/user/ota") || path.StartsWith("/user/rescue") || path.Contains("..") || path.Length > 100) throw new IOException(Lang.T("Caminho não permitido: só arquivos em /user/ (fora de ota e rescue)."));
            WithLink(delegate(Link l) { l.WriteFile(path, data, progress); });
        }
        // ---- light apps: /user/apps/<id>.f3a plus the index the watch reads. One operation per connection, like the original flasher.
        public const string AppsIndex = "/user/apps/index.txt";
        static bool PlainId(string id) { if (id.Length == 0 || id.Length > 11) return false; foreach (char c in id) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false; return true; }
        public static List<string> ParseIndex(byte[] data)
        {
            List<string> lines = new List<string>(); if (data == null) return lines;
            foreach (string l in Encoding.GetEncoding(28591).GetString(data).Replace("\r", "").Split('\n')) if (l.Trim().Length > 0 && l.IndexOf(';') > 0) lines.Add(l.Trim());
            return lines;
        }
        public List<string> ReadIndex() { return ParseIndex(ReadFile(AppsIndex, null)); }
        static byte[] IndexBytes(List<string> idx) { return Encoding.GetEncoding(28591).GetBytes(idx.Count == 0 ? "\n" : string.Join("\n", idx.ToArray()) + "\n"); }
        public void InstallLight(string id, byte[] f3a, string indexLine, Action<long, long, long> progress)
        {
            if (!PlainId(id)) throw new IOException(Lang.T("Identificador de app inválido."));
            if (f3a.Length < 64 || f3a[0] != 0x46 || f3a[1] != 0x33 || f3a[2] != 0x41 || f3a[3] != 0x31 || f3a.Length > 60000) throw new IOException(Lang.T("Arquivo .f3a inválido."));
            List<string> idx = ReadIndex(); int at = -1; for (int i = 0; i < idx.Count; i++) if (idx[i].StartsWith(id + ";")) at = i;
            if (at < 0 && idx.Count >= 12) throw new IOException(Lang.T("A lista de apps do relógio está cheia (máx. 12). Remova algum."));
            Sleep(1500); WriteUserFile("/user/apps/" + id + ".f3a", f3a, progress); Sleep(1500);
            if (at >= 0) idx[at] = indexLine; else idx.Add(indexLine);
            WriteUserFile(AppsIndex, IndexBytes(idx), null);
        }
        public void RemoveLight(string id)
        {
            List<string> idx = ReadIndex(); int n = idx.RemoveAll(delegate(string s) { return s.StartsWith(id + ";"); }); if (n == 0) return;
            Sleep(1500); WriteUserFile(AppsIndex, IndexBytes(idx), null);
        }
        /// <summary>Sends a validated package to the OTA slot and checks the watch holds a file of the same size (optionally reads it all back).</summary>
        public void Stage(byte[] bytes, bool fullReadback)
        {
            WithLink(delegate(Link l) { l.WriteFile(RemotePath, bytes, delegate(long d, long t, long bps) { Progress("upload", d, t, bps); }); });
            Sleep(2200);
            WithLink(delegate(Link l)
            {
                l.Send("061" + RemotePath + "\0"); byte[] h = l.ReadExact(5, 30000);
                if (h[0] != 0x40) throw new IOException(Lang.T("Não foi possível conferir o arquivo no relógio."));
                int total = (h[1] << 24) | (h[2] << 16) | (h[3] << 8) | h[4];
                if (total != bytes.Length) throw new IOException(Lang.T("O tamanho do arquivo no relógio não bate. Instalação bloqueada."));
                Progress("verify", 0, total, 0);
                if (!fullReadback) return;
                Sleep(250); int offset = 0;
                while (offset < total)
                {
                    l.Send("062"); byte[] hh = l.ReadExact(5, 30000); int n = (hh[1] << 24) | (hh[2] << 16) | (hh[3] << 8) | hh[4];
                    if (hh[0] != 0x40 || n <= 0 || n > 1024 * 1024 || n > total - offset) throw new IOException(Lang.T("Bloco de conferência inválido."));
                    byte[] data = l.ReadExact(n, 180000), sum = l.ReadExact(4, 180000); uint actual = 0;
                    for (int i = 0; i < n; i++) { actual = unchecked(actual + data[i]); if (data[i] != bytes[offset + i]) throw new IOException(Lang.T("O conteúdo no relógio não bate. Instalação bloqueada.")); }
                    uint want = (uint)((sum[0] << 24) | (sum[1] << 16) | (sum[2] << 8) | sum[3]);
                    if (actual != want) throw new IOException(Lang.T("Erro de soma de verificação na leitura."));
                    offset += n; Progress("verify", offset, total, 0); Sleep(250);
                }
                l.Send("062"); Sleep(100);
            });
        }
        /// <summary>Starts the update on the watch. Returns the reply (or null). sending[0] becomes true once the command may have left.</summary>
        public string StartOta(bool[] sending)
        {
            Sleep(2200); string reply = null;
            WithLink(delegate(Link l)
            {
                sending[0] = true; Log(Lang.T("Enviando o comando OTA.")); l.Send("00AT^OTA_UPDATE");
                try { reply = Encoding.UTF8.GetString(l.ReadExact(4, 6000)); } catch (IOException) { reply = null; }
            });
            return reply;
        }
    }
}
