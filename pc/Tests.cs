using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Fit3Manager
{
    /// <summary>PC tests: the validator on real packages (+ damaged copies) and the transfer protocol against a mock watch over TCP.</summary>
    static class Tests
    {
        static int failures = 0;
        static void Check(string name, bool ok) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + name); if (!ok) failures++; }

        sealed class Store { public volatile byte[] File; public int Sessions; public volatile bool Ota; public volatile int FlipBlock = -1; public byte[] UserFile; public string UserPath; }
        static byte[] ReadN(Stream s, int n) { byte[] b = new byte[n]; int g = 0; while (g < n) { int r = s.Read(b, g, n - g); if (r <= 0) throw new EndOfStreamException(); g += r; } return b; }
        static string Cmd(Socket c) { while (c.Available == 0) Thread.Sleep(1); Thread.Sleep(3); byte[] b = new byte[c.Available]; int r = c.Receive(b); return Encoding.ASCII.GetString(b, 0, r); }
        static void W(Socket c, string s) { c.Send(Encoding.ASCII.GetBytes(s)); }
        static void MockWatch(object o)
        {
            object[] a = (object[])o; Socket c = (Socket)a[0]; Store st = (Store)a[1];
            try
            {
                Interlocked.Increment(ref st.Sessions); string cmd = Cmd(c);
                if (cmd == "300")
                {
                    W(c, "300"); cmd = Cmd(c); if (!cmd.StartsWith("33bin,")) return; string[] p = cmd.Split(','); int total = int.Parse(p[2]); W(c, "330"); byte[] file = new byte[total]; int off = 0, blk = 0;
                    NetworkStream ns = new NetworkStream(c);
                    while (off < total)
                    {
                        int n = Math.Min(39600, total - off); byte[] f = ReadN(ns, n + 4); uint crc = Fwd.Crc32(f, 0, n); uint got = (uint)(f[n] | (f[n + 1] << 8) | (f[n + 2] << 16) | (f[n + 3] << 24));
                        if (crc != got) { W(c, "FAIL"); return; }
                        Buffer.BlockCopy(f, 0, file, off, n); if (blk == st.FlipBlock) file[off] ^= 1; off += n; blk++; W(c, "310");
                    }
                    if (Cmd(c) != "32") return; W(c, "320"); if (Cmd(c) != "34") return; W(c, "340");
                    if (p[1] == Watch.RemotePath) st.File = file; else { st.UserFile = file; st.UserPath = p[1]; }
                }
                else if (cmd.StartsWith("061"))
                {
                    string path = cmd.Substring(3).TrimEnd('\0'); byte[] f = path == Watch.RemotePath ? st.File : (path == st.UserPath ? st.UserFile : null);
                    if (f == null) { c.Send(new byte[5]); return; }
                    c.Send(new byte[] { 0x40, (byte)(f.Length >> 24), (byte)(f.Length >> 16), (byte)(f.Length >> 8), (byte)f.Length }); int off = 0;
                    while (true)
                    {
                        if (Cmd(c) != "062") return; if (off >= f.Length) return; int n = Math.Min(5000, f.Length - off); long sum = 0; for (int i = 0; i < n; i++) sum += f[off + i];
                        byte[] o2 = new byte[5 + n + 4]; o2[0] = 0x40; o2[3] = (byte)(n >> 8); o2[4] = (byte)n; Buffer.BlockCopy(f, off, o2, 5, n); o2[5 + n] = (byte)(sum >> 24); o2[6 + n] = (byte)(sum >> 16); o2[7 + n] = (byte)(sum >> 8); o2[8 + n] = (byte)sum; c.Send(o2); off += n;
                    }
                }
                else if (cmd.StartsWith("00AT^"))
                {
                    while (true)                                                // one connection can carry several AT commands
                    {
                        if (cmd == "00AT^OTA_UPDATE") { st.Ota = true; W(c, "OK\r\n"); return; }
                        W(c, "OK:" + cmd.Substring(5) + "\r\n"); cmd = Cmd(c);
                    }
                }
            }
            catch (Exception) { }
            finally { try { c.Close(); } catch (Exception) { } }
        }
        static ConnFactory Factory(Store st, TcpListener l)
        {
            return delegate()
            {
                Socket cl = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); cl.Connect(IPAddress.Loopback, ((IPEndPoint)l.LocalEndpoint).Port);
                Socket srv = l.AcceptSocket(); Thread t = new Thread(MockWatch); t.IsBackground = true; t.Start(new object[] { srv, st }); return new SockConn(cl);
            };
        }

        static int Main(string[] args)
        {
            foreach (string path in args)
            {
                byte[] b = File.ReadAllBytes(path); Console.WriteLine(path + " (" + b.Length + " bytes)"); DateTime t0 = DateTime.Now; Fwd.Info info = Fwd.Validate(b, null);
                Check("valid package: " + info.Source + " -> " + info.Target + ", " + info.Count + " components, stock=" + info.Stock + ", " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms, " + info.Sha256.Substring(0, 16), true);
                int[] spots = { 0, 8, 80, 200, 5000, b.Length / 2, b.Length - 3 };
                foreach (int s in spots) { byte[] c = (byte[])b.Clone(); c[s] ^= 0x5a; bool rej = false; try { Fwd.Validate(c, null); } catch (Fwd.Bad) { rej = true; } Check("flipped byte " + s + " rejected", rej); }
                { byte[] c = new byte[b.Length - 1]; Buffer.BlockCopy(b, 0, c, 0, c.Length); bool rej = false; try { Fwd.Validate(c, null); } catch (Fwd.Bad) { rej = true; } Check("truncated by 1 rejected", rej); }
            }
            Random r = new Random(5); byte[] pkg = new byte[100777]; r.NextBytes(pkg);
            TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Store st = new Store(); Watch w = new Watch(Factory(st, l));
            w.CheckConnection(); Thread.Sleep(150); Check("connection check opens and closes once", st.Sessions == 1);
            w.Stage(pkg, false); Check("upload with CRC/ACK + size check; file intact", Equal(st.File, pkg) && st.Sessions == 3);
            st = new Store(); w = new Watch(Factory(st, l)); w.Stage(pkg, true); Check("upload + full readback accepted", Equal(st.File, pkg));
            st = new Store(); st.FlipBlock = 1; w = new Watch(Factory(st, l)); bool caught = false; try { w.Stage(pkg, true); } catch (IOException) { caught = true; } Check("corruption on the watch detected by the readback", caught);
            st = new Store(); w = new Watch(Factory(st, l)); w.WriteUserFile("/user/gb.gb", pkg, null); Check("user file written", Equal(st.UserFile, pkg) && st.UserPath == "/user/gb.gb");
            byte[] back = w.ReadFile("/user/gb.gb", null); Check("user file read back identical", back != null && Equal(back, pkg)); Check("missing file reads as null", w.ReadFile("/user/nope.bin", null) == null);
            caught = false; try { w.WriteUserFile("/user/ota/R390_WEB_FOTA.bin", pkg, null); } catch (IOException) { caught = true; } Check("writing the OTA slot as a user file is refused", caught);
            caught = false; try { w.WriteUserFile("/nand/system/x.bin", pkg, null); } catch (IOException) { caught = true; } Check("writing system paths is refused", caught);
            string[] at = w.AtMany(new string[] { "SWVER", "GETBATPERCENT" }, 3000); Check("AT commands answered: " + at[0].Trim() + " / " + at[1].Trim(), at[0].StartsWith("OK:SWVER") && at[1].StartsWith("OK:GETBATPERCENT"));
            bool[] sending = { false }; string reply = w.StartOta(sending); Check("OTA command sent, sending flag set, reply " + (reply == null ? "null" : reply.Trim()), st.Ota && sending[0] && reply != null);
            Watch w2 = new Watch(Factory(new Store(), l)); caught = false; Thread th = new Thread(delegate() { try { w2.Progress = delegate(string p, long d, long t, long bps) { if (d > 100000) w2.Abort(); }; w2.Stage(new byte[3000000], false); } catch (IOException) { caught = true; } }); th.Start(); th.Join(20000); Check("abort stops the transfer", caught);
            {
                st = new Store(); w = new Watch(Factory(st, l)); byte[] app = new byte[900]; app[0] = 0x46; app[1] = 0x33; app[2] = 0x41; app[3] = 0x31;
                w.InstallLight("hello", app, "hello;F8D8B0;A85A20;Ola;Hello", null);
                Check("light app installed: index written last", st.UserPath == "/user/apps/index.txt" && st.UserFile != null);
                List<string> idx = Watch.ParseIndex(st.UserFile); Check("index has the new line", idx.Count == 1 && idx[0] == "hello;F8D8B0;A85A20;Ola;Hello");
                bool bad = false; try { w.InstallLight("../x", app, "x;000000;000000;a;b", null); } catch (IOException) { bad = true; } Check("odd ids are refused", bad);
                bad = false; try { w.InstallLight("okid", new byte[100], "okid;000000;000000;a;b", null); } catch (IOException) { bad = true; } Check("a file that is not .f3a is refused", bad);
            }
            {
                string bin = AppBuilder.FindToolchain(), sdk = Path.GetFullPath("../apps");
                if (bin == null || !Directory.Exists(sdk)) Console.WriteLine("  skip app builder (no Arm toolchain or ../apps)");
                else foreach (string id in new string[] { "snake", "tetris", "relogio", "alarme" })
                {
                    string outp = Path.Combine(Path.GetTempPath(), "ab_" + id + ".f3a"); string pt = id, en = id;
                    foreach (string line in File.ReadAllLines(Path.Combine(sdk, "dist", id + ".meta"))) { string[] q = line.Split(';'); if (q.Length >= 5) { pt = q[3]; en = q[4]; } }
                    AppBuilder.Result br = AppBuilder.Build(bin, sdk, Path.Combine(sdk, "src", id + ".c"), id, pt, en, outp);
                    Check("app builder: " + id + " compiles" + (br.Ok ? "" : " (" + br.Log + ")"), br.Ok);
                    if (br.Ok) Check("app builder: " + id + ".f3a is byte-identical to appcc.py's", Equal(File.ReadAllBytes(outp), File.ReadAllBytes(Path.Combine(sdk, "dist", id + ".f3a"))));
                }
                AppBuilder.Result bad = AppBuilder.Build(bin ?? "", sdk, Path.Combine(Path.GetTempPath(), "nope.c"), "Bad Id", "a", "b", Path.Combine(Path.GetTempPath(), "bad.f3a")); Check("app builder: invalid id refused", !bad.Ok);
            }
            Check("Catalog: every entry resolves to a unique file name", UniqueFiles());
            Console.WriteLine(failures == 0 ? "ALL OK" : "FAILURES: " + failures); return failures == 0 ? 0 : 1;
        }
        static bool Equal(byte[] a, byte[] b) { if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        static bool UniqueFiles() { HashSet<string> s = new HashSet<string>(); foreach (App a in Catalog.All) if (!s.Add(Catalog.FileFor(a))) return false; return true; }
    }
}
