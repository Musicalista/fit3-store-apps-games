using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Fit3Manager
{
    /// <summary>"Criar app": edit C source (templates and the 22 shipped apps as examples), compile with the Arm toolchain (AppBuilder) and optionally install on the watch.</summary>
    public sealed partial class MainForm
    {
        TextBox cId, cPt, cEn, cBg, cFg, cCode, cOut; ComboBox cTpl; Label cTool; string toolBin; readonly Dictionary<string, string> tplFiles = new Dictionary<string, string>();

        static string SdkDir()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory; string[] c = { Path.Combine(b, "sdk"), Path.Combine(Path.GetDirectoryName(b.TrimEnd('\\', '/')) ?? b, "sdk") };
            foreach (string d in c) if (File.Exists(Path.Combine(d, "f3app.h"))) return d; return c[0];
        }
        static string AppsOutDir()
        {
            string d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apps");
            try { Directory.CreateDirectory(d); string t = Path.Combine(d, ".w"); File.WriteAllText(t, "x"); File.Delete(t); return d; }
            catch (Exception) { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fit3Manager"), "apps"); }
        }
        Panel PageCreator()
        {
            Panel p = new Panel(); int y = 0;
            Panel c1 = Section(p, Lang.T("1 · Comece por um modelo"), Lang.T("Escolha um modelo ou um dos 22 apps do relógio como exemplo, clique em Carregar e edite o código. Os apps usam C simples e as funções do relógio (desenhar, tocar, vibrar, hora, bateria, arquivos)."), 820, ref y); c1.Height = 190;
            cTpl = new ComboBox(); cTpl.DropDownStyle = ComboBoxStyle.DropDownList; cTpl.Left = 16; cTpl.Top = 92; cTpl.Width = 380;
            string sdk = SdkDir(); string td = Path.Combine(sdk, "templates"), ed = Path.Combine(sdk, "examples");
            if (Directory.Exists(td)) foreach (string f in Directory.GetFiles(td, "*.c")) { if (f.EndsWith(".en.c")) continue; string n = Lang.T("Modelo: ") + Lang.T(Path.GetFileNameWithoutExtension(f).Replace("modelo-", "").Replace('-', ' ')); string en = f.Substring(0, f.Length - 2) + ".en.c"; tplFiles[n] = Lang.En && File.Exists(en) ? en : f; cTpl.Items.Add(n); }
            if (Directory.Exists(ed)) foreach (string f in Directory.GetFiles(ed, "*.c")) { string n = Lang.T("Exemplo: ") + Path.GetFileNameWithoutExtension(f); tplFiles[n] = f; cTpl.Items.Add(n); }
            if (cTpl.Items.Count > 0) cTpl.SelectedIndex = 0; c1.Controls.Add(cTpl);
            Button load = Ui.Primary(Lang.T("Carregar"), delegate { LoadTemplate(); }); load.Left = 410; load.Top = 86; c1.Controls.Add(load);
            toolBin = AppBuilder.FindToolchain(); cTool = Ui.Lbl("", 9f, true, Ui.Muted); cTool.Left = 16; cTool.Top = 134; c1.Controls.Add(cTool); ShowTool();
            Button pick = Ui.Soft(Lang.T("Escolher pasta do compilador"), delegate { PickToolchain(); }); pick.Left = 16; pick.Top = 152; pick.Height = 28; pick.Font = Ui.F(9f, true); c1.Controls.Add(pick);
            y += 204;
            Panel c2 = Section(p, Lang.T("2 · Identificação do app"), Lang.T("O identificador vira o nome do arquivo (/user/apps/<id>.f3a): 1 a 11 letras minúsculas, números, _ ou -. Os nomes aparecem no relógio (até 15 letras, sem acento). As cores são o fundo e o texto do ícone (RRGGBB)."), 820, ref y); c2.Height = 150;
            cId = Field(c2, Lang.T("Identificador"), 16, 96, 130, "meuapp"); cPt = Field(c2, Lang.T("Nome (PT)"), 160, 96, 150, Lang.T("Meu app")); cEn = Field(c2, Lang.T("Nome (EN)"), 324, 96, 150, "My app"); cBg = Field(c2, Lang.T("Cor de fundo"), 488, 96, 100, "D0DCF8"); cFg = Field(c2, Lang.T("Cor do texto"), 602, 96, 100, "3454A8");
            y += 164;
            Panel c3 = Ui.Card(820, 430); c3.Left = 0; c3.Top = y; Label t3 = Ui.Lbl(Lang.T("3 · Código (C)"), 12f, true, Ui.Text); t3.Left = 16; t3.Top = 12; c3.Controls.Add(t3);
            cCode = new TextBox(); cCode.Multiline = true; cCode.AcceptsTab = true; cCode.AcceptsReturn = true; cCode.WordWrap = false; cCode.ScrollBars = ScrollBars.Both; cCode.Font = new Font("Consolas", 10f); cCode.Left = 16; cCode.Top = 42; cCode.Width = 784; cCode.Height = 372; cCode.BackColor = Color.FromArgb(250, 251, 255); c3.Controls.Add(cCode); p.Controls.Add(c3);
            y += 444;
            Panel c4 = Ui.Card(820, 330); c4.Left = 0; c4.Top = y; Label t4 = Ui.Lbl(Lang.T("4 · Compilar e instalar"), 12f, true, Ui.Text); t4.Left = 16; t4.Top = 12; c4.Controls.Add(t4);
            FlowLayoutPanel fb = Ui.Row(); fb.Left = 16; fb.Top = 44;
            fb.Controls.Add(Ui.Primary(Lang.T("Compilar"), delegate { BuildApp(false); })); fb.Controls.Add(Ui.Primary(Lang.T("Compilar e instalar no relógio"), delegate { BuildApp(true); })); fb.Controls.Add(Ui.Soft(Lang.T("Abrir a pasta dos apps"), delegate { try { System.Diagnostics.Process.Start("explorer.exe", AppsOutDir()); } catch (Exception) { } }));
            c4.Controls.Add(fb);
            cOut = new TextBox(); cOut.Multiline = true; cOut.ReadOnly = true; cOut.ScrollBars = ScrollBars.Vertical; cOut.Font = new Font("Consolas", 9.5f); cOut.Left = 16; cOut.Top = 96; cOut.Width = 784; cOut.Height = 214; cOut.BackColor = Color.White; c4.Controls.Add(cOut); p.Controls.Add(c4);
            LoadTemplate(); return p;
        }
        static TextBox Field(Panel c, string label, int x, int y, int w, string def)
        {
            Label l = Ui.Lbl(label, 9f, true, Ui.Muted); l.Left = x; l.Top = y - 20; c.Controls.Add(l); TextBox t = new TextBox(); t.Left = x; t.Top = y; t.Width = w; t.Text = def; t.Font = Ui.F(10f, false); c.Controls.Add(t); return t;
        }
        void ShowTool()
        {
            if (toolBin != null) { cTool.Text = Lang.T("Compilador encontrado: ") + toolBin; cTool.ForeColor = Ui.Ok; }
            else { cTool.Text = Lang.T("Compilador ARM não encontrado. Instale o \"Arm GNU Toolchain\" (arm-none-eabi) e escolha a pasta bin abaixo."); cTool.ForeColor = Ui.Bad; }
        }
        void PickToolchain()
        {
            FolderBrowserDialog d = new FolderBrowserDialog(); d.Description = Lang.T("Pasta bin do Arm GNU Toolchain (onde está arm-none-eabi-gcc.exe)");
            if (d.ShowDialog() == DialogResult.OK) { if (File.Exists(Path.Combine(d.SelectedPath, "arm-none-eabi-gcc.exe"))) { toolBin = d.SelectedPath; Environment.SetEnvironmentVariable("ARM_GNU_BIN", toolBin); } else MessageBox.Show(Lang.T("Não achei arm-none-eabi-gcc.exe nessa pasta."), "Fit3 Manager"); ShowTool(); }
        }
        void LoadTemplate()
        {
            string f; if (cTpl == null || cTpl.SelectedItem == null || !tplFiles.TryGetValue((string)cTpl.SelectedItem, out f)) return;
            cCode.Text = File.ReadAllText(f, Encoding.UTF8).Replace("\r\n", "\n").Replace("\n", "\r\n"); string n = Path.GetFileNameWithoutExtension(f);
            if (((string)cTpl.SelectedItem).StartsWith(Lang.T("Modelo"))) { cId.Text = "meuapp"; cPt.Text = Lang.T("Meu app"); cEn.Text = "My app"; } else { cId.Text = n.Length > 11 ? n.Substring(0, 11) : n; cPt.Text = n; cEn.Text = n; }
        }
        void BuildApp(bool install)
        {
            if (busy) return; if (toolBin == null) { MessageBox.Show(Lang.T("Compilador ARM não encontrado. Instale o Arm GNU Toolchain e escolha a pasta bin."), "Fit3 Manager"); return; }
            string id = cId.Text.Trim(), pt = cPt.Text.Trim(), en = cEn.Text.Trim(), bg = cBg.Text.Trim().ToUpperInvariant(), fg = cFg.Text.Trim().ToUpperInvariant();
            if (!AppBuilder.ValidId(id)) { MessageBox.Show(Lang.T("Identificador inválido: use de 1 a 11 letras minúsculas, números, _ ou -."), "Fit3 Manager"); return; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(bg, "^[0-9A-F]{6}$") || !System.Text.RegularExpressions.Regex.IsMatch(fg, "^[0-9A-F]{6}$")) { MessageBox.Show(Lang.T("As cores devem ter 6 dígitos hexadecimais (por exemplo 3454A8)."), "Fit3 Manager"); return; }
            string outDir = AppsOutDir(), srcDir = Path.Combine(outDir, "src"); Directory.CreateDirectory(srcDir); string src = Path.Combine(srcDir, id + ".c"), outFile = Path.Combine(outDir, id + ".f3a"); string code = cCode.Text, sdk = SdkDir(), bin = toolBin;
            File.WriteAllText(src, code, new UTF8Encoding(false)); cOut.Text = Lang.T("Compilando ") + id + "..."; Begin(); Status(Lang.T("Compilando ") + id + "...", -1);
            new Thread(delegate()
            {
                AppBuilder.Result r = AppBuilder.Build(bin, sdk, src, id, pt, en, outFile);
                if (r.Ok) File.WriteAllText(Path.Combine(outDir, id + ".meta"), id + ";" + bg + ";" + fg + ";" + pt.Replace(';', ' ') + ";" + en.Replace(';', ' ') + "\n", new UTF8Encoding(false));
                Post(delegate
                {
                    cOut.Text = r.Log + (r.Ok ? Environment.NewLine + Lang.T("Salvo em ") + outFile : ""); Log(Lang.T("Criar app ") + id + ": " + (r.Ok ? "OK, " + r.FileSize + " bytes" : Lang.T("falhou")));
                    Status(r.Ok ? id + Lang.T(" compilado (") + r.FileSize + " bytes)." : Lang.T("A compilação falhou: veja o resultado."), r.Ok ? 1000 : 0); busy = false; RefreshChip();
                    if (r.Ok) { FillLight(); if (install) { foreach (Catalog.Light a in light) if (a.Id == id) { InstallLightApp(a); break; } } }
                });
            }).Start();
        }
    }
}
