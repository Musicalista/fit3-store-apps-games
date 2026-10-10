using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Fit3Manager
{
    /// <summary>Step-by-step installer: check the list, send the package (with progress), then start the installation. Nothing is sent before every box is ticked.</summary>
    public sealed class InstallWizard : Form
    {
        readonly Func<Watch> newWatch; readonly byte[] pkg; readonly Fwd.Info info; readonly string title; readonly Action<string> log;
        Panel p0, p1, p2, p3; Label lblStep, lblProg, lblErr, lblDone, lblDoneTitle; ProgressBar bar; CheckBox c1, c2, c3, c4, cRead; Button bSend, bCancelSend, bRetry, bStart, bClose;
        volatile bool busy, staged, started; Watch watch; SynchronizationContext ui;

        public InstallWizard(string title, byte[] pkg, Fwd.Info info, Func<Watch> newWatch, Action<string> log)
        {
            this.title = title; this.pkg = pkg; this.info = info; this.newWatch = newWatch; this.log = log; ui = SynchronizationContext.Current;
            Text = Lang.T("Instalar: ") + title; Width = 640; Height = 660; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = Ui.Bg; Font = Ui.F(10f, false);
            lblStep = Ui.Lbl("", 10f, true, Ui.Accent); lblStep.Left = 24; lblStep.Top = 14; Controls.Add(lblStep);
            p0 = Page(); p1 = Page(); p2 = Page(); p3 = Page(); Controls.AddRange(new Control[] { p0, p1, p2, p3 });
            BuildCheck(); BuildSend(); BuildReady(); BuildDone(); Show(0);
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; MessageBox.Show(Lang.T("Espere terminar ou use Cancelar."), "Fit3 Manager"); return; }
                if (staged && !started && MessageBox.Show(Lang.T("O arquivo já está no relógio, mas a instalação ainda não foi iniciada. Fechar mesmo assim?\n\n(Você pode abrir de novo e iniciar depois; o arquivo continua lá. Se desistir, mande outro pacote por cima ou deixe como está: nada é instalado sem o comando.)"), Lang.T("Fechar"), MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
            };
        }
        Panel Page() { Panel p = new Panel(); p.Left = 16; p.Top = 44; p.Width = 600; p.Height = 570; p.BackColor = Ui.Bg; p.Visible = false; return p; }
        void Show(int n)
        {
            p0.Visible = n == 0; p1.Visible = n == 1; p2.Visible = n == 2; p3.Visible = n == 3;
            lblStep.Text = n == 0 ? Lang.T("PASSO 1 DE 3  ·  CONFERIR") : n == 1 ? Lang.T("PASSO 2 DE 3  ·  ENVIAR AO RELÓGIO") : n == 2 ? Lang.T("PASSO 3 DE 3  ·  INSTALAR") : Lang.T("CONCLUÍDO");
        }
        void Post(Action a) { ui.Post(delegate { a(); }, null); }

        void BuildCheck()
        {
            Label t = Ui.Lbl(title, 16f, true, Ui.Text); t.Left = 8; t.Top = 4; p0.Controls.Add(t);
            Panel c = Ui.Card(576, 84); c.Left = 8; c.Top = 44;
            c.Controls.Add(Ui.Wrap(Lang.T("Pacote verificado por inteiro (estrutura, somas de verificação e componentes).\r\n") + info.Count + Lang.T(" componentes · ") + (info.Size / 1048576.0).ToString("0.0") + " MB · " + (info.Stock ? Lang.T("firmware ORIGINAL da Samsung") : Lang.T("firmware MODIFICADO")) + "\r\nSHA-256: " + info.Sha256.Substring(0, 32) + "…", 10f, Ui.Muted, 540));
            p0.Controls.Add(c);
            Panel wp = new Panel(); wp.Left = 8; wp.Top = 136; wp.Width = 576; wp.Height = 92; wp.BackColor = Color.FromArgb(253, 236, 234); Label wt = Ui.Wrap(Lang.T("ATENÇÃO: só o Galaxy Fit3 SM-R390 com o firmware R390XXU0AZA3 é suportado. Outros modelos, variantes regionais e outras versões NÃO são suportados e não foram testados: gravar este pacote neles pode causar um ciclo de reinicialização (o relógio reinicia sem parar) ou deixá-lo inutilizável. Confira em Configurações > Sobre > Informações do software."), 9.5f, Ui.Bad, 556); wt.Left = 10; wt.Top = 8; wp.Controls.Add(wt); p0.Controls.Add(wp);
            Label q = Ui.Lbl(Lang.T("Marque tudo para continuar:"), 11f, true, Ui.Text); q.Left = 8; q.Top = 238; p0.Controls.Add(q);
            c1 = Chk(Lang.T("O relógio está com 50% de bateria ou mais e perto deste PC."), 266); c2 = Chk(Lang.T("Fechei o Galaxy Wearable e outros programas que usam o Bluetooth do relógio."), 298);
            c3 = Chk(Lang.T("Entendo o risco: gravar firmware pode deixar o relógio inutilizável. Tenho o firmware original à mão."), 330); c3.Height = 44;
            c4 = Chk(Lang.T("Confirmo que meu relógio é um Galaxy Fit3 SM-R390 com o firmware R390XXU0AZA3 e entendo o risco de ciclo de reinicialização em outro modelo."), 378); c4.Height = 44;
            cRead = new CheckBox(); cRead.Text = Lang.T("Conferir byte a byte depois do envio (bem mais lento, mais seguro)"); cRead.Left = 8; cRead.Top = 428; cRead.Width = 570; cRead.Font = Ui.F(9.5f, false); cRead.ForeColor = Ui.Muted; p0.Controls.Add(cRead);
            Label w = Ui.Wrap(Lang.T("Se você já enviou outros pacotes de atualização e não sabe se ficaram no relógio, não continue. A verificação de integridade não garante que um firmware modificado funcione."), 9.5f, Ui.Warn, 570); w.Left = 8; w.Top = 458; p0.Controls.Add(w);
            bSend = Ui.Primary(Lang.T("Enviar ao relógio"), delegate { StartSend(); }); bSend.Left = 8; bSend.Top = 520; bSend.Enabled = false; p0.Controls.Add(bSend);
            EventHandler h = delegate { bSend.Enabled = c1.Checked && c2.Checked && c3.Checked && c4.Checked; }; c1.CheckedChanged += h; c2.CheckedChanged += h; c3.CheckedChanged += h; c4.CheckedChanged += h;
            Button cancel = Ui.Soft(Lang.T("Cancelar"), delegate { Close(); }); cancel.Left = 190; cancel.Top = 520; p0.Controls.Add(cancel);
        }
        CheckBox Chk(string text, int top) { CheckBox c = new CheckBox(); c.Text = text; c.Left = 8; c.Top = top; c.Width = 570; c.Height = 30; c.Font = Ui.F(10f, false); p0.Controls.Add(c); return c; }
        void BuildSend()
        {
            Label t = Ui.Lbl(Lang.T("Enviando o firmware…"), 16f, true, Ui.Text); t.Left = 8; t.Top = 4; p1.Controls.Add(t);
            Label l = Ui.Wrap(Lang.T("Isso leva alguns minutos. Mantenha o relógio perto e este programa aberto. Não desligue nada."), 10f, Ui.Muted, 570); l.Left = 8; l.Top = 44; p1.Controls.Add(l);
            bar = new ProgressBar(); bar.Left = 8; bar.Top = 96; bar.Width = 570; bar.Height = 22; bar.Maximum = 1000; p1.Controls.Add(bar);
            lblProg = Ui.Wrap(Lang.T("Preparando…"), 10f, Ui.Text, 570); lblProg.Left = 8; lblProg.Top = 126; p1.Controls.Add(lblProg);
            lblErr = Ui.Wrap("", 10f, Ui.Bad, 570); lblErr.Left = 8; lblErr.Top = 170; lblErr.Height = 200; p1.Controls.Add(lblErr);
            bCancelSend = Ui.Soft(Lang.T("Cancelar envio"), delegate { if (watch != null) watch.Abort(); }); bCancelSend.Left = 8; bCancelSend.Top = 420; p1.Controls.Add(bCancelSend);
            bRetry = Ui.Primary(Lang.T("Tentar de novo"), delegate { Show(0); }); bRetry.Left = 8; bRetry.Top = 420; bRetry.Visible = false; p1.Controls.Add(bRetry);
        }
        void BuildReady()
        {
            Label t = Ui.Lbl(Lang.T("Arquivo no relógio. Pronto para instalar!"), 16f, true, Ui.Ok); t.Left = 8; t.Top = 4; p2.Controls.Add(t);
            Label l = Ui.Wrap(Lang.T("O firmware já está dentro do relógio e foi conferido. Falta só mandar o comando para o relógio atualizar.\r\n\r\nDepois que você iniciar:\r\n•  não dá para cancelar;\r\n•  não desligue o relógio nem o repita o comando;\r\n•  o relógio vai mostrar o progresso e reiniciar sozinho (leva alguns minutos);\r\n•  confirme o resultado na tela do relógio."), 10.5f, Ui.Text, 570); l.Left = 8; l.Top = 50; p2.Controls.Add(l);
            bStart = Ui.Danger(Lang.T("Iniciar a instalação agora"), delegate { StartInstall(); }); bStart.Left = 8; bStart.Top = 300; p2.Controls.Add(bStart);
            Button later = Ui.Soft(Lang.T("Fechar sem instalar"), delegate { Close(); }); later.Left = 8; later.Top = 350; p2.Controls.Add(later);
        }
        void BuildDone()
        {
            lblDoneTitle = Ui.Lbl("", 16f, true, Ui.Text); lblDoneTitle.Left = 8; lblDoneTitle.Top = 4; p3.Controls.Add(lblDoneTitle);
            lblDone = Ui.Wrap("", 10.5f, Ui.Text, 570); lblDone.Left = 8; lblDone.Top = 50; lblDone.Height = 260; p3.Controls.Add(lblDone);
            bClose = Ui.Primary(Lang.T("Concluir"), delegate { Close(); }); bClose.Left = 8; bClose.Top = 420; p3.Controls.Add(bClose);
        }

        void StartSend()
        {
            watch = newWatch(); if (watch == null) return; Show(1); busy = true; lblErr.Text = ""; bRetry.Visible = false; bCancelSend.Visible = true; bar.Value = 0; lblProg.Text = Lang.T("Conectando ao relógio…"); Watch w = watch; bool full = cRead.Checked;
            w.Progress = delegate(string phase, long d, long t, long bps)
            {
                bool up = phase == "upload"; int pm = (int)(d * 1000 / Math.Max(1, t));
                Post(delegate { bar.Value = Math.Max(0, Math.Min(1000, pm)); lblProg.Text = (up ? Lang.T("Enviando: ") : Lang.T("Conferindo o arquivo no relógio: ")) + (d / 1024) + Lang.T(" de ") + (t / 1024) + " KB" + (up && bps > 0 ? "  ·  " + (bps / 1024) + " KB/s" : "") + "  (" + (pm / 10) + "%)"; });
            };
            new Thread(delegate()
            {
                int end = Environment.TickCount + 30000; Exception last = null; bool ok = false;
                while (!ok && unchecked(end - Environment.TickCount) > 0 && !w.IsAborted)
                {
                    try { w.Stage(pkg, full); ok = true; }
                    catch (Exception e) { last = e; log(Lang.T("Envio: ") + e.Message); if (!e.Message.Contains(Lang.T("serviço serial"))) break; Post(delegate { lblProg.Text = Lang.T("Tentando conectar de novo…"); }); Thread.Sleep(2500); }
                }
                busy = false;
                if (ok) { staged = true; log(full ? Lang.T("Envio: arquivo lido de volta, todos os bytes iguais.") : Lang.T("Envio: CRC/ACK dos blocos e tamanho conferidos.")); Post(delegate { Show(2); }); }
                else Post(delegate
                {
                    lblProg.Text = Lang.T("O envio não terminou."); lblErr.Text = (last == null ? Lang.T("Cancelado.") : last.Message) + Lang.T("\r\n\r\nO relógio não foi alterado: nada foi instalado.") + (last != null && last.Message.Contains(Lang.T("serviço serial")) ? Lang.T("\r\n\r\nDicas: desconecte o relógio do celular (Bluetooth do celular desligado), feche o Galaxy Wearable, reinicie o relógio e tente de novo.") : "");
                    bCancelSend.Visible = false; bRetry.Visible = true;
                });
            }).Start();
        }
        void StartInstall()
        {
            if (MessageBox.Show(Lang.T("Depois de iniciar não dá para cancelar. Não desligue o relógio e não repita o comando.\n\nIniciar a instalação agora?"), Lang.T("Última confirmação"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            watch = newWatch(); if (watch == null) return; busy = true; staged = false; bStart.Enabled = false; Watch w = watch; bool[] sending = { false };
            new Thread(delegate()
            {
                string msgTitle, msg; Color col;
                try { string reply = w.StartOta(sending); started = true; log("Comando OTA enviado; resposta=" + (reply == null ? "null" : "\"" + reply.Replace("\r", "\\r").Replace("\n", "\\n") + "\"")); msgTitle = Lang.T("Comando enviado"); col = Ui.Ok; msg = Lang.T("O relógio recebeu o comando de instalação.\r\n\r\nAgora é só esperar: ele atualiza e reinicia sozinho (alguns minutos). Não desligue o relógio. Confira na tela dele se deu certo.\r\n\r\nSe algo parecer errado, o firmware original (aba Loja > Original Samsung) devolve o relógio ao padrão."); }
                catch (Exception e)
                {
                    if (sending[0]) { started = true; msgTitle = Lang.T("Olhe a tela do relógio"); col = Ui.Warn; msg = Lang.T("A conexão caiu ao iniciar. A atualização pode ter começado.\r\n\r\nNão repita o comando e não desligue o relógio. Espere alguns minutos e olhe a tela dele."); log(Lang.T("Resultado do OTA desconhecido: ") + e.Message); }
                    else { staged = true; bStart.Enabled = true; msgTitle = Lang.T("Não foi possível iniciar"); col = Ui.Bad; msg = e.Message + Lang.T("\r\n\r\nNada foi iniciado. Você pode tentar de novo."); log(Lang.T("Erro ao iniciar: ") + e.Message); }
                }
                busy = false;
                Post(delegate { lblDoneTitle.Text = msgTitle; lblDoneTitle.ForeColor = col; lblDone.Text = msg; if (started) Show(3); else MessageBox.Show(msg, "Fit3 Manager"); });
            }).Start();
        }
    }
}
