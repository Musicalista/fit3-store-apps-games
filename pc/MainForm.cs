using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Fit3Manager
{
    public sealed partial class MainForm : Form
    {
        // layout
        Panel side, content, header, bottom, logPanel; Panel[] pages; Button[] nav; Label hTitle, hSub, chip, status; ProgressBar bar; TextBox txtLog, txtOut, txtMac, txtPath, txtSearch; ComboBox cbDev, cbLang; Label lblHomeState, lblFolder;
        FlowLayoutPanel flowStore, flowLight, chips; HashSet<string> picked = new HashSet<string>(); Label lblPicked; TrackBar tbBright; Button btnTestHome;
        // state
        List<BtDevice> devices = new List<BtDevice>(); string folder; volatile bool busy; bool watchOk; Watch watch; readonly SynchronizationContext ui;
        List<App> shown = new List<App>(); List<Catalog.Light> light = new List<Catalog.Light>(); List<string> installed = new List<string>(); bool installedKnown;
        static readonly string[] PageNames = { "Início", "Loja de apps", "Apps leves", "Jogos e arquivos", "Relógio", "Criar app", "Avançado" };
        static readonly string[] PageSubs = { "Conecte o relógio e escolha o que colocar nele.", "Escolha o que você quer no relógio. Cada opção é um firmware já verificado.", "Apps pequenos que se instalam em segundos, sem regravar o firmware.", "Envie jogos (ROMs) e faça backup dos seus saves.", "Informações e ajustes do relógio.", "Escreva um app em C, compile com um clique e instale no relógio.", "Para quem já tem um pacote FWD próprio." };

        public MainForm()
        {
            ui = SynchronizationContext.Current; Text = "Fit3 Manager - gerenciador do Galaxy Fit3"; Width = 1120; Height = 690; MinimumSize = new Size(980, 600); StartPosition = FormStartPosition.CenterScreen; Font = Ui.F(10f, false); BackColor = Ui.Bg;
            folder = Catalog.FindFolder(); BuildUi(); LoadDevices(); FillStore(); FillLight(); ShowPage(0); { string[] ca = Environment.GetCommandLineArgs(); for (int i = 1; i + 1 < ca.Length; i++) if (ca[i] == "--page") { int pg; if (int.TryParse(ca[i + 1], out pg) && pg >= 0 && pg < PageNames.Length) ShowPage(pg); } else if (ca[i] == "--shot") { string shotPath = ca[i + 1]; System.Windows.Forms.Timer st = new System.Windows.Forms.Timer(); st.Interval = 2500; st.Tick += delegate { st.Stop(); using (Bitmap bm = new Bitmap(Width, Height)) { DrawToBitmap(bm, new Rectangle(0, 0, Width, Height)); bm.Save(shotPath, System.Drawing.Imaging.ImageFormat.Png); } Close(); }; st.Start(); } } Log("Fit3 Manager 1.1. Não oficial; gravar firmware pode inutilizar o relógio.");
        }

        // ------------------------------------------------------------------------------------------ shell
        void BuildUi()
        {
            side = new Panel(); side.Dock = DockStyle.Left; side.Width = 210; side.BackColor = Ui.Side;
            Label logo = Ui.Lbl("Fit3 Manager", 15f, true, Color.White); logo.Left = 18; logo.Top = 20; side.Controls.Add(logo);
            Label sub = Ui.Lbl("Galaxy Fit3 · SM-R390", 8.5f, false, Color.FromArgb(160, 168, 205)); sub.Left = 20; sub.Top = 50; side.Controls.Add(sub);
            nav = new Button[PageNames.Length];
            for (int i = 0; i < nav.Length; i++)
            {
                int idx = i; Button b = new Button(); b.Text = "   " + PageNames[i]; b.TextAlign = ContentAlignment.MiddleLeft; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = Ui.Side; b.ForeColor = Color.White; b.Font = Ui.F(11f, false);
                b.Left = 0; b.Top = 96 + i * 50; b.Width = 210; b.Height = 46; b.Cursor = Cursors.Hand; b.FlatAppearance.MouseOverBackColor = Ui.SideHot; b.Click += delegate { ShowPage(idx); }; nav[i] = b; side.Controls.Add(b);
            }
            Label warn = Ui.Wrap("Não oficial. Gravar firmware pode deixar o relógio inutilizável: leia os avisos antes de instalar.", 8.5f, Color.FromArgb(160, 168, 205), 176); warn.Left = 18; warn.Top = 500; warn.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; side.Controls.Add(warn);

            header = new Panel(); header.Dock = DockStyle.Top; header.Height = 86; header.BackColor = Color.White;
            hTitle = Ui.Lbl("", 18f, true, Ui.Text); hTitle.Left = 28; hTitle.Top = 12; hSub = Ui.Lbl("", 10f, false, Ui.Muted); hSub.Left = 30; hSub.Top = 50;
            chip = Ui.Lbl("", 10f, true, Ui.Muted); chip.Anchor = AnchorStyles.Top | AnchorStyles.Right; chip.Top = 28; header.Controls.AddRange(new Control[] { hTitle, hSub, chip });
            header.Resize += delegate { chip.Left = header.Width - chip.Width - 28; };

            bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 44; bottom.BackColor = Color.White;
            status = Ui.Lbl("Pronto.", 9.5f, false, Ui.Text); status.Left = 18; status.Top = 12; status.AutoSize = false; status.Width = 640; status.Height = 22; status.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            bar = new ProgressBar(); bar.Maximum = 1000; bar.Width = 220; bar.Height = 12; bar.Top = 16; bar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button det = Ui.Soft("Detalhes", delegate { logPanel.Visible = !logPanel.Visible; }); det.Height = 28; det.Top = 8; det.Anchor = AnchorStyles.Top | AnchorStyles.Right; det.Font = Ui.F(9f, true);
            bottom.Controls.AddRange(new Control[] { status, bar, det }); bottom.Resize += delegate { det.Left = bottom.Width - det.Width - 14; bar.Left = det.Left - bar.Width - 14; status.Width = Math.Max(100, bar.Left - 30); };
            logPanel = new Panel(); logPanel.Dock = DockStyle.Bottom; logPanel.Height = 190; logPanel.Visible = false; logPanel.BackColor = Ui.Bg; logPanel.Padding = new Padding(10);
            txtLog = new TextBox(); txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.Dock = DockStyle.Fill; txtLog.Font = new Font("Consolas", 9f); txtLog.BackColor = Color.White; logPanel.Controls.Add(txtLog);

            content = new Panel(); content.Dock = DockStyle.Fill; content.BackColor = Ui.Bg;
            pages = new Panel[] { PageHome(), PageStore(), PageLight(), PageFiles(), PageWatch(), PageCreator(), PageAdvanced() };
            foreach (Panel p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; p.AutoScroll = true; p.Padding = new Padding(28, 22, 20, 20); content.Controls.Add(p); }
            Controls.Add(content); Controls.Add(logPanel); Controls.Add(bottom); Controls.Add(header); Controls.Add(side);
            RefreshChip();
        }
        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Length; k++) { pages[k].Visible = k == i; nav[k].BackColor = k == i ? Ui.Accent : Ui.Side; nav[k].Font = Ui.F(11f, k == i); }
            hTitle.Text = PageNames[i]; hSub.Text = PageSubs[i];
            if (i == 1) FillStore(); if (i == 2) FillLight();
        }
        void RefreshChip()
        {
            chip.Text = watchOk ? "●  Relógio acessível" : "●  Relógio não testado"; chip.ForeColor = watchOk ? Ui.Ok : Ui.Muted; chip.Left = Math.Max(0, header.Width - chip.Width - 28);
            if (lblHomeState != null) { lblHomeState.Text = watchOk ? "Tudo certo: o relógio respondeu. Escolha o que instalar." : "Ainda não testado. Toque em \"Testar conexão\"."; lblHomeState.ForeColor = watchOk ? Ui.Ok : Ui.Muted; }
        }
        void Log(string s) { string line = DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n"; Post(delegate { txtLog.AppendText(line); }); }
        void Post(Action a) { ui.Post(delegate { a(); }, null); }
        void Status(string s, int permille) { Post(delegate { status.Text = s; if (permille >= 0) bar.Value = Math.Max(0, Math.Min(1000, permille)); }); }
        void Begin() { busy = true; }
        void End() { Post(delegate { busy = false; RefreshChip(); }); }
        void Fail(Exception e, bool device) { Log("Erro: " + e.Message); Status("Falhou: " + e.Message + (device ? "  O relógio não foi alterado." : ""), -1); Post(delegate { MessageBox.Show(e.Message + (device ? "\n\nO relógio não foi alterado." : ""), "Fit3 Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning); }); }

        // ------------------------------------------------------------------------------------------ pages
        Panel Section(Panel page, string title, string text, int width, ref int y)
        {
            Panel c = Ui.Card(width, 100); c.Left = 0; c.Top = y; Label t = Ui.Lbl(title, 12f, true, Ui.Text); t.Left = 16; t.Top = 12; c.Controls.Add(t);
            if (text != null) { Label l = Ui.Wrap(text, 9.5f, Ui.Muted, width - 34); l.Left = 16; l.Top = 40; c.Controls.Add(l); }
            page.Controls.Add(c); return c;
        }
        Panel PageHome()
        {
            Panel p = new Panel(); int y = 0;
            Panel wc = Section(p, "Antes de qualquer coisa: modelo compatível", "Só o Galaxy Fit3 SM-R390 com o firmware R390XXU0AZA3 é suportado. Outros modelos, variantes regionais e outras versões NÃO são suportados e não foram testados: gravar firmware neles pode causar um ciclo de reinicialização ou deixar o relógio inutilizável. Confira em Configurações > Sobre > Informações do software.", 820, ref y); wc.Height = 130; wc.BackColor = Color.FromArgb(253, 236, 234); y += 154;
            Panel c1 = Section(p, "1 · Conecte o relógio", "Pareie o Galaxy Fit3 com este PC no Bluetooth do Windows, feche o Galaxy Wearable e deixe o relógio perto.", 820, ref y); c1.Height = 190;
            Label l = Ui.Lbl("Relógio:", 10f, true, Ui.Text); l.Left = 16; l.Top = 84; c1.Controls.Add(l);
            cbDev = new ComboBox(); cbDev.DropDownStyle = ComboBoxStyle.DropDownList; cbDev.Left = 90; cbDev.Top = 80; cbDev.Width = 420; c1.Controls.Add(cbDev);
            Button r = Ui.Soft("Atualizar", delegate { LoadDevices(); }); r.Left = 520; r.Top = 76; r.Height = 32; c1.Controls.Add(r);
            Label l2 = Ui.Lbl("ou digite o endereço (MAC):", 9f, false, Ui.Muted); l2.Left = 16; l2.Top = 124; c1.Controls.Add(l2); txtMac = new TextBox(); txtMac.Left = 200; txtMac.Top = 120; txtMac.Width = 150; c1.Controls.Add(txtMac);
            btnTestHome = Ui.Primary("Testar conexão", delegate { TestConnection(); }); btnTestHome.Left = 380; btnTestHome.Top = 112; c1.Controls.Add(btnTestHome);
            lblHomeState = Ui.Lbl("", 10f, true, Ui.Muted); lblHomeState.Left = 16; lblHomeState.Top = 156; c1.Controls.Add(lblHomeState);
            y += 204;
            Panel c2 = Section(p, "2 · Escolha o que colocar", "Os apps e jogos ficam dentro do firmware. Na Loja você marca o que quer e o programa escolhe o pacote certo. Já os apps leves e os jogos (ROMs) entram em segundos.", 820, ref y); c2.Height = 150;
            FlowLayoutPanel f = Ui.Row(); f.Left = 16; f.Top = 100; f.Controls.Add(Ui.Primary("Abrir a Loja de apps", delegate { ShowPage(1); })); f.Controls.Add(Ui.Soft("Apps leves", delegate { ShowPage(2); })); f.Controls.Add(Ui.Soft("Jogos e arquivos", delegate { ShowPage(3); })); c2.Controls.Add(f);
            y += 164;
            Panel c3 = Section(p, "Dicas importantes", "•  Se a conexão falhar: o relógio costuma aceitar um cliente por vez. Desligue o Bluetooth do celular, feche o Galaxy Wearable e janelas do navegador que usem o relógio, reinicie o relógio e tente de novo.\r\n•  Antes de instalar um firmware, deixe o relógio com 50% ou mais de bateria.\r\n•  Tenha o firmware original à mão (Loja > Original Samsung) para voltar ao padrão.", 820, ref y); c3.Height = 170;
            return p;
        }
        Panel PageStore()
        {
            Panel p = new Panel(); FlowLayoutPanel top = new FlowLayoutPanel(); top.Dock = DockStyle.Top; top.Height = 118; top.FlowDirection = FlowDirection.TopDown; top.WrapContents = false; top.BackColor = Ui.Bg;
            FlowLayoutPanel r1 = Ui.Row(); r1.Margin = new Padding(0); txtSearch = new TextBox(); txtSearch.Width = 260; txtSearch.Font = Ui.F(10.5f, false); txtSearch.TextChanged += delegate { FillStore(); };
            Label sl = Ui.Lbl("Buscar:  ", 10f, true, Ui.Text); sl.Margin = new Padding(0, 6, 0, 0); r1.Controls.Add(sl); r1.Controls.Add(txtSearch);
            lblFolder = Ui.Lbl("", 9f, false, Ui.Muted); lblFolder.Margin = new Padding(20, 8, 8, 0); r1.Controls.Add(lblFolder); Button bf = Ui.Soft("Trocar pasta dos pacotes", delegate { PickFolder(); }); bf.Height = 30; bf.Font = Ui.F(9f, true); r1.Controls.Add(bf);
            Label q = Ui.Lbl("Quero ter no relógio (toque para marcar):", 10f, true, Ui.Text); q.Margin = new Padding(0, 10, 0, 4);
            chips = Ui.Row(); chips.Margin = new Padding(0); chips.MaximumSize = new Size(900, 0);
            for (int i = 0; i < Catalog.FeatNames.Length; i++)
            {
                CheckBox cb = new CheckBox(); cb.Appearance = Appearance.Button; cb.FlatStyle = FlatStyle.Flat; cb.FlatAppearance.BorderSize = 0; cb.FlatAppearance.CheckedBackColor = Ui.Accent; cb.BackColor = Ui.Chip; cb.ForeColor = Ui.Text; cb.Font = Ui.F(9f, true); cb.AutoSize = true; cb.Padding = new Padding(8, 3, 8, 3); cb.Margin = new Padding(0, 0, 8, 6); cb.Cursor = Cursors.Hand;
                cb.Text = ChipName(i); cb.Tag = i; cb.CheckedChanged += delegate(object s, EventArgs e) { CheckBox c = (CheckBox)s; c.BackColor = c.Checked ? Ui.Accent : Ui.Chip; c.ForeColor = c.Checked ? Color.White : Ui.Text; FillStore(); }; chips.Controls.Add(cb);
            }
            top.Controls.Add(r1); top.Controls.Add(q); top.Controls.Add(chips);
            flowStore = new FlowLayoutPanel(); flowStore.Dock = DockStyle.Fill; flowStore.AutoScroll = true; flowStore.BackColor = Ui.Bg; flowStore.Padding = new Padding(0, 8, 0, 0);
            p.Controls.Add(flowStore); p.Controls.Add(top); p.AutoScroll = false; return p;
        }
        static string ChipName(int i) { string[] n = { "Internet", "Minijogos", "Mais jogos", "Utilitários", "Game Boy", "Mundo de blocos 3D", "FPS (CS)", "Doom", "IA + Discador", "Meus apps" }; return n[i]; }
        Panel PageLight()
        {
            Panel p = new Panel(); FlowLayoutPanel top = new FlowLayoutPanel(); top.Dock = DockStyle.Top; top.Height = 96; top.FlowDirection = FlowDirection.TopDown; top.WrapContents = false; top.BackColor = Ui.Bg;
            Label l = Ui.Wrap("Apps leves são pequenos programas (.f3a) que entram em /user/apps do relógio em segundos. Precisam de um firmware com o launcher \"Meus apps\" (na Loja: pacotes \"Loja de apps ...\"). Para criar o seu: pasta apps\\ (appcc.py).", 9.5f, Ui.Muted, 820);
            FlowLayoutPanel r = Ui.Row(); r.Margin = new Padding(0, 8, 0, 0); r.Controls.Add(Ui.Soft("Ver o que já está no relógio", delegate { LoadInstalled(); })); r.Controls.Add(Ui.Soft("Marcar todos", delegate { foreach (Catalog.Light a in light) picked.Add(a.Id); FillLight(); })); r.Controls.Add(Ui.Soft("Desmarcar", delegate { picked.Clear(); FillLight(); })); r.Controls.Add(Ui.Primary("Instalar marcados", delegate { InstallPicked(); })); lblPicked = Ui.Lbl("", 9.5f, true, Ui.Muted); lblPicked.Margin = new Padding(8, 10, 0, 0); r.Controls.Add(lblPicked); top.Controls.Add(l); top.Controls.Add(r);
            flowLight = new FlowLayoutPanel(); flowLight.Dock = DockStyle.Fill; flowLight.AutoScroll = true; flowLight.BackColor = Ui.Bg; p.Controls.Add(flowLight); p.Controls.Add(top); p.AutoScroll = false; return p;
        }
        Panel PageFiles()
        {
            Panel p = new Panel(); FlowLayoutPanel f = new FlowLayoutPanel(); f.Dock = DockStyle.Fill; f.FlowDirection = FlowDirection.TopDown; f.WrapContents = false; f.AutoScroll = true; f.BackColor = Ui.Bg; p.Controls.Add(f); p.AutoScroll = false;
            f.Controls.Add(FileCard("Game Boy", "O emulador lê o jogo de /user/gb.gb. Use um jogo seu (.gb ou .gbc). O progresso é salvo em /user/gb.sav.",
                Ui.Primary("Enviar um jogo (ROM)...", delegate { SendUserFile("Jogos Game Boy|*.gb;*.gbc|Todos|*.*", "/user/gb.gb"); }), Ui.Soft("Baixar o save", delegate { GetUserFile("/user/gb.sav", "gb.sav"); }), Ui.Soft("Enviar um save...", delegate { SendUserFile("Save|*.sav|Todos|*.*", "/user/gb.sav"); })));
            f.Controls.Add(FileCard("Mundo de blocos", "O mundo fica salvo em /user/mc.sav. Faça um backup antes de regravar o firmware.",
                Ui.Primary("Fazer backup do mundo", delegate { GetUserFile("/user/mc.sav", "mundo-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sav"); }), Ui.Soft("Restaurar um mundo...", delegate { SendUserFile("Mundo|*.sav|Todos|*.*", "/user/mc.sav"); })));
            f.Controls.Add(FileCard("Anotações (Utilitários)", "As anotações do relógio ficam em /user/notes.txt.", Ui.Primary("Baixar as anotações", delegate { GetUserFile("/user/notes.txt", "anotacoes.txt"); }), Ui.Soft("Enviar anotações...", delegate { SendUserFile("Texto|*.txt|Todos|*.*", "/user/notes.txt"); })));
            Panel adv = FileCard("Arquivo qualquer (avançado)", "Só arquivos em /user/ (o sistema e a área de atualização ficam bloqueados).", null);
            txtPath = new TextBox(); txtPath.Width = 280; txtPath.Text = "/user/"; txtPath.Left = 16; txtPath.Top = 78; adv.Controls.Add(txtPath);
            Button b1 = Ui.Soft("Ler e salvar...", delegate { string pth = txtPath.Text.Trim(); string nm = Path.GetFileName(pth.Replace('/', '\\')); GetUserFile(pth, nm.Length > 0 ? nm : "arquivo.bin"); }); b1.Left = 310; b1.Top = 72; adv.Controls.Add(b1);
            Button b2 = Ui.Soft("Enviar arquivo para este caminho...", delegate { SendUserFile("Todos|*.*", txtPath.Text.Trim()); }); b2.Left = 440; b2.Top = 72; adv.Controls.Add(b2); adv.Height = 130; f.Controls.Add(adv);
            return p;
        }
        Panel FileCard(string title, string text, params Button[] btns)
        {
            Panel c = Ui.Card(820, 128); Label t = Ui.Lbl(title, 12f, true, Ui.Text); t.Left = 16; t.Top = 12; c.Controls.Add(t); Label l = Ui.Wrap(text, 9.5f, Ui.Muted, 780); l.Left = 16; l.Top = 38; c.Controls.Add(l);
            if (btns != null) { FlowLayoutPanel f = Ui.Row(); f.Left = 16; f.Top = 76; foreach (Button b in btns) f.Controls.Add(b); c.Controls.Add(f); }
            return c;
        }
        Panel PageWatch()
        {
            Panel p = new Panel(); FlowLayoutPanel f = new FlowLayoutPanel(); f.Dock = DockStyle.Fill; f.FlowDirection = FlowDirection.TopDown; f.WrapContents = false; f.AutoScroll = true; f.BackColor = Ui.Bg; p.Controls.Add(f); p.AutoScroll = false;
            Panel c1 = FileCard("Informações e testes", "Versão, bateria e endereço do relógio; uma notificação de teste; vibração.", Ui.Primary("Ver informações", delegate { Info(); }), Ui.Soft("Notificação de teste", delegate { RunAt(new string[] { "NOTICE=-a=Fit3 Manager: teste" }); }), Ui.Soft("Vibrar", delegate { RunAt(new string[] { "MOTOR_VIB=on" }); }), Ui.Soft("Parar vibração", delegate { RunAt(new string[] { "MOTOR_VIB=off" }); }));
            txtOut = new TextBox(); txtOut.Multiline = true; txtOut.ReadOnly = true; txtOut.Font = new Font("Consolas", 10f); txtOut.Width = 780; txtOut.Height = 120; txtOut.Left = 16; txtOut.Top = 124; txtOut.ScrollBars = ScrollBars.Vertical; c1.Controls.Add(txtOut); c1.Height = 262; f.Controls.Add(c1);
            Panel c2 = Ui.Card(820, 120); Label t2 = Ui.Lbl("Brilho da tela", 12f, true, Ui.Text); t2.Left = 16; t2.Top = 12; c2.Controls.Add(t2);
            tbBright = new TrackBar(); tbBright.Minimum = 1; tbBright.Maximum = 5; tbBright.Value = 3; tbBright.Left = 16; tbBright.Top = 44; tbBright.Width = 300; c2.Controls.Add(tbBright);
            Button ab = Ui.Primary("Aplicar", delegate { int[] v = { 20, 40, 60, 80, 100 }; RunAt(new string[] { "SCREENBRIGHT=" + v[tbBright.Value - 1] }); }); ab.Left = 330; ab.Top = 46; c2.Controls.Add(ab); f.Controls.Add(c2);
            Panel c3 = Ui.Card(820, 120); Label t3 = Ui.Lbl("Idioma do relógio", 12f, true, Ui.Text); t3.Left = 16; t3.Top = 12; c3.Controls.Add(t3);
            cbLang = new ComboBox(); cbLang.DropDownStyle = ComboBoxStyle.DropDownList; cbLang.Items.AddRange(new object[] { "Português (Brasil)", "English", "English (US)", "Português (Portugal)" }); cbLang.SelectedIndex = 0; cbLang.Left = 16; cbLang.Top = 52; cbLang.Width = 240; c3.Controls.Add(cbLang);
            Button al = Ui.Primary("Aplicar idioma", delegate { int[] ids = { 68, 30, 13, 52 }; RunAt(new string[] { "LANGUAGE=" + ids[cbLang.SelectedIndex] }); }); al.Left = 270; al.Top = 46; c3.Controls.Add(al); f.Controls.Add(c3);
            Panel c4 = Ui.Card(820, 110); Label t4 = Ui.Lbl("Reiniciar o relógio", 12f, true, Ui.Text); t4.Left = 16; t4.Top = 12; c4.Controls.Add(t4);
            Button rb = Ui.Danger("Reiniciar agora", delegate { if (MessageBox.Show("O relógio vai desligar e ligar de novo. Continuar?", "Reiniciar", MessageBoxButtons.YesNo) == DialogResult.Yes) RunAt(new string[] { "REBOOT" }); }); rb.Left = 16; rb.Top = 48; c4.Controls.Add(rb); f.Controls.Add(c4);
            return p;
        }
        Panel PageAdvanced()
        {
            Panel p = new Panel(); int y = 0;
            Panel c = Section(p, "Instalar um pacote FWD próprio", "Escolha qualquer pacote FWD da AZA3 (inclusive o original). Ele é verificado por inteiro antes de qualquer envio; um pacote inválido nunca é mandado ao relógio.", 820, ref y); c.Height = 150;
            Button b = Ui.Primary("Escolher pacote (.bin)...", delegate { PickFirmware(); }); b.Left = 16; b.Top = 96; c.Controls.Add(b);
            y += 164; Panel d = Section(p, "Antes de instalar", "1. Pareie o relógio com este PC e feche o Galaxy Wearable e janelas que usem Bluetooth com o relógio.\r\n2. Deixe o relógio com 50% ou mais de bateria, perto do PC.\r\n3. Confira em Configurações > Sobre > Informações do software que a versão é R390XXU0AZA3.\r\n4. Se já enviou outros pacotes e não sabe se ficaram no relógio, não inicie a instalação.\r\n5. Tenha o stock-aza3.bin à mão para voltar ao original.\r\n\r\nBaseado no fit3-flasher de yuriyurin (github.com/yuriyurin/fit3-flasher). Não afiliado à Samsung.", 820, ref y); d.Height = 260;
            return p;
        }

        // ------------------------------------------------------------------------------------------ connection
        void LoadDevices()
        {
            devices.Clear(); cbDev.Items.Clear();
            try { devices = Bt.Paired(); } catch (Exception e) { Log("Não consegui listar os dispositivos Bluetooth: " + e.Message); }
            int pick = 0; for (int i = 0; i < devices.Count; i++) { cbDev.Items.Add(devices[i].ToString()); if (devices[i].Name != null && (devices[i].Name.Contains("Fit3") || devices[i].Name.Contains("Galaxy Fit"))) pick = i; }
            if (devices.Count > 0) cbDev.SelectedIndex = pick; else cbDev.Items.Add("(nenhum dispositivo pareado: digite o endereço ao lado)");
        }
        bool GetAddress(out ulong addr)
        {
            addr = 0; if (txtMac.Text.Trim().Length > 0) { if (BtDevice.TryParse(txtMac.Text, out addr)) return true; MessageBox.Show("Endereço inválido. Use o formato AA:BB:CC:DD:EE:FF."); return false; }
            if (cbDev.SelectedIndex >= 0 && cbDev.SelectedIndex < devices.Count) { addr = devices[cbDev.SelectedIndex].Address; return true; }
            MessageBox.Show("Escolha o relógio pareado na tela Início (ou digite o endereço)."); ShowPage(0); return false;
        }
        Watch NewWatch()
        {
            ulong a; if (!GetAddress(out a)) return null;
            Watch w = new Watch(Bt.Factory(a)); w.Log = delegate(string s) { Log(s); }; w.Progress = delegate(string phase, long d, long t, long bps) { Status((phase == "upload" ? "Enviando: " : "Conferindo: ") + (d / 1024) + " / " + (t / 1024) + " KB", (int)(d * 1000 / Math.Max(1, t))); }; return w;
        }
        void TestConnection()
        {
            if (busy) return; watch = NewWatch(); if (watch == null) return; Begin(); watchOk = false; RefreshChip(); Status("Testando a conexão (tenta por até 30 s)...", 0); Watch w = watch; btnTestHome.Enabled = false;
            new Thread(delegate()
            {
                Exception last = null; int end = Environment.TickCount + 30000; int n = 0;
                while (unchecked(end - Environment.TickCount) > 0 && !w.IsAborted)
                {
                    n++; try { w.CheckConnection(); watchOk = true; last = null; break; } catch (Exception e) { last = e; Log("Tentativa " + n + ": " + e.Message); Status("Tentando conectar... (tentativa " + n + ")", -1); }
                    Thread.Sleep(2500);
                }
                Post(delegate { btnTestHome.Enabled = true; });
                if (watchOk) { Log("RFCOMM: abrir e fechar o serviço funcionou."); Status("Conexão verificada. Nada foi gravado.", 1000); }
                else { Log("Não conectou. Último erro: " + (last != null ? last.Message : "-")); Status("Não conectou.", 0); Post(delegate { MessageBox.Show("Não consegui falar com o relógio.\n\n" + (last != null ? last.Message + "\n\n" : "") + "O relógio costuma aceitar um cliente por vez:\n• desligue o Bluetooth do celular e feche o Galaxy Wearable;\n• feche janelas do navegador que usem o relógio;\n• reinicie o relógio (desligue e ligue);\n• deixe-o perto do PC e tente de novo.", "Não conectou", MessageBoxButtons.OK, MessageBoxIcon.Information); }); }
                End();
            }).Start();
        }
        void RunAt(string[] cmds)
        {
            if (busy) return; watch = NewWatch(); if (watch == null) return; Begin(); Status("Falando com o relógio...", -1); Watch w = watch;
            new Thread(delegate()
            {
                try { string[] r = w.AtMany(cmds, 3000); watchOk = true; for (int i = 0; i < cmds.Length; i++) { string line = cmds[i] + " -> " + (r[i].Length == 0 ? "(sem resposta)" : r[i].Replace("\r", "").Replace("\n", " ")); Log(line); Post(delegate { if (txtOut != null) txtOut.AppendText(line + "\r\n"); }); } Status("Pronto.", 0); }
                catch (Exception e) { Fail(e, false); }
                End();
            }).Start();
        }
        void Info() { RunAt(new string[] { "GET_DEVINFO", "SWVER", "BTMAC", "GETBATPERCENT", "GETVBAT" }); }

        // ------------------------------------------------------------------------------------------ files
        void GetUserFile(string path, string suggested)
        {
            if (busy) return; SaveFileDialog d = new SaveFileDialog(); d.FileName = suggested; if (d.ShowDialog() != DialogResult.OK) return;
            string target = d.FileName; watch = NewWatch(); if (watch == null) return; Begin(); Status("Lendo " + path + "...", 0); Watch w = watch;
            new Thread(delegate()
            {
                try
                {
                    byte[] data = w.ReadFile(path, delegate(long a, long b) { Status("Lendo " + path + ": " + a + " / " + b + " bytes", (int)(a * 1000 / Math.Max(1, b))); });
                    if (data == null) { Log(path + ": não existe no relógio."); Status(path + " não existe no relógio.", 0); Post(delegate { MessageBox.Show(path + " não existe no relógio (ainda).", "Fit3 Manager"); }); }
                    else { File.WriteAllBytes(target, data); Log(path + " salvo em " + target + " (" + data.Length + " bytes)"); Status("Salvo: " + target, 1000); }
                }
                catch (Exception e) { Fail(e, false); }
                End();
            }).Start();
        }
        void SendUserFile(string filter, string path)
        {
            if (busy) return; OpenFileDialog d = new OpenFileDialog(); d.Filter = filter; if (d.ShowDialog() != DialogResult.OK) return;
            byte[] data = File.ReadAllBytes(d.FileName); if (data.Length > 8 * 1024 * 1024) { MessageBox.Show("Arquivo grande demais (máx. 8 MB)."); return; }
            if (MessageBox.Show("Enviar " + Path.GetFileName(d.FileName) + " (" + data.Length + " bytes) para " + path + " no relógio? Um arquivo com o mesmo nome será substituído.", "Enviar arquivo", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            watch = NewWatch(); if (watch == null) return; Begin(); Status("Enviando para " + path + "...", 0); Watch w = watch;
            new Thread(delegate()
            {
                try { w.WriteUserFile(path, data, delegate(long a, long b, long bps) { Status("Enviando: " + a / 1024 + " / " + b / 1024 + " KB · " + bps / 1024 + " KB/s", (int)(a * 1000 / Math.Max(1, b))); }); Log("Enviado para " + path + " (" + data.Length + " bytes)."); Status("Enviado para " + path + ".", 1000); Post(delegate { MessageBox.Show("Enviado para " + path + ".", "Fit3 Manager"); }); }
                catch (Exception e) { Fail(e, true); }
                End();
            }).Start();
        }

        // ------------------------------------------------------------------------------------------ store (firmware packages)
        void PickFolder() { FolderBrowserDialog d = new FolderBrowserDialog(); d.Description = "Pasta com os arquivos fit3-*.bin (e SHA256SUMS.txt)"; if (d.ShowDialog() == DialogResult.OK) { folder = d.SelectedPath; FillStore(); FillLight(); } }
        Color Stripe(App a)
        {
            if (a.Id == "stock") return Ui.Muted; if ((a.F & Feat.Blocks) != 0) return Color.FromArgb(120, 84, 200); if ((a.F & Feat.Cs) != 0) return Ui.Bad; if ((a.F & Feat.Apps) != 0) return Color.FromArgb(40, 160, 190); if ((a.F & Feat.Internet) != 0) return Ui.Accent; return Ui.Ok;
        }
        void FillStore()
        {
            if (flowStore == null || chips == null) return; lblFolder.Text = "Pacotes em: " + Shorten(folder, 46);
            Feat want = Feat.None; foreach (Control c in chips.Controls) { CheckBox cb = c as CheckBox; if (cb != null && cb.Checked) want |= Catalog.FeatFlags[(int)cb.Tag]; }
            string q = txtSearch != null ? txtSearch.Text.Trim().ToLowerInvariant() : "";
            flowStore.SuspendLayout(); flowStore.Controls.Clear(); shown.Clear();
            foreach (App a in Catalog.All)
            {
                if (a.Id != "stock" && (a.F & want) != want) continue; if (q.Length > 0 && (a.Title + " " + a.Desc).ToLowerInvariant().IndexOf(q) < 0) continue;
                shown.Add(a); flowStore.Controls.Add(StoreCard(a));
            }
            if (shown.Count == 0) { Label l = Ui.Wrap("Nenhum pacote reúne tudo isso: não cabe na flash do relógio. Desmarque algum item.", 11f, Ui.Muted, 600); flowStore.Controls.Add(l); }
            flowStore.ResumeLayout();
        }
        static string Shorten(string s, int n) { return s.Length <= n ? s : "..." + s.Substring(s.Length - n + 3); }
        Panel StoreCard(App a)
        {
            Panel c = Ui.Card(392, 214); string path = Catalog.PathFor(folder, a); Color col = Stripe(a);
            Panel stripe = new Panel(); stripe.Dock = DockStyle.Top; stripe.Height = 6; stripe.BackColor = col; c.Controls.Add(stripe);
            Label t = Ui.Lbl(a.Title, 11.5f, true, Ui.Text); t.Left = 14; t.Top = 16; t.AutoSize = false; t.Width = 360; t.Height = 24; c.Controls.Add(t);
            Label d = Ui.Wrap(a.Desc, 9.5f, Ui.Muted, 360); d.Left = 14; d.Top = 44; d.Height = 50; c.Controls.Add(d);
            StringBuilder tags = new StringBuilder(); for (int i = 0; i < Catalog.FeatFlags.Length; i++) if ((a.F & Catalog.FeatFlags[i]) != 0) { if (tags.Length > 0) tags.Append("  ·  "); tags.Append(ChipName(i)); }
            Label tg = Ui.Wrap(tags.ToString(), 8.5f, col, 360); tg.Left = 14; tg.Top = 98; tg.Height = 34; c.Controls.Add(tg);
            if (a.Use > 0)
            {
                Panel track = new Panel(); track.Left = 14; track.Top = 138; track.Width = 360; track.Height = 8; track.BackColor = Ui.Chip; Panel fill = new Panel(); fill.Height = 8; fill.Width = Math.Max(4, 360 * a.Use / 100); fill.BackColor = a.Use > 90 ? Ui.Bad : a.Use > 75 ? Ui.Warn : Ui.Ok; track.Controls.Add(fill); c.Controls.Add(track);
                Label u = Ui.Lbl("Usa " + a.Use + "% da área livre da memória do relógio", 8.5f, false, Ui.Muted); u.Left = 14; u.Top = 150; c.Controls.Add(u);
            }
            Label st = Ui.Lbl(path != null ? "✔ Arquivo encontrado" : "✖ Arquivo não encontrado", 9f, true, path != null ? Ui.Ok : Ui.Bad); st.Left = 14; st.Top = 178; c.Controls.Add(st);
            App app = a; Button b = path != null ? Ui.Primary(a.Id == "stock" ? "Restaurar" : "Instalar", delegate { InstallApp(app); }) : Ui.Soft("Ver onde está", delegate { MessageBox.Show("Arquivo esperado: " + Catalog.FileFor(app) + "\n\nColoque-o na pasta dos pacotes (" + folder + ") ou use \"Trocar pasta dos pacotes\".", "Arquivo não encontrado"); });
            b.Left = 270; b.Top = 168; b.Width = 106; b.AutoSize = false; b.Height = 34; c.Controls.Add(b);
            return c;
        }
        void InstallApp(App a)
        {
            if (busy) return; string path = Catalog.PathFor(folder, a); if (path == null) return;
            byte[] b; Fwd.Info inf; Cursor = Cursors.WaitCursor; Status("Verificando " + Path.GetFileName(path) + "...", 0);
            try
            {
                b = File.ReadAllBytes(path); inf = Fwd.Validate(b, null); string expect; if (Catalog.ReadSums(Path.GetDirectoryName(path)).TryGetValue(Path.GetFileName(path), out expect) && expect != inf.Sha256) throw new Fwd.Bad("O SHA-256 do arquivo não bate com o SHA256SUMS.txt da pasta. Não use este arquivo.");
                Log("Pacote " + a.Title + ": " + inf.Source + " -> " + inf.Target + "; " + inf.Count + " componentes; SHA-256 " + inf.Sha256 + (inf.Stock ? " (ORIGINAL Samsung)" : ""));
            }
            catch (Exception e) { Cursor = Cursors.Default; Fail(e, false); return; }
            Cursor = Cursors.Default; Status("Pacote verificado.", 1000);
            using (InstallWizard w = new InstallWizard(a.Title, b, inf, NewWatch, Log)) { w.ShowDialog(this); }
            watchOk = false; RefreshChip();
        }
        void PickFirmware()
        {
            if (busy) return; OpenFileDialog d = new OpenFileDialog(); d.Filter = "Pacote FWD (*.bin)|*.bin|Todos|*.*"; if (d.ShowDialog() != DialogResult.OK) return;
            byte[] b; Fwd.Info inf; Cursor = Cursors.WaitCursor;
            try { b = File.ReadAllBytes(d.FileName); inf = Fwd.Validate(b, null); Log("Pacote " + Path.GetFileName(d.FileName) + ": " + inf.Count + " componentes; SHA-256 " + inf.Sha256 + (inf.Stock ? " (ORIGINAL Samsung)" : "")); }
            catch (Exception e) { Cursor = Cursors.Default; Fail(e, false); return; }
            Cursor = Cursors.Default;
            using (InstallWizard w = new InstallWizard(Path.GetFileName(d.FileName), b, inf, NewWatch, Log)) { w.ShowDialog(this); }
        }

        // ------------------------------------------------------------------------------------------ light apps
        void LoadInstalled()
        {
            if (busy) return; watch = NewWatch(); if (watch == null) return; Begin(); Status("Lendo a lista de apps do relógio...", -1); Watch w = watch;
            new Thread(delegate()
            {
                try { installed = w.ReadIndex(); installedKnown = true; watchOk = true; Log("Apps no relógio: " + installed.Count); Status("Pronto.", 0); } catch (Exception e) { Fail(e, false); }
                Post(delegate { FillLight(); }); End();
            }).Start();
        }
        void FillLight()
        {
            if (flowLight == null) return; light = Catalog.LoadLight(folder); flowLight.SuspendLayout(); flowLight.Controls.Clear();
            foreach (Catalog.Light a in light)
            {
                Catalog.Light app = a; bool inst = false; foreach (string s in installed) if (s.StartsWith(a.Id + ";")) inst = true;
                Panel c = Ui.Card(392, 150); Color bg = ParseColor(a.Bg), fg = ParseColor(a.Fg);
                Panel ic = new Panel(); ic.Left = 14; ic.Top = 14; ic.Width = 54; ic.Height = 54; ic.BackColor = fg; Label ini = Ui.Lbl(a.Pt.Length > 0 ? a.Pt.Substring(0, 1).ToUpperInvariant() : "?", 20f, true, bg); ini.Left = 14; ini.Top = 8; ic.Controls.Add(ini); c.Controls.Add(ic);
                Label t = Ui.Lbl(a.Pt + " / " + a.En, 11.5f, true, Ui.Text); t.Left = 80; t.Top = 14; c.Controls.Add(t); Label z = Ui.Lbl(Path.GetFileName(a.Path) + "  ·  " + a.Size + " bytes", 9f, false, Ui.Muted); z.Left = 80; z.Top = 40; c.Controls.Add(z);
                Label sl2 = Ui.Lbl(inst ? "✔ Instalado no relógio" : (installedKnown ? "Não instalado" : "Estado no relógio: desconhecido"), 9f, true, inst ? Ui.Ok : Ui.Muted); sl2.Left = 80; sl2.Top = 62; c.Controls.Add(sl2);
                Button i1 = Ui.Primary(inst ? "Atualizar" : "Instalar", delegate { InstallLightApp(app); }); i1.Left = 14; i1.Top = 98; c.Controls.Add(i1); CheckBox pk = new CheckBox(); pk.Text = "Marcar"; pk.Font = Ui.F(9f, true); pk.ForeColor = Ui.Text; pk.AutoSize = true; pk.Left = 300; pk.Top = 14; pk.Checked = picked.Contains(app.Id); pk.CheckedChanged += delegate(object o, EventArgs e2) { if (((CheckBox)o).Checked) picked.Add(app.Id); else picked.Remove(app.Id); UpdatePicked(); }; c.Controls.Add(pk);
                Button r1 = Ui.Soft("Remover", delegate { RemoveLightApp(app); }); r1.Left = 140; r1.Top = 98; c.Controls.Add(r1); flowLight.Controls.Add(c);
            }
            if (light.Count == 0) flowLight.Controls.Add(Ui.Wrap("Nenhum arquivo .f3a encontrado na pasta apps\\ ao lado do programa.", 11f, Ui.Muted, 600));
            flowLight.ResumeLayout(); UpdatePicked();
        }
        static Color ParseColor(string h) { try { return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16)); } catch (Exception) { return Ui.Accent; } }
        void UpdatePicked() { if (lblPicked != null) lblPicked.Text = picked.Count + " marcado(s); o relógio aceita até 12 apps"; }
        /// <summary>Installs every ticked app one after another (one connection per file, like a single install).</summary>
        void InstallPicked()
        {
            if (busy) return; List<Catalog.Light> todo = new List<Catalog.Light>(); foreach (Catalog.Light a in light) if (picked.Contains(a.Id)) todo.Add(a);
            if (todo.Count == 0) { MessageBox.Show("Marque pelo menos um app (caixa \"Marcar\" em cada cartão).", "Fit3 Manager"); return; }
            StringBuilder names = new StringBuilder(); foreach (Catalog.Light a in todo) { if (names.Length > 0) names.Append(", "); names.Append(a.Pt); }
            if (MessageBox.Show("Instalar " + todo.Count + " app(s) no relógio?\n\n" + names + "\n\nOs apps rodam como programa nativo dentro do relógio: instale só apps em que você confia. É preciso que o relógio tenha o firmware com \"Meus apps\".", "Instalar apps", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            watch = NewWatch(); if (watch == null) return; Begin(); Watch w = watch;
            new Thread(delegate()
            {
                int done = 0;
                try
                {
                    foreach (Catalog.Light a in todo)
                    {
                        Catalog.Light app = a; byte[] data = File.ReadAllBytes(app.Path); int idx = done; Status("Instalando " + app.Pt + " (" + (done + 1) + " de " + todo.Count + ")...", 0);
                        w.InstallLight(app.Id, data, app.IndexLine(), delegate(long d, long t, long bps) { Status("Enviando " + app.Pt + " (" + (idx + 1) + " de " + todo.Count + "): " + d + " / " + t + " bytes", (int)(d * 1000 / Math.Max(1, t))); });
                        done++; Log("App instalado: " + app.Id); Thread.Sleep(1200);
                    }
                    installed = w.ReadIndex(); installedKnown = true; watchOk = true; Status(done + " app(s) instalado(s). No relógio: Apps extras > Meus apps.", 1000);
                    Post(delegate { MessageBox.Show(done + " app(s) instalado(s)!\n\nNo relógio abra Apps extras > Meus apps.", "Fit3 Manager"); });
                }
                catch (Exception e) { Fail(new IOException(done + " de " + todo.Count + " instalados. " + e.Message), false); try { installed = w.ReadIndex(); installedKnown = true; } catch (Exception) { } }
                Post(delegate { FillLight(); }); End();
            }).Start();
        }
        void InstallLightApp(Catalog.Light a)
        {
            if (busy) return; byte[] data = File.ReadAllBytes(a.Path);
            if (MessageBox.Show("Instalar \"" + a.Pt + "\" no relógio?\n\nO app roda como programa nativo dentro do relógio: instale só apps em que você confia. É preciso que o relógio tenha o firmware com \"Meus apps\".", "Instalar app", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            watch = NewWatch(); if (watch == null) return; Begin(); Status("Instalando " + a.Pt + "...", 0); Watch w = watch;
            new Thread(delegate()
            {
                try { w.InstallLight(a.Id, data, a.IndexLine(), delegate(long d, long t, long bps) { Status("Enviando " + a.Pt + ": " + d + " / " + t + " bytes", (int)(d * 1000 / Math.Max(1, t))); }); installed = w.ReadIndex(); installedKnown = true; Log("App instalado: " + a.Id); Status(a.Pt + " instalado. No relógio: Apps extras > Meus apps.", 1000); Post(delegate { MessageBox.Show(a.Pt + " instalado!\n\nNo relógio abra Configurações > Dicas e tutoriais (Apps extras) > Meus apps.", "Fit3 Manager"); }); }
                catch (Exception e) { Fail(e, false); }
                Post(delegate { FillLight(); }); End();
            }).Start();
        }
        void RemoveLightApp(Catalog.Light a)
        {
            if (busy) return; if (MessageBox.Show("Tirar \"" + a.Pt + "\" da lista do relógio?", "Remover app", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            watch = NewWatch(); if (watch == null) return; Begin(); Status("Removendo " + a.Pt + "...", -1); Watch w = watch;
            new Thread(delegate()
            {
                try { w.RemoveLight(a.Id); installed = w.ReadIndex(); installedKnown = true; Log("App removido da lista: " + a.Id); Status("Removido.", 0); } catch (Exception e) { Fail(e, false); }
                Post(delegate { FillLight(); }); End();
            }).Start();
        }
    }

    static class Program
    {
        [STAThread] static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); }
            catch (Exception e) { try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ToString()); } catch (Exception) { } MessageBox.Show("O programa encontrou um erro e vai fechar: " + e.Message, "Fit3 Manager"); }
        }
    }
}
