using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Fit3Manager
{
    /// <summary>C# port of apps/appcc.py: compiles a C file against the Fit3 app SDK (sdk\f3app.h, f3game.h, app.ld) with the Arm GNU toolchain and writes a .f3a (+ .meta).
    /// The result is byte-identical to appcc.py (checked by Tests.cs against apps/dist).</summary>
    public static class AppBuilder
    {
        public sealed class Result { public bool Ok; public string Log = ""; public int ImageSize, BssSize, Relocs, FileSize; }

        /// <summary>The folder with arm-none-eabi-gcc.exe: ARM_GNU_BIN, the usual install folders, then PATH. Null when not found.</summary>
        public static string FindToolchain()
        {
            List<string> c = new List<string>(); string env = Environment.GetEnvironmentVariable("ARM_GNU_BIN"); if (!string.IsNullOrEmpty(env)) c.Add(env);
            foreach (string pf in new string[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
            {
                if (string.IsNullOrEmpty(pf) || !Directory.Exists(pf)) continue;
                foreach (string d in Directory.GetDirectories(pf, "Arm GNU Toolchain*")) foreach (string v in Directory.GetDirectories(d)) c.Add(Path.Combine(v, "bin"));
                foreach (string d in Directory.GetDirectories(pf, "GNU Arm Embedded*")) c.Add(Path.Combine(d, "bin"));
            }
            string path = Environment.GetEnvironmentVariable("PATH"); if (path != null) foreach (string p in path.Split(';')) if (p.Length > 0) c.Add(p.Trim('"'));
            foreach (string d in c) { try { if (File.Exists(Path.Combine(d, "arm-none-eabi-gcc.exe"))) return d; } catch (Exception) { } }
            return null;
        }
        public static bool ValidId(string id) { return Regex.IsMatch(id ?? "", "^[a-z0-9_-]{1,11}$"); }

        static string Q(string s) { return "\"" + s + "\""; }
        static string Run(string exe, string args, out int code)
        {
            ProcessStartInfo si = new ProcessStartInfo(exe, args); si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8;
            using (Process p = Process.Start(si))
            {
                StringBuilder err = new StringBuilder(); p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) err.AppendLine(e.Data); }; p.BeginErrorReadLine();
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); code = p.ExitCode; return o + err.ToString();
            }
        }
        static string Tool(string bin, string n) { return Path.Combine(bin, "arm-none-eabi-" + n + ".exe"); }
        static byte[] Name(string s) { byte[] b = new byte[16]; byte[] a = Encoding.GetEncoding(28591).GetBytes((s ?? "").Replace(';', ' ')); for (int i = 0; i < 15 && i < a.Length; i++) b[i] = a[i] < 32 ? (byte)'?' : a[i]; return b; }
        static void U32(Stream s, uint v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }

        /// <param name="sdk">folder with f3app.h, f3game.h and app.ld</param>
        public static Result Build(string bin, string sdk, string src, string id, string pt, string en, string outPath)
        {
            Result r = new Result(); StringBuilder log = new StringBuilder(); string tmp = Path.Combine(Path.GetTempPath(), "f3a_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                if (!ValidId(id)) { r.Log = "O identificador deve ter de 1 a 11 caracteres: letras minúsculas, números, _ ou -."; return r; }
                if (!File.Exists(Path.Combine(sdk, "f3app.h")) || !File.Exists(Path.Combine(sdk, "app.ld"))) { r.Log = "Não achei a pasta sdk (f3app.h, app.ld) ao lado do programa: " + sdk; return r; }
                Directory.CreateDirectory(tmp); string obj = Path.Combine(tmp, "app.o"), elf = Path.Combine(tmp, "app.elf"), bn = Path.Combine(tmp, "app.bin"); int code;
                string o = Run(Tool(bin, "gcc"), "-mcpu=cortex-m4 -mfpu=fpv4-sp-d16 -mfloat-abi=hard -mthumb -Os -ffreestanding -fno-builtin -fno-tree-loop-distribute-patterns -fno-stack-protector -finput-charset=UTF-8 -fexec-charset=ISO-8859-1 -mword-relocations -fno-pic -fno-exceptions -ffunction-sections -fdata-sections -I " + Q(sdk) + " -c " + Q(src) + " -o " + Q(obj), out code);
                log.Append(o); if (code != 0) { r.Log = "Erro de compilação:\r\n" + log; return r; }
                o = Run(Tool(bin, "ld"), "-q --gc-sections -T " + Q(Path.Combine(sdk, "app.ld")) + " -o " + Q(elf) + " " + Q(obj), out code); log.Append(o); if (code != 0) { r.Log = "Erro de ligação:\r\n" + log; return r; }
                string und = Run(Tool(bin, "nm"), "-u " + Q(elf), out code).Trim();
                if (und.Length > 0) { r.Log = "O relógio não tem biblioteca C: use só as funções de F3Api e do f3game.h. Símbolos soltos: " + Regex.Replace(und, "\\s+", " "); return r; }
                Dictionary<string, long> syms = new Dictionary<string, long>();
                foreach (string ln in Run(Tool(bin, "nm"), Q(elf), out code).Split('\n')) { string[] p = ln.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (p.Length == 3) syms[p[2]] = Convert.ToInt64(p[0], 16); }
                if (!syms.ContainsKey("f3app_main")) { r.Log = "O app precisa definir F3App *f3app_main(const F3Api *api)."; return r; }
                long imgEnd = syms["__image_end"], bssEnd = syms["__bss_end"], entry = syms["f3app_main"] & ~1L;
                Run(Tool(bin, "objcopy"), "-O binary --only-section=.text --only-section=.data " + Q(elf) + " " + Q(bn), out code);
                byte[] img = File.ReadAllBytes(bn); byte[] full = new byte[imgEnd]; Array.Copy(img, full, Math.Min(img.Length, full.Length)); img = full;
                List<uint> relocs = new List<uint>(); string section = null;
                foreach (string ln in Run(Tool(bin, "objdump"), "-r " + Q(elf), out code).Split('\n'))
                {
                    Match m = Regex.Match(ln, "RELOCATION RECORDS FOR \\[(.*)\\]"); if (m.Success) { section = m.Groups[1].Value; continue; }
                    m = Regex.Match(ln, "^([0-9a-fA-F]+)\\s+(R_ARM_\\w+)\\s+(\\S+)"); if (!m.Success || (section != ".text" && section != ".data")) continue;
                    uint off = Convert.ToUInt32(m.Groups[1].Value, 16); string typ = m.Groups[2].Value;
                    if (typ == "R_ARM_ABS32" || typ == "R_ARM_TARGET1") relocs.Add(off);
                    else if (typ == "R_ARM_THM_CALL" || typ == "R_ARM_THM_JUMP24" || typ == "R_ARM_THM_JUMP11" || typ == "R_ARM_THM_JUMP8" || typ == "R_ARM_THM_PC8" || typ == "R_ARM_THM_PC12" || typ == "R_ARM_REL32" || typ == "R_ARM_THM_JUMP19" || typ == "R_ARM_V4BX") { }
                    else { r.Log = "Relocação não suportada " + typ + " em 0x" + off.ToString("x") + " (chame só funções deste arquivo ou de F3Api)."; return r; }
                }
                foreach (uint rr in relocs) if (rr % 4 != 0 || rr + 4 > img.Length) { r.Log = "Relocação desalinhada ou fora do app em 0x" + rr.ToString("x"); return r; }
                if (img.Length > 40000) { r.Log = "App grande demais (" + img.Length + " bytes, máx. 40000)."; return r; }
                relocs.Sort();
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(Encoding.ASCII.GetBytes("F3A1"), 0, 4); ms.WriteByte(1); ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0);
                    U32(ms, (uint)img.Length); U32(ms, (uint)(bssEnd - imgEnd)); U32(ms, (uint)entry); U32(ms, Fwd.Crc32(img, 0, img.Length)); U32(ms, (uint)relocs.Count); U32(ms, 0);
                    byte[] n1 = Name(pt), n2 = Name(en); ms.Write(n1, 0, 16); ms.Write(n2, 0, 16); ms.Write(img, 0, img.Length); foreach (uint x in relocs) U32(ms, x);
                    byte[] data = ms.ToArray(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))); File.WriteAllBytes(outPath, data);
                    r.ImageSize = img.Length; r.BssSize = (int)(bssEnd - imgEnd); r.Relocs = relocs.Count; r.FileSize = data.Length;
                }
                r.Ok = true; r.Log = (log.ToString().Trim().Length > 0 ? log.ToString().Trim() + "\r\n" : "") + "OK: " + r.FileSize + " bytes (código " + r.ImageSize + ", " + r.Relocs + " relocações).";
                return r;
            }
            catch (Exception e) { r.Log = "Erro: " + e.Message; return r; }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception) { } }
        }
    }
}
