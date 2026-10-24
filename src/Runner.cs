using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DiagnosticoReparo
{
    // ---------------------------------------------------------------
    // Executa programas de linha de comando de forma oculta, capturando
    // a saida linha a linha (com o encoding certo para cada ferramenta)
    // e extraindo porcentagens de progresso quando a ferramenta as
    // imprime (DISM, SFC, chkdsk...).
    // ---------------------------------------------------------------
    public class RunResult
    {
        public int ExitCode = -1;
        public string Saida = "";
        public bool Timeout = false;
        public bool NaoEncontrado = false;   // executavel nao existe neste ambiente

        public bool Contem(params string[] trechos)
        {
            string baixa = (Saida ?? "").ToLowerInvariant();
            foreach (string t in trechos)
                if (baixa.Contains(t.ToLowerInvariant())) return true;
            return false;
        }
    }

    public static class Runner
    {
        // Encoding da saida:
        //   OEM     -> maioria dos utilitarios de console (chkdsk, netsh, dism...)
        //   Unicode -> sfc.exe (imprime UTF-16 quando redirecionado)
        public enum Enc { OEM, Unicode, UTF8 }

        public static RunResult Rodar(string exe, string args, int timeoutMs,
                                      Action<string> logLinha, Action<double> progresso,
                                      Enc enc = Enc.OEM, string stdin = null)
        {
            var res = new RunResult();
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                if (stdin != null) psi.RedirectStandardInput = true;
                try
                {
                    if (enc == Enc.Unicode) psi.StandardOutputEncoding = Encoding.Unicode;
                    else if (enc == Enc.UTF8) psi.StandardOutputEncoding = Encoding.UTF8;
                    else psi.StandardOutputEncoding = Encoding.GetEncoding(
                        System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    psi.StandardErrorEncoding = psi.StandardOutputEncoding;
                }
                catch { }

                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    DataReceivedEventHandler h = delegate(object snd, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        string s = LimparLinha(e.Data);
                        if (s.Length == 0) return;
                        lock (sb) sb.AppendLine(s);
                        double pct = ExtrairPct(s);
                        if (pct >= 0 && progresso != null) progresso(pct);
                        // Linhas que sao so progresso repetido nao poluem o log
                        if (pct >= 0 && s.Length <= 40) return;
                        if (logLinha != null) logLinha("    " + s);
                    };
                    p.OutputDataReceived += h;
                    p.ErrorDataReceived += h;
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (stdin != null)
                    {
                        try { p.StandardInput.Write(stdin); p.StandardInput.Close(); } catch { }
                    }
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        res.Timeout = true;
                        if (logLinha != null)
                            logLinha("    (tempo esgotado — o comando foi interrompido)");
                    }
                    else
                    {
                        p.WaitForExit(); // garante flush dos eventos de saida
                        res.ExitCode = p.ExitCode;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                res.NaoEncontrado = true;
                if (logLinha != null)
                    logLinha("    (programa \"" + exe + "\" não está disponível neste ambiente)");
            }
            catch (Exception ex)
            {
                if (logLinha != null) logLinha("    ERRO: " + ex.Message);
            }
            lock (sb) res.Saida = sb.ToString();
            return res;
        }

        // PowerShell embutido do Windows. Sem -ExecutionPolicy Bypass: politica
        // de execucao nao se aplica a -Command, e a string e das mais
        // sinalizadas por antivirus (licao aprendida no projeto Otimizador).
        public static RunResult PowerShell(string comando, int timeoutMs,
                                           Action<string> logLinha, Action<double> progresso)
        {
            return Rodar("powershell.exe",
                "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(comando)),
                timeoutMs, logLinha, progresso, Enc.OEM, null);
        }

        // sfc.exe e chkdsk sobrescrevem a linha de progresso com \r;
        // mantemos so o trecho final. sfc ainda intercala NULs quando
        // a saida vai para pipe — removidos aqui.
        static string LimparLinha(string s)
        {
            if (s.IndexOf('\0') >= 0) s = s.Replace("\0", "");
            int cr = s.LastIndexOf('\r');
            if (cr >= 0) s = s.Substring(cr + 1);
            return s.Trim();
        }

        // Extrai "NN%" ou "NN.N%" de uma linha (progresso do DISM/SFC/defrag).
        static double ExtrairPct(string s)
        {
            int idx = s.IndexOf('%');
            if (idx <= 0) return -1;
            int fim = idx;
            int ini = idx - 1;
            while (ini >= 0 && (char.IsDigit(s[ini]) || s[ini] == '.' || s[ini] == ','))
                ini--;
            ini++;
            if (ini >= fim) return -1;
            string num = s.Substring(ini, fim - ini).Replace(',', '.');
            double v;
            if (double.TryParse(num, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                if (v >= 0 && v <= 100) return v;
            }
            return -1;
        }
    }
}
