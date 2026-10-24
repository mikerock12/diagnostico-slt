using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DiagnosticoReparo
{
    public class MainForm : Form
    {
        static readonly Color CorFundo = Color.White;
        static readonly Color CorTopo = Color.FromArgb(23, 48, 77);
        static readonly Color CorNav = Color.FromArgb(238, 241, 245);
        static readonly Color CorNavSel = Color.FromArgb(0, 102, 204);
        static readonly Color CorAcento = Color.FromArgb(0, 102, 204);
        static readonly Color CorOk = Color.FromArgb(30, 140, 60);
        static readonly Color CorInfo = Color.FromArgb(0, 102, 204);
        static readonly Color CorAviso = Color.FromArgb(180, 90, 0);
        static readonly Color CorErro = Color.FromArgb(190, 40, 40);

        SystemInfo sys;
        List<Fix> catalogo;
        WindowsOffline alvo;             // instalação alvo no modo PE
        bool preDiagFeito = false;

        Panel painelTopo, painelConteudo, painelRodape, painelNav;
        Label lblTitulo, lblSub;
        Button navSelecionado;
        ComboBox cbInstalacao;

        public MainForm()
        {
            Text = "Diagnóstico e Reparo — Windows 10/11";
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(880, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CorFundo;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ---------------- Topo ----------------
            painelTopo = new Panel();
            painelTopo.Dock = DockStyle.Top; painelTopo.Height = 64; painelTopo.BackColor = CorTopo;
            lblTitulo = new Label();
            lblTitulo.Text = "Diagnóstico e Reparo";
            lblTitulo.ForeColor = Color.White;
            lblTitulo.Font = new Font("Segoe UI Semibold", 15F);
            lblTitulo.Location = new Point(18, 8); lblTitulo.AutoSize = true;
            lblSub = new Label();
            lblSub.ForeColor = Color.FromArgb(170, 200, 230);
            lblSub.Font = new Font("Segoe UI", 9F);
            lblSub.Location = new Point(20, 38); lblSub.AutoSize = true;
            lblSub.Text = "Detectando ambiente...";
            painelTopo.Controls.Add(lblTitulo);
            painelTopo.Controls.Add(lblSub);

            // ---------------- Rodapé ----------------
            painelRodape = new Panel();
            painelRodape.Dock = DockStyle.Bottom; painelRodape.Height = 54;
            painelRodape.BackColor = Color.FromArgb(243, 243, 243);
            var logoPeq = SplashForm.CarregarLogo();
            if (logoPeq != null)
            {
                var picLogo = new PictureBox();
                picLogo.Image = logoPeq; picLogo.SizeMode = PictureBoxSizeMode.Zoom;
                picLogo.Size = new Size(38, 38); picLogo.Location = new Point(12, 8);
                picLogo.Cursor = Cursors.Hand;
                picLogo.Click += delegate { SplashForm.AbrirSite(); };
                painelRodape.Controls.Add(picLogo);
            }
            var lblCredito = new Label();
            lblCredito.Text = "Criado por Maicon Nunes — Smells Like Tech Informática";
            lblCredito.Font = new Font("Segoe UI", 8.25F);
            lblCredito.ForeColor = Color.FromArgb(90, 90, 90);
            lblCredito.Location = new Point(58, 9); lblCredito.AutoSize = true;
            painelRodape.Controls.Add(lblCredito);
            var linkSite = new LinkLabel();
            linkSite.Text = SplashForm.SITE_TEXTO;
            linkSite.Font = new Font("Segoe UI", 8.25F);
            linkSite.Location = new Point(58, 27); linkSite.AutoSize = true;
            linkSite.LinkClicked += delegate { SplashForm.AbrirSite(); };
            painelRodape.Controls.Add(linkSite);

            // ---------------- Nav ----------------
            painelNav = new Panel();
            painelNav.Dock = DockStyle.Left; painelNav.Width = 224; painelNav.BackColor = CorNav;
            painelNav.AutoScroll = true;

            // ---------------- Conteúdo ----------------
            painelConteudo = new Panel();
            painelConteudo.Dock = DockStyle.Fill; painelConteudo.BackColor = CorFundo;
            painelConteudo.Padding = new Padding(20, 16, 20, 16);
            painelConteudo.AutoScroll = true;

            Controls.Add(painelConteudo);
            Controls.Add(painelNav);
            Controls.Add(painelTopo);
            Controls.Add(painelRodape);

            Load += delegate { IniciarDeteccao(); };
        }

        // ============================================================
        // Detecção inicial
        // ============================================================
        void IniciarDeteccao()
        {
            MostrarMensagemCentral("Detectando o ambiente e o hardware...");
            var t = new Thread(delegate()
            {
                var s = SystemInfo.Detectar();
                var cat = FixCatalog.Construir();
                BeginInvoke((Action)delegate
                {
                    sys = s; catalogo = cat;
                    if (s.Instalacoes.Count > 0) alvo = s.Instalacoes[0];
                    AtualizarSubtitulo();
                    MontarNav();
                    MostrarInicio();
                });
            });
            t.IsBackground = true; t.Start();
        }

        void AtualizarSubtitulo()
        {
            if (sys.ModoPE)
                lblSub.Text = "Ambiente de recuperação (WinPE) · " + (sys.FirmwareUEFI ? "UEFI" : "BIOS") +
                    " · " + sys.Instalacoes.Count + " Windows encontrado(s)";
            else
                lblSub.Text = sys.WindowsNome + " " + sys.WindowsVersao + " · build " + sys.WindowsBuild +
                    " · " + (sys.FirmwareUEFI ? "UEFI" : "BIOS");
        }

        Contexto NovoContexto()
        {
            var ctx = new Contexto();
            ctx.Sys = sys;
            ctx.Alvo = sys != null && sys.ModoPE ? alvo : null;
            return ctx;
        }

        // ============================================================
        // Barra de navegação
        // ============================================================
        void MontarNav()
        {
            painelNav.Controls.Clear();
            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(0, 8, 0, 8);

            flow.Controls.Add(NavBotao("  ●  Diagnóstico", "inicio"));
            flow.Controls.Add(NavSeparador("REPAROS"));

            var ctx = NovoContexto();
            string[] cats =
            {
                FixCatalog.CAT_SIS, FixCatalog.CAT_BOOT, FixCatalog.CAT_NET, FixCatalog.CAT_IMP,
                FixCatalog.CAT_USR, FixCatalog.CAT_WU, FixCatalog.CAT_APP, FixCatalog.CAT_LIMP
            };
            foreach (string c in cats)
            {
                int n = catalogo.Count(f => f.Categoria == c && f.DisponivelEm(ctx));
                if (n == 0) continue;
                flow.Controls.Add(NavBotao("  " + c, "cat:" + c));
            }

            flow.Controls.Add(NavSeparador(""));
            flow.Controls.Add(NavBotao("  Todos os comandos", "todos"));
            flow.Controls.Add(NavBotao("  Teste final e nota", "teste"));
            painelNav.Controls.Add(flow);
        }

        Button NavBotao(string texto, string tag)
        {
            var b = new Button();
            b.Text = texto;
            b.Tag = tag;
            b.Width = 206;
            b.Height = 38;
            b.Margin = new Padding(6, 2, 6, 2);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.BackColor = CorNav;
            b.ForeColor = Color.FromArgb(40, 40, 40);
            b.Font = new Font("Segoe UI", 9.75F);
            b.Cursor = Cursors.Hand;
            b.Click += delegate { Navegar(b); };
            return b;
        }

        Label NavSeparador(string texto)
        {
            var l = new Label();
            l.Text = texto;
            l.Width = 206; l.Height = texto.Length > 0 ? 26 : 12;
            l.Margin = new Padding(12, texto.Length > 0 ? 8 : 2, 6, 2);
            l.ForeColor = Color.FromArgb(120, 130, 145);
            l.Font = new Font("Segoe UI Semibold", 8F);
            l.TextAlign = ContentAlignment.BottomLeft;
            return l;
        }

        void Navegar(Button b)
        {
            if (navSelecionado != null) { navSelecionado.BackColor = CorNav; navSelecionado.ForeColor = Color.FromArgb(40, 40, 40); }
            navSelecionado = b;
            b.BackColor = CorNavSel; b.ForeColor = Color.White;

            string tag = (string)b.Tag;
            if (tag == "inicio") MostrarInicio();
            else if (tag == "todos") MostrarReparos("Todos os comandos disponíveis", catalogo);
            else if (tag == "teste") MostrarTeste();
            else if (tag.StartsWith("cat:"))
            {
                string cat = tag.Substring(4);
                MostrarReparos(cat, catalogo.Where(f => f.Categoria == cat).ToList());
            }
        }

        void SelecionarNav(string tag)
        {
            foreach (Control c in painelNav.Controls)
                if (c is FlowLayoutPanel)
                    foreach (Control x in c.Controls)
                    {
                        var b = x as Button;
                        if (b != null && (string)b.Tag == tag) { Navegar(b); return; }
                    }
        }

        // ============================================================
        // Utilidades de layout
        // ============================================================
        void MostrarMensagemCentral(string msg)
        {
            painelConteudo.Controls.Clear();
            var l = new Label();
            l.Text = msg;
            l.Font = new Font("Segoe UI", 12F);
            l.ForeColor = Color.FromArgb(90, 90, 90);
            l.AutoSize = true;
            l.Location = new Point(40, 40);
            painelConteudo.Controls.Add(l);
        }

        // Seletor da instalação alvo (só no modo PE). Devolve a altura ocupada.
        int MontarSeletorInstalacao(int y)
        {
            if (sys == null || !sys.ModoPE || sys.Instalacoes.Count == 0) return 0;
            var painel = new Panel();
            painel.Location = new Point(0, y);
            painel.Size = new Size(painelConteudo.ClientSize.Width - 44, 56);
            painel.BackColor = Color.FromArgb(255, 248, 225);
            painel.BorderStyle = BorderStyle.FixedSingle;

            var lbl = new Label();
            lbl.Text = "Windows a reparar:";
            lbl.Font = new Font("Segoe UI Semibold", 9.5F);
            lbl.Location = new Point(12, 17); lbl.AutoSize = true;
            painel.Controls.Add(lbl);

            cbInstalacao = new ComboBox();
            cbInstalacao.DropDownStyle = ComboBoxStyle.DropDownList;
            cbInstalacao.Location = new Point(160, 14);
            cbInstalacao.Size = new Size(440, 28);
            cbInstalacao.Font = new Font("Segoe UI", 9.5F);
            foreach (var w in sys.Instalacoes) cbInstalacao.Items.Add(w.Rotulo());
            cbInstalacao.SelectedIndex = Math.Max(0, sys.Instalacoes.IndexOf(alvo));
            cbInstalacao.SelectedIndexChanged += delegate
            {
                int i = cbInstalacao.SelectedIndex;
                if (i >= 0 && i < sys.Instalacoes.Count) alvo = sys.Instalacoes[i];
            };
            painel.Controls.Add(cbInstalacao);
            painelConteudo.Controls.Add(painel);
            return 66;
        }

        // ============================================================
        // Tela: Diagnóstico (início)
        // ============================================================
        void MostrarInicio()
        {
            painelConteudo.Controls.Clear();
            int y = 0;
            y += MontarSeletorInstalacao(y);

            var lblResumo = new Label();
            lblResumo.Text = sys.Resumo();
            lblResumo.Font = new Font("Consolas", 9.5F);
            lblResumo.Location = new Point(2, y);
            lblResumo.Size = new Size(painelConteudo.ClientSize.Width - 44, 96);
            lblResumo.ForeColor = Color.FromArgb(50, 50, 50);
            painelConteudo.Controls.Add(lblResumo);
            y += 104;

            var titulo = new Label();
            titulo.Text = "Pré-diagnóstico rápido";
            titulo.Font = new Font("Segoe UI Semibold", 13F);
            titulo.Location = new Point(0, y); titulo.AutoSize = true;
            painelConteudo.Controls.Add(titulo);

            var btnRodar = new Button();
            btnRodar.Text = preDiagFeito ? "Repetir pré-diagnóstico" : "Executar pré-diagnóstico";
            btnRodar.Size = new Size(220, 34);
            btnRodar.Location = new Point(painelConteudo.ClientSize.Width - 264, y - 2);
            btnRodar.FlatStyle = FlatStyle.Flat; btnRodar.FlatAppearance.BorderSize = 0;
            btnRodar.BackColor = CorAcento; btnRodar.ForeColor = Color.White;
            btnRodar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            painelConteudo.Controls.Add(btnRodar);
            y += 40;

            var lblStatus = new Label();
            lblStatus.Location = new Point(0, y);
            lblStatus.Size = new Size(painelConteudo.ClientSize.Width - 44, 22);
            lblStatus.ForeColor = CorAcento;
            lblStatus.Font = new Font("Segoe UI", 9.5F);
            painelConteudo.Controls.Add(lblStatus);

            var barra = new ProgressBar();
            barra.Location = new Point(0, y + 24);
            barra.Size = new Size(painelConteudo.ClientSize.Width - 44, 16);
            barra.Maximum = 100;
            barra.Visible = false;
            painelConteudo.Controls.Add(barra);

            var painelAchados = new FlowLayoutPanel();
            painelAchados.Location = new Point(0, y + 48);
            painelAchados.Size = new Size(painelConteudo.ClientSize.Width - 44, painelConteudo.ClientSize.Height - (y + 56));
            painelAchados.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            painelAchados.FlowDirection = FlowDirection.TopDown;
            painelAchados.WrapContents = false;
            painelAchados.AutoScroll = true;
            painelConteudo.Controls.Add(painelAchados);

            btnRodar.Click += delegate { RodarPreDiag(btnRodar, lblStatus, barra, painelAchados); };

            if (!preDiagFeito)
                RodarPreDiag(btnRodar, lblStatus, barra, painelAchados);
            else
            {
                var dica = new Label();
                dica.Text = "Clique acima para rodar o pré-diagnóstico novamente.";
                dica.ForeColor = Color.FromArgb(120, 120, 120); dica.AutoSize = true;
                painelAchados.Controls.Add(dica);
            }
        }

        void RodarPreDiag(Button btn, Label status, ProgressBar barra, FlowLayoutPanel painel)
        {
            btn.Enabled = false;
            painel.Controls.Clear();
            barra.Visible = true; barra.Value = 0;
            status.Text = "Iniciando...";

            var ctx = NovoContexto();
            var t = new Thread(delegate()
            {
                Action<string> passo = delegate(string s) { try { BeginInvoke((Action)delegate { status.Text = s; }); } catch { } };
                Action<int> pct = delegate(int p) { try { BeginInvoke((Action)delegate { barra.Value = Math.Min(100, p); }); } catch { } };
                List<Achado> achados;
                try { achados = PreDiag.Executar(sys, passo, pct); }
                catch (Exception ex)
                {
                    achados = new List<Achado>();
                    achados.Add(new Achado(Gravidade.Atencao, "O pré-diagnóstico encontrou um erro", ex.Message));
                }
                BeginInvoke((Action)delegate
                {
                    preDiagFeito = true;
                    btn.Enabled = true; btn.Text = "Repetir pré-diagnóstico";
                    barra.Visible = false;
                    int criticos = achados.Count(a => a.Nivel == Gravidade.Critico);
                    int atencao = achados.Count(a => a.Nivel == Gravidade.Atencao);
                    status.Text = string.Format("Concluído — {0} ponto(s) de atenção, {1} crítico(s).", atencao, criticos);
                    foreach (var a in achados) painel.Controls.Add(CartaoAchado(a));
                });
            });
            t.IsBackground = true; t.Start();
        }

        Control CartaoAchado(Achado a)
        {
            Color cor;
            string icone;
            switch (a.Nivel)
            {
                case Gravidade.Critico: cor = CorErro; icone = "✕"; break;
                case Gravidade.Atencao: cor = CorAviso; icone = "!"; break;
                case Gravidade.Ok: cor = CorOk; icone = "✓"; break;
                default: cor = CorInfo; icone = "i"; break;
            }

            int largura = painelConteudo.ClientSize.Width - 68;
            var card = new Panel();
            card.Width = largura;
            card.BackColor = Color.FromArgb(250, 250, 250);
            card.BorderStyle = BorderStyle.FixedSingle;
            card.Margin = new Padding(0, 0, 0, 10);

            var faixa = new Panel();
            faixa.BackColor = cor; faixa.Width = 6; faixa.Dock = DockStyle.Left;
            card.Controls.Add(faixa);

            var lblIco = new Label();
            lblIco.Text = icone; lblIco.ForeColor = cor;
            lblIco.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblIco.Location = new Point(16, 10); lblIco.Size = new Size(28, 28);
            lblIco.TextAlign = ContentAlignment.MiddleCenter;
            card.Controls.Add(lblIco);

            var lblTit = new Label();
            lblTit.Text = a.Titulo;
            lblTit.Font = new Font("Segoe UI Semibold", 10.5F);
            lblTit.ForeColor = cor;
            lblTit.Location = new Point(50, 10);
            lblTit.Size = new Size(largura - 66, 22);
            card.Controls.Add(lblTit);

            var lblDet = new Label();
            lblDet.Text = a.Detalhe;
            lblDet.Location = new Point(50, 34);
            lblDet.Size = new Size(largura - 66, 0);
            lblDet.AutoSize = false;
            lblDet.MaximumSize = new Size(largura - 66, 0);
            lblDet.AutoSize = true;
            card.Controls.Add(lblDet);

            int y = 34 + Math.Max(20, lblDet.PreferredHeight) + 8;

            // Botões de reparo recomendado
            var ctx = NovoContexto();
            int bx = 50;
            var botoes = new List<Button>();
            foreach (string fid in a.FixIds)
            {
                var f = FixCatalog.PorId(catalogo, fid);
                if (f == null || !f.DisponivelEm(ctx)) continue;
                var bfix = new Button();
                bfix.Text = "Corrigir: " + f.Nome;
                bfix.AutoSize = true;
                bfix.MaximumSize = new Size(largura - 70, 0);
                bfix.Padding = new Padding(8, 4, 8, 4);
                bfix.FlatStyle = FlatStyle.Flat;
                bfix.FlatAppearance.BorderColor = cor;
                bfix.ForeColor = cor;
                bfix.BackColor = Color.White;
                bfix.Cursor = Cursors.Hand;
                Fix capturado = f;
                bfix.Click += delegate { ExecutarFix(capturado); };
                botoes.Add(bfix);
            }
            foreach (var bfix in botoes)
            {
                bfix.Location = new Point(bx, y);
                card.Controls.Add(bfix);
                bx += bfix.Width + 8;
                if (bx > largura - 120) { bx = 50; y += bfix.Height + 6; }
            }
            if (botoes.Count > 0) y += 34;

            card.Height = y + 4;
            return card;
        }

        // ============================================================
        // Tela: Reparos (lista de comandos)
        // ============================================================
        ListBox lstFixes;
        Label lblDescTit;
        TextBox txtDesc;
        Button btnExecutar;
        List<Fix> fixesExibidos;

        void MostrarReparos(string titulo, List<Fix> fixes)
        {
            painelConteudo.Controls.Clear();
            int topo = 0;
            topo += MontarSeletorInstalacao(topo);

            var ctx = NovoContexto();
            fixesExibidos = fixes.Where(f => f.DisponivelEm(ctx)).ToList();

            var lblTit = new Label();
            lblTit.Text = titulo;
            lblTit.Font = new Font("Segoe UI Semibold", 13F);
            lblTit.Location = new Point(0, topo); lblTit.AutoSize = true;
            painelConteudo.Controls.Add(lblTit);

            var lblAjudaLista = new Label();
            lblAjudaLista.Text = "Clique em um comando para ver exatamente o que ele faz. Depois use o botão azul embaixo para executá-lo.";
            lblAjudaLista.Location = new Point(0, topo + 28);
            lblAjudaLista.Size = new Size(painelConteudo.ClientSize.Width - 44, 20);
            lblAjudaLista.ForeColor = Color.FromArgb(110, 110, 110);
            painelConteudo.Controls.Add(lblAjudaLista);

            int yLista = topo + 54;
            int alturaLista = painelConteudo.ClientSize.Height - yLista - 64;
            if (alturaLista < 180) alturaLista = 180;
            int larguraLista = 360;

            lstFixes = new ListBox();
            lstFixes.Location = new Point(0, yLista);
            lstFixes.Size = new Size(larguraLista, alturaLista);
            lstFixes.Font = new Font("Segoe UI", 10F);
            lstFixes.IntegralHeight = false;
            lstFixes.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            foreach (var f in fixesExibidos) lstFixes.Items.Add(f.Nome);
            lstFixes.SelectedIndexChanged += delegate { AtualizarDescricao(); };
            painelConteudo.Controls.Add(lstFixes);

            lblDescTit = new Label();
            lblDescTit.Location = new Point(larguraLista + 16, yLista);
            lblDescTit.Size = new Size(painelConteudo.ClientSize.Width - larguraLista - 60, 24);
            lblDescTit.Font = new Font("Segoe UI Semibold", 11F);
            lblDescTit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblDescTit.Text = "Selecione um comando";
            painelConteudo.Controls.Add(lblDescTit);

            txtDesc = new TextBox();
            txtDesc.Location = new Point(larguraLista + 16, yLista + 28);
            txtDesc.Size = new Size(painelConteudo.ClientSize.Width - larguraLista - 60, alturaLista - 28);
            txtDesc.Multiline = true; txtDesc.ReadOnly = true;
            txtDesc.ScrollBars = ScrollBars.Vertical;
            txtDesc.BackColor = Color.FromArgb(248, 249, 250);
            txtDesc.BorderStyle = BorderStyle.FixedSingle;
            txtDesc.Font = new Font("Segoe UI", 9.75F);
            txtDesc.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtDesc.Text = "Clique num comando à esquerda para ver a explicação completa.";
            painelConteudo.Controls.Add(txtDesc);

            // O botão "vira" o comando selecionado (muda o texto para a opção clicada)
            btnExecutar = new Button();
            btnExecutar.Size = new Size(painelConteudo.ClientSize.Width - 44, 44);
            btnExecutar.Location = new Point(0, yLista + alturaLista + 10);
            btnExecutar.FlatStyle = FlatStyle.Flat; btnExecutar.FlatAppearance.BorderSize = 0;
            btnExecutar.BackColor = Color.FromArgb(200, 200, 200);
            btnExecutar.ForeColor = Color.White;
            btnExecutar.Font = new Font("Segoe UI Semibold", 11F);
            btnExecutar.Text = "Selecione um comando acima";
            btnExecutar.Enabled = false;
            btnExecutar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnExecutar.Click += delegate
            {
                int i = lstFixes.SelectedIndex;
                if (i >= 0 && i < fixesExibidos.Count) ExecutarFix(fixesExibidos[i]);
            };
            painelConteudo.Controls.Add(btnExecutar);

            if (fixesExibidos.Count > 0) lstFixes.SelectedIndex = 0;
        }

        void AtualizarDescricao()
        {
            int i = lstFixes.SelectedIndex;
            if (i < 0 || i >= fixesExibidos.Count) return;
            var f = fixesExibidos[i];
            lblDescTit.Text = f.Nome;
            txtDesc.Text = f.DescricaoCompleta();
            btnExecutar.Text = "Executar:  " + f.Nome;
            btnExecutar.Enabled = true;
            btnExecutar.BackColor = CorAcento;
        }

        // ============================================================
        // Execução de um reparo
        // ============================================================
        void ExecutarFix(Fix f)
        {
            var ctx = NovoContexto();

            // Coleta de entradas (usuário, senha, impressora...)
            if (f.Preparar != null)
            {
                bool ok;
                try { ok = f.Preparar(ctx, this); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Erro ao preparar o reparo: " + ex.Message, "Diagnóstico e Reparo",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (!ok) return;
            }

            // Confirmação quando há aviso de efeito colateral
            if (!string.IsNullOrEmpty(f.Aviso))
            {
                var r = MessageBox.Show(this,
                    f.Nome + "\r\n\r\nATENÇÃO: " + f.Aviso + "\r\n\r\nDeseja continuar?",
                    "Confirmar reparo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            using (var ef = new ExecForm(f, ctx))
            {
                ef.ShowDialog(this);
                var res = ef.Resultado;
                if (res != null && f.RequerReinicio && res.TudoOk)
                    OferecerReinicio(f.Nome);
            }
        }

        void OferecerReinicio(string nomeFix)
        {
            if (sys != null && sys.ModoPE) return; // no PE o técnico reinicia pelo menu do PE
            var r = MessageBox.Show(this,
                "O reparo \"" + nomeFix + "\" precisa de reinício para concluir.\r\n\r\nReiniciar o computador agora?",
                "Reiniciar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start("shutdown.exe", "/r /t 8 /c \"Reiniciando para concluir o reparo\"");
                    Application.Exit();
                }
                catch { }
            }
        }

        // ============================================================
        // Tela: Teste final e nota
        // ============================================================
        TextBox txtBench;
        ProgressBar barraBench;
        Button btnBench, btnReiniciar;
        Label lblNota, lblVeredito;
        bool benchRodando = false;

        void MostrarTeste()
        {
            painelConteudo.Controls.Clear();

            var lblTit = new Label();
            lblTit.Text = "Teste final e nota da máquina";
            lblTit.Font = new Font("Segoe UI Semibold", 13F);
            lblTit.Location = new Point(0, 0); lblTit.AutoSize = true;
            painelConteudo.Controls.Add(lblTit);

            var lbl = new Label();
            lbl.Text = "Teste rápido de memória RAM, armazenamento (disco) e processador, com uma nota de 1 a 10. " +
                       "Faça isto ao final do atendimento. Leva cerca de 1 minuto; evite usar o computador durante o teste.";
            lbl.Location = new Point(0, 30);
            lbl.Size = new Size(painelConteudo.ClientSize.Width - 44, 40);
            painelConteudo.Controls.Add(lbl);

            btnBench = new Button();
            btnBench.Text = "Iniciar teste";
            btnBench.Size = new Size(180, 38);
            btnBench.Location = new Point(0, 76);
            btnBench.FlatStyle = FlatStyle.Flat; btnBench.FlatAppearance.BorderSize = 0;
            btnBench.BackColor = CorAcento; btnBench.ForeColor = Color.White;
            btnBench.Font = new Font("Segoe UI Semibold", 10.5F);
            btnBench.Click += delegate { RodarBench(); };
            painelConteudo.Controls.Add(btnBench);

            barraBench = new ProgressBar();
            barraBench.Location = new Point(196, 82);
            barraBench.Size = new Size(painelConteudo.ClientSize.Width - 240, 26);
            barraBench.Maximum = 100;
            barraBench.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            painelConteudo.Controls.Add(barraBench);

            int yb = 126;
            int altura = painelConteudo.ClientSize.Height - yb - 10;
            if (altura < 240) altura = 240;

            txtBench = new TextBox();
            txtBench.Multiline = true; txtBench.ReadOnly = true;
            txtBench.ScrollBars = ScrollBars.Vertical;
            txtBench.Font = new Font("Consolas", 9F);
            txtBench.BackColor = Color.FromArgb(30, 30, 30);
            txtBench.ForeColor = Color.FromArgb(220, 220, 220);
            txtBench.Location = new Point(0, yb);
            txtBench.Size = new Size(painelConteudo.ClientSize.Width - 340, altura);
            txtBench.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            painelConteudo.Controls.Add(txtBench);

            var painelNota = new Panel();
            painelNota.Location = new Point(painelConteudo.ClientSize.Width - 328, yb);
            painelNota.Size = new Size(284, altura);
            painelNota.BorderStyle = BorderStyle.FixedSingle;
            painelNota.BackColor = Color.FromArgb(248, 249, 250);
            painelNota.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;

            var lblNotaTit = new Label();
            lblNotaTit.Text = "NOTA DA MÁQUINA";
            lblNotaTit.Font = new Font("Segoe UI Semibold", 10F);
            lblNotaTit.TextAlign = ContentAlignment.MiddleCenter;
            lblNotaTit.Size = new Size(280, 28); lblNotaTit.Location = new Point(0, 14);
            painelNota.Controls.Add(lblNotaTit);

            lblNota = new Label();
            lblNota.Text = "–";
            lblNota.Font = new Font("Segoe UI", 46F, FontStyle.Bold);
            lblNota.TextAlign = ContentAlignment.MiddleCenter;
            lblNota.Size = new Size(280, 92); lblNota.Location = new Point(0, 44);
            lblNota.ForeColor = Color.FromArgb(120, 120, 120);
            painelNota.Controls.Add(lblNota);

            lblVeredito = new Label();
            lblVeredito.Text = "Clique em \"Iniciar teste\".";
            lblVeredito.TextAlign = ContentAlignment.TopCenter;
            lblVeredito.Location = new Point(10, 142);
            lblVeredito.Size = new Size(264, altura - 200);
            painelNota.Controls.Add(lblVeredito);

            btnReiniciar = new Button();
            btnReiniciar.Text = "Reiniciar agora";
            btnReiniciar.Size = new Size(160, 34);
            btnReiniciar.Location = new Point(62, altura - 48);
            btnReiniciar.FlatStyle = FlatStyle.Flat;
            btnReiniciar.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            btnReiniciar.Anchor = AnchorStyles.Bottom;
            btnReiniciar.Visible = false;
            btnReiniciar.Click += delegate
            {
                try { System.Diagnostics.Process.Start("shutdown.exe", "/r /t 10 /c \"Reiniciando\""); Application.Exit(); }
                catch { }
            };
            painelNota.Controls.Add(btnReiniciar);

            painelConteudo.Controls.Add(painelNota);
        }

        void RodarBench()
        {
            if (benchRodando) return;
            benchRodando = true;
            btnBench.Enabled = false;
            txtBench.Clear();
            barraBench.Value = 0;

            var t = new Thread(delegate()
            {
                Action<string> log = delegate(string s) { try { BeginInvoke((Action)delegate { txtBench.AppendText(s + "\r\n"); }); } catch { } };
                Action<int> pct = delegate(int p) { try { BeginInvoke((Action)delegate { barraBench.Value = Math.Min(100, p); }); } catch { } };
                BenchResult res = null;
                try { res = Bench.Executar(sys, log, pct); }
                catch (Exception ex) { log("ERRO no teste: " + ex.Message); }
                BeginInvoke((Action)delegate { BenchConcluido(res); });
            });
            t.IsBackground = true; t.Start();
        }

        void BenchConcluido(BenchResult res)
        {
            benchRodando = false;
            btnBench.Enabled = true;
            btnBench.Text = "Repetir teste";
            if (res == null) return;

            txtBench.AppendText("\r\n" + Bench.Relatorio(res));
            lblNota.Text = res.NotaFinal.ToString("0.0");
            if (res.NotaFinal < 3 || res.RamErros > 0) lblNota.ForeColor = CorErro;
            else if (res.NotaFinal < 5) lblNota.ForeColor = CorAviso;
            else if (res.NotaFinal < 7) lblNota.ForeColor = Color.FromArgb(180, 140, 0);
            else lblNota.ForeColor = CorOk;

            lblVeredito.Text = string.Format("CPU: {0:0.0}   RAM: {1:0.0}   Disco: {2:0.0}\r\n\r\n{3}",
                res.NotaCpu, res.NotaRam, res.NotaDisco, res.Veredito);

            if (sys != null && !sys.ModoPE) btnReiniciar.Visible = true;
        }
    }
}
