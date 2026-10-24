using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DiagnosticoReparo
{
    // Diálogo simples para coletar uma entrada do técnico antes de um reparo:
    // texto livre, senha (mascarada) ou escolha em lista. Roda na thread de UI.
    public static class InputDialog
    {
        static readonly Color CorTopo = Color.FromArgb(23, 48, 77);
        static readonly Color CorAcento = Color.FromArgb(0, 102, 204);

        // Retorna o texto digitado, ou null se o técnico cancelar.
        public static string Pedir(IWin32Window owner, string titulo, string rotulo, string padrao, bool senha)
        {
            using (var f = Base(titulo, rotulo))
            {
                var txt = new TextBox();
                txt.Location = new Point(20, 96);
                txt.Size = new Size(384, 26);
                txt.Font = new Font("Segoe UI", 11F);
                txt.Text = padrao ?? "";
                if (senha) txt.UseSystemPasswordChar = true;
                f.Controls.Add(txt);

                string resultado = null;
                MontarBotoes(f, delegate { resultado = txt.Text; });
                txt.Focus(); txt.SelectAll();
                f.AcceptButton = (Button)f.Tag;
                return f.ShowDialog(owner) == DialogResult.OK ? resultado : null;
            }
        }

        // Retorna o item escolhido, ou null se cancelar.
        public static string Escolher(IWin32Window owner, string titulo, string rotulo, List<string> opcoes)
        {
            using (var f = Base(titulo, rotulo))
            {
                var cb = new ComboBox();
                cb.Location = new Point(20, 96);
                cb.Size = new Size(384, 28);
                cb.Font = new Font("Segoe UI", 11F);
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (string o in opcoes) cb.Items.Add(o);
                if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                f.Controls.Add(cb);

                string resultado = null;
                MontarBotoes(f, delegate { resultado = cb.SelectedItem as string; });
                return f.ShowDialog(owner) == DialogResult.OK ? resultado : null;
            }
        }

        static Form Base(string titulo, string rotulo)
        {
            var f = new Form();
            f.Text = titulo;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.ClientSize = new Size(424, 190);
            f.BackColor = Color.White;
            f.Font = new Font("Segoe UI", 9F);
            try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var faixa = new Panel();
            faixa.Dock = DockStyle.Top; faixa.Height = 6; faixa.BackColor = CorTopo;
            f.Controls.Add(faixa);

            var lbl = new Label();
            lbl.Text = rotulo;
            lbl.Location = new Point(20, 32);
            lbl.Size = new Size(384, 56);
            lbl.Font = new Font("Segoe UI", 10F);
            f.Controls.Add(lbl);
            return f;
        }

        static void MontarBotoes(Form f, Action aoConfirmar)
        {
            var ok = new Button();
            ok.Text = "OK"; ok.Size = new Size(110, 34);
            ok.Location = new Point(294, 140);
            ok.FlatStyle = FlatStyle.Flat; ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = CorAcento; ok.ForeColor = Color.White;
            ok.Click += delegate { aoConfirmar(); f.DialogResult = DialogResult.OK; f.Close(); };
            f.Controls.Add(ok);
            f.Tag = ok;

            var cancel = new Button();
            cancel.Text = "Cancelar"; cancel.Size = new Size(110, 34);
            cancel.Location = new Point(174, 140);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };
            f.Controls.Add(cancel);
            f.CancelButton = cancel;
        }
    }
}
