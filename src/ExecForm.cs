using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DiagnosticoReparo
{
    // Tela de status de um reparo: mostra a porcentagem, a explicação do que
    // está sendo feito no momento, o log ao vivo e, ao final, a explicação do
    // que aconteceu (resumo interpretado de cada etapa).
    public class ExecForm : Form
    {
        static readonly Color CorTopo = Color.FromArgb(23, 48, 77);
        static readonly Color CorAcento = Color.FromArgb(0, 102, 204);
        static readonly Color CorOk = Color.FromArgb(30, 140, 60);
        static readonly Color CorAviso = Color.FromArgb(180, 90, 0);
        static readonly Color CorErro = Color.FromArgb(190, 40, 40);

        readonly Fix _fix;
        readonly Contexto _ctx;
        Label lblTitulo, lblFazendo, lblPct;
        ProgressBar barra;
        TextBox txtLog;
        Panel painelResumo;
        Label lblResumoTit, lblResumo;
        Button btnFechar;
        bool _rodando = true;
        readonly StringBuilder _logCompleto = new StringBuilder();
        FixExecResultado _resultado;

        public FixExecResultado Resultado { get { return _resultado; } }

        public ExecForm(Fix fix, Contexto ctx)
        {
            _fix = fix; _ctx = ctx;

            Text = "Executando: " + fix.Nome;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ControlBox = false;
            ClientSize = new Size(720, 520);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var topo = new Panel();
            topo.Dock = DockStyle.Top; topo.Height = 60; topo.BackColor = CorTopo;
            lblTitulo = new Label();
            lblTitulo.Text = fix.Nome;
            lblTitulo.ForeColor = Color.White;
            lblTitulo.Font = new Font("Segoe UI Semibold", 13F);
            lblTitulo.Location = new Point(20, 14); lblTitulo.AutoSize = true;
            lblTitulo.MaximumSize = new Size(680, 40);
            topo.Controls.Add(lblTitulo);
            Controls.Add(topo);

            lblFazendo = new Label();
            lblFazendo.Location = new Point(20, 74);
            lblFazendo.Size = new Size(680, 24);
            lblFazendo.Font = new Font("Segoe UI Semibold", 10F);
            lblFazendo.ForeColor = CorAcento;
            lblFazendo.Text = "Preparando...";
            Controls.Add(lblFazendo);

            barra = new ProgressBar();
            barra.Location = new Point(20, 102);
            barra.Size = new Size(620, 24);
            barra.Maximum = 1000;
            Controls.Add(barra);

            lblPct = new Label();
            lblPct.Location = new Point(648, 102);
            lblPct.Size = new Size(56, 24);
            lblPct.Font = new Font("Segoe UI Semibold", 11F);
            lblPct.TextAlign = ContentAlignment.MiddleRight;
            lblPct.Text = "0%";
            Controls.Add(lblPct);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.BackColor = Color.FromArgb(30, 30, 30);
            txtLog.ForeColor = Color.FromArgb(220, 220, 220);
            txtLog.Location = new Point(20, 138);
            txtLog.Size = new Size(684, 220);
            Controls.Add(txtLog);

            painelResumo = new Panel();
            painelResumo.Location = new Point(20, 368);
            painelResumo.Size = new Size(684, 110);
            painelResumo.BorderStyle = BorderStyle.FixedSingle;
            painelResumo.BackColor = Color.FromArgb(248, 249, 250);
            lblResumoTit = new Label();
            lblResumoTit.Text = "O que aconteceu";
            lblResumoTit.Font = new Font("Segoe UI Semibold", 10F);
            lblResumoTit.Location = new Point(10, 8); lblResumoTit.AutoSize = true;
            painelResumo.Controls.Add(lblResumoTit);
            lblResumo = new Label();
            lblResumo.Location = new Point(10, 32);
            lblResumo.Size = new Size(664, 72);
            lblResumo.Text = "Aguardando o término...";
            painelResumo.Controls.Add(lblResumo);
            Controls.Add(painelResumo);

            btnFechar = new Button();
            btnFechar.Text = "Fechar";
            btnFechar.Size = new Size(130, 36);
            btnFechar.Location = new Point(574, 482);
            btnFechar.FlatStyle = FlatStyle.Flat; btnFechar.FlatAppearance.BorderSize = 0;
            btnFechar.BackColor = CorAcento; btnFechar.ForeColor = Color.White;
            btnFechar.Enabled = false;
            btnFechar.Click += delegate { Close(); };
            Controls.Add(btnFechar);

            Shown += delegate { Iniciar(); };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (_rodando) e.Cancel = true;
            };
        }

        void Log(string s)
        {
            lock (_logCompleto) _logCompleto.AppendLine(s);
            try { BeginInvoke((Action)delegate { txtLog.AppendText(s + "\r\n"); }); } catch { }
        }

        void Iniciar()
        {
            _resultado = new FixExecResultado();
            _resultado.Fix = _fix;

            int total = 0;
            foreach (var e in _fix.Etapas) total += Math.Max(1, e.Peso);
            if (total == 0) total = 1;

            _ctx.Log = Log;
            _ctx.Status = delegate(string s) { try { BeginInvoke((Action)delegate { lblFazendo.Text = s; }); } catch { } };

            var th = new Thread(delegate()
            {
                int acumulado = 0;
                Log("Início: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                Log("Alvo: " + (_ctx.Online ? "Windows em execução" : "instalação " + _ctx.WinDir + " (modo recuperação)"));
                Log("");

                foreach (var etapa in _fix.Etapas)
                {
                    int baseAcum = acumulado;
                    int peso = Math.Max(1, etapa.Peso);
                    string fazendo = etapa.Fazendo;
                    try { BeginInvoke((Action)delegate { lblFazendo.Text = fazendo; }); } catch { }
                    Log(">> " + fazendo);

                    _ctx.ProgressoEtapa = delegate(double p)
                    {
                        double frac = (baseAcum + peso * (p / 100.0)) / total;
                        AtualizarBarra(frac);
                    };
                    AtualizarBarra((double)baseAcum / total);

                    EtapaResultado er;
                    try { er = etapa.Executar(_ctx); }
                    catch (Exception ex) { er = EtapaResultado.Falha("Erro inesperado: " + ex.Message); }
                    if (er == null) er = EtapaResultado.Sucesso("Concluído.");

                    _resultado.Resultados.Add(er);
                    if (!er.Ok) _resultado.TudoOk = false;
                    if (er.Aviso) _resultado.AlgumAviso = true;

                    string marca = er.Ok ? (er.Aviso ? "[atenção]" : "[ok]") : "[falhou]";
                    Log("   " + marca + " " + er.Mensagem);
                    Log("");

                    acumulado += peso;
                    AtualizarBarra((double)acumulado / total);
                }

                _ctx.Parametros.Clear();
                SalvarLog();
                try { BeginInvoke((Action)delegate { Finalizar(); }); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        void AtualizarBarra(double frac)
        {
            if (frac < 0) frac = 0; if (frac > 1) frac = 1;
            int val = (int)(frac * 1000);
            int pctInt = (int)Math.Round(frac * 100);
            try
            {
                BeginInvoke((Action)delegate
                {
                    barra.Value = Math.Min(1000, val);
                    lblPct.Text = pctInt + "%";
                });
            }
            catch { }
        }

        void Finalizar()
        {
            _rodando = false;
            AtualizarBarra(1.0);
            lblFazendo.Text = "Concluído.";

            Color cor = _resultado.TudoOk ? (_resultado.AlgumAviso ? CorAviso : CorOk) : CorErro;
            lblResumoTit.ForeColor = cor;
            lblResumoTit.Text = _resultado.TudoOk
                ? (_resultado.AlgumAviso ? "Concluído — com observações" : "Concluído com sucesso")
                : "Concluído — com falhas";

            string resumo = _resultado.ResumoFinal();
            if (string.IsNullOrEmpty(resumo)) resumo = "Reparo executado.";
            lblResumo.Text = resumo;

            btnFechar.Enabled = true;
            btnFechar.Focus();
        }

        void SalvarLog()
        {
            try
            {
                string caminho = Path.Combine(Contexto.PastaDados,
                    "reparo_" + _fix.Id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("Reparo: " + _fix.Nome);
                sb.AppendLine("Data: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                sb.AppendLine();
                lock (_logCompleto) sb.Append(_logCompleto.ToString());
                sb.AppendLine("=== O QUE ACONTECEU ===");
                sb.AppendLine(_resultado.ResumoFinal());
                File.WriteAllText(caminho, sb.ToString(), Encoding.UTF8);
                Log("Log salvo em: " + caminho);
            }
            catch { }
        }
    }
}
