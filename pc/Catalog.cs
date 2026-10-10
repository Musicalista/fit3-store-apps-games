using System;
using System.Collections.Generic;
using System.IO;

namespace Fit3Manager
{
    [Flags]
    public enum Feat { None = 0, Kbd = 1, Internet = 2, Mini = 4, More = 8, Tools = 16, Gb = 32, Blocks = 64, Cs = 128, Doom = 256, Proxy = 512, Apps = 1024 }

    /// <summary>One installable "app set": all our apps live inside the firmware image, so installing means flashing the matching validated package.</summary>
    public sealed class App
    {
        public string Id, Title, Desc; public Feat F; public int Use;      // Use: percent of the free flash area used by the injected code
        public App(string id, string title, Feat f, int use, string desc) { Id = id; Title = title; F = f; Use = use; Desc = desc; }
        public string FileName { get { return "fit3-" + Id + ".bin"; } }
    }

    public static class Catalog
    {
        public static readonly string[] FeatNames = { Lang.T("Internet direta (Bluetooth)"), Lang.T("Minijogos (Snake, Flappy, Tetris, 2048)"), Lang.T("Mais jogos (Pong, Velha, Sudoku, Memória)"), Lang.T("Utilitários (calculadora, cronômetro, notas...)"), Lang.T("Game Boy (emulador)"), Lang.T("Mundo de blocos 3D (sobrevivência)"), Lang.T("FPS estilo CS"), Lang.T("Doom (estilo)"), Lang.T("IA + Discador (precisa do app no celular)"), Lang.T("Meus apps (instalar apps leves pela loja, sem regravar)") };
        public static readonly Feat[] FeatFlags = { Feat.Internet, Feat.Mini, Feat.More, Feat.Tools, Feat.Gb, Feat.Blocks, Feat.Cs, Feat.Doom, Feat.Proxy, Feat.Apps };

        const Feat K = Feat.Kbd, I = Feat.Internet, M = Feat.Mini, G2 = Feat.More, T = Feat.Tools, GB = Feat.Gb, B = Feat.Blocks, C = Feat.Cs, D = Feat.Doom, P = Feat.Proxy, AP = Feat.Apps;
        public static readonly App[] All = new App[]
        {
            new App("kbd-only", Lang.T("Só o teclado de resposta"), K, 10, Lang.T("Só o teclado T9 nas respostas rápidas das notificações. Nada mais muda no relógio.")),
            new App("direct-min", Lang.T("Internet direta"), K | I, 72, Lang.T("Navegador de texto pela ancoragem Bluetooth do celular, sem nenhum app extra (HTTPS TLS 1.3).")),
            new App("direct-tools", Lang.T("Internet + Utilitários"), K | I | T, 84, Lang.T("Internet direta e utilitários.")),
            new App("direct-games2", Lang.T("Internet + Mais jogos"), K | I | G2, 84, Lang.T("Internet direta, Pong, Jogo da velha, Sudoku e Memória.")),
            new App("direct-mini", Lang.T("Internet + Minijogos"), K | I | M, 83, Lang.T("Internet direta e os 4 minijogos.")),
            new App("direct-mini-tools", Lang.T("Internet + Minijogos + Utilitários"), K | I | M | T, 95, Lang.T("Internet direta, minijogos e utilitários.")),
            new App("direct-mini-games2", Lang.T("Internet + todos os jogos pequenos"), K | I | M | G2, 95, Lang.T("Internet direta, minijogos e Mais jogos.")),
            new App("direct-mini-gb", Lang.T("Internet + Minijogos + Game Boy"), K | I | M | Feat.Gb, 97, Lang.T("Internet direta, minijogos e emulador de Game Boy.")),
            new App("lite-net", Lang.T("Rede completa (IA e Discador)"), K | I | M | P, 95, Lang.T("Internet direta + IA + Discador + minijogos. IA e Discador precisam do app Fit3 Hub no celular.")),
            new App("lite-offline", Lang.T("Minijogos"), K | M, 26, Lang.T("Snake, Flappy, Tetris e 2048 com gestos de toque.")),
            new App("lite-gb-offline", Lang.T("Minijogos + Game Boy"), K | M | GB, 41, Lang.T("Minijogos e emulador de Game Boy (envie a ROM na aba Jogos).")),
            new App("lite-all-offline", Lang.T("Jogos pequenos + Game Boy + Utilitários"), K | M | G2 | T | GB, 65, Lang.T("Tudo o que é pequeno, sem Internet.")),
            new App("mc-offline", Lang.T("Mundo de blocos 3D"), K | M | B, 63, Lang.T("Mundo de blocos com sobrevivência, mobs e modo criativo, mais minijogos.")),
            new App("mc-games2-offline", Lang.T("Mundo de blocos + Mais jogos"), K | M | G2 | B, 75, Lang.T("Mundo de blocos 3D e todos os jogos pequenos.")),
            new App("mc-all-offline", Lang.T("Mundo de blocos + Game Boy + Utilitários"), K | M | B | GB | T, 89, Lang.T("Mundo de blocos, Game Boy, utilitários e minijogos.")),
            new App("mc-doom-offline", Lang.T("Mundo de blocos + Doom"), K | M | B | D, 71, Lang.T("Mundo de blocos 3D e Doom (estilo).")),
            new App("mc-doom-gb-offline", Lang.T("Mundo de blocos + Doom + Game Boy"), K | M | B | D | GB, 85, Lang.T("Mundo de blocos, Doom, Game Boy e minijogos.")),
            new App("cs-offline", Lang.T("FPS estilo CS"), K | M | C, 48, Lang.T("Tiro em primeira pessoa e minijogos.")),
            new App("cs-games2-offline", Lang.T("FPS + Mais jogos"), K | M | G2 | C, 60, Lang.T("FPS e todos os jogos pequenos.")),
            new App("cs-all-offline", Lang.T("FPS + Game Boy + Utilitários"), K | M | C | GB | T, 75, Lang.T("FPS, Game Boy, utilitários e minijogos.")),
            new App("cs-doom-offline", "FPS + Doom", K | M | C | D, 56, Lang.T("FPS e Doom (estilo).")),
            new App("cs-doom-gb-offline", "FPS + Doom + Game Boy", K | M | C | D | GB, 70, Lang.T("FPS, Doom, Game Boy e minijogos.")),
            new App("store-base", Lang.T("Loja de apps (base)"), K | AP, 21, Lang.T("Só o launcher 'Meus apps': depois instale apps leves pelo PC em segundos, sem regravar o firmware.")),
            new App("store-lite", Lang.T("Loja de apps + Minijogos"), K | M | AP, 32, Lang.T("Launcher 'Meus apps' e os 4 minijogos.")),
            new App("store-lite-all", Lang.T("Loja de apps + jogos pequenos + Game Boy + Utilitários"), K | M | G2 | T | GB | AP, 69, Lang.T("Launcher 'Meus apps', jogos pequenos, Game Boy e utilitários.")),
            new App("store-direct", Lang.T("Loja de apps + Internet direta"), K | I | AP, 78, Lang.T("Launcher 'Meus apps' e navegador de texto pela ancoragem Bluetooth.")),
            new App("store-mc", Lang.T("Loja de apps + Mundo de blocos 3D"), K | M | B | AP, 67, Lang.T("Launcher 'Meus apps' e o mundo de blocos com sobrevivência.")),
            new App("store-cs", Lang.T("Loja de apps + FPS estilo CS"), K | M | C | AP, 52, Lang.T("Launcher 'Meus apps' e o FPS.")),
            new App("spp-only", Lang.T("Serial Bluetooth sempre aberto (2 patches)"), Feat.None, 0, Lang.T("Original com 2 instruções alteradas: o Bluetooth fica sempre visível e conectável (como na tela de configuração) e a porta serial aceita a conexão mesmo ocupada. Nada mais muda.")),
            new App("spp-access-only", Lang.T("Serial: só Bluetooth sempre visível (1 patch)"), Feat.None, 0, Lang.T("Só o primeiro patch (Bluetooth sempre visível e conectável). Confirmado no relógio.")),
            new App("store-base-spp", Lang.T("Loja de apps (base) + serial sempre aberto"), K | AP, 26, Lang.T("Launcher 'Meus apps' mais os 2 patches do serial Bluetooth sempre aberto.")),
            new App("max-cs", Lang.T("O máximo: FPS + jogos + Game Boy + utilitários + loja"), K | M | G2 | T | GB | C | AP, 97, Lang.T("Tudo o que cabe com o FPS: minijogos, mais jogos, Game Boy, utilitários, Meus apps e o serial sempre aberto (2 patches). Sem Internet, mundo de blocos e Doom.")),
            new App("max-mc", Lang.T("O máximo: mundo de blocos + jogos + Game Boy + utilitários + loja"), K | M | T | GB | B | AP, 99, Lang.T("Tudo o que cabe com o mundo de blocos: minijogos, Game Boy, utilitários, Meus apps e o serial sempre aberto (2 patches). Sem Internet, FPS, Doom e Mais jogos.")),
            new App("net-mini", Lang.T("Internet + minijogos + loja (sem Minecraft e CS)"), K | I | M | AP, 94, Lang.T("Internet direta pelo celular (ancoragem Bluetooth), os 4 minijogos, Meus apps e o serial sempre aberto (2 patches).")),
            new App("net-gb", Lang.T("Internet + Game Boy + loja"), K | I | GB | AP, 97, Lang.T("Internet direta, emulador de Game Boy, Meus apps e o serial sempre aberto (2 patches).")),
            new App("net-tools", Lang.T("Internet + utilitários + loja"), K | I | T | AP, 95, Lang.T("Internet direta, utilitários (calculadora, cronômetro, notas...), Meus apps e o serial sempre aberto (2 patches).")),
            new App("net-games2", Lang.T("Internet + mais jogos + loja"), K | I | G2 | AP, 95, Lang.T("Internet direta, Pong, Velha, Sudoku e Memória, Meus apps e o serial sempre aberto (2 patches).")),
            new App("stock",Lang.T("Original Samsung (restaurar)"), Feat.None, 0, Lang.T("Firmware original AZA3 sem modificações. Use para voltar ao padrão (não apaga seus dados). Arquivo: stock-aza3.bin.")),
        };
        public sealed class Light { public string Id, Pt, En, Bg, Fg, Path, Ico; public long Size; public string IndexLine() { return Id + ";" + Bg + ";" + Fg + ";" + Pt + ";" + En + (Ico != null ? ";" + Ico : ""); } }
        /// <summary>Light apps: *.f3a files with an optional *.meta (id;BGRRGGBB;FGRRGGBB;Nome PT;Name EN) next to each. Looked up in apps\ next to the exe, then in the packages folder.</summary>
        public static List<Light> LoadLight(string pkgFolder)
        {
            List<Light> list = new List<Light>(); string exe = AppDomain.CurrentDomain.BaseDirectory; string[] dirs = { Path.Combine(exe, "apps"), Path.Combine(pkgFolder, "apps"), Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fit3Manager"), "apps") };
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (string f in Directory.GetFiles(d, "*.f3a"))
                {
                    byte[] b = File.ReadAllBytes(f); if (b.Length < 64 || b[0] != 0x46 || b[1] != 0x33 || b[2] != 0x41 || b[3] != 0x31) continue;
                    Light l = new Light(); l.Id = Path.GetFileNameWithoutExtension(f).ToLowerInvariant(); l.Path = f; l.Size = b.Length; l.Bg = "D0DCF8"; l.Fg = "3454A8"; l.Pt = Z(b, 32); l.En = Z(b, 48);
                    string meta = Path.ChangeExtension(f, ".meta");
                    if (File.Exists(meta)) { string[] p = File.ReadAllText(meta).Trim().Split(';'); if (p.Length >= 5 && p[1].Length == 6 && p[2].Length == 6) { l.Bg = p[1]; l.Fg = p[2]; l.Pt = p[3]; l.En = p[4]; if (p.Length >= 6 && System.Text.RegularExpressions.Regex.IsMatch(p[5].Trim(), "^[0-9A-Fa-f]{256}$")) l.Ico = p[5].Trim().ToUpperInvariant(); } }
                    bool dup = false; foreach (Light o in list) if (o.Id == l.Id) dup = true; if (!dup) list.Add(l);
                }
            }
            return list;
        }
        static string Z(byte[] b, int o) { int n = 0; while (n < 16 && b[o + n] != 0) n++; return System.Text.Encoding.ASCII.GetString(b, o, n); }
        public static string FileFor(App a) { return a.Id == "stock" ? "stock-aza3.bin" : a.FileName; }

        /// <summary>Folders that may hold the packages: next to the exe, then the newest fit3-release-* folder in Downloads.</summary>
        public static string FindFolder()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory; string c1 = Path.Combine(exe, "packages");
            if (Directory.Exists(c1)) return c1;
            string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(dl)) { string[] ds = Directory.GetDirectories(dl, "fit3-release-v*"); if (ds.Length > 0) { Array.Sort(ds, StringComparer.OrdinalIgnoreCase); return ds[ds.Length - 1]; } }
            return exe;
        }
        public static string PathFor(string folder, App a)
        {
            string p = Path.Combine(folder, FileFor(a)); if (File.Exists(p)) return p;
            string dl0 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(dl0)) { string[] ds = Directory.GetDirectories(dl0, "fit3-release-v*"); Array.Sort(ds, StringComparer.OrdinalIgnoreCase); for (int i = ds.Length - 1; i >= 0; i--) { string q = Path.Combine(ds[i], FileFor(a)); if (File.Exists(q)) return q; } }
            if (a.Id == "stock") { string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); string[] cand = { Path.Combine(dl, "stock-aza3.bin"), Path.Combine(dl, "fit3-flasher-main\\fit3-flasher-main\\firmware\\stock-aza3.bin") }; foreach (string c in cand) if (File.Exists(c)) return c; }
            return null;
        }
        public static Dictionary<string, string> ReadSums(string folder)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); string f = Path.Combine(folder, "SHA256SUMS.txt");
            if (File.Exists(f)) foreach (string line in File.ReadAllLines(f)) { int sp = line.IndexOf(' '); if (sp > 0) d[line.Substring(sp).Trim().TrimStart('*')] = line.Substring(0, sp).ToLowerInvariant(); }
            return d;
        }
    }
}
