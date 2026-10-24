using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiagnosticoReparo
{
    // ---------------------------------------------------------------
    // Modelo de um reparo ("prompt pré-programado"):
    //  - Fix: um botão do catálogo. Tem nome, explicação em linguagem
    //    clara do que faz, avisos e uma lista de Etapas.
    //  - Etapa: um passo com texto "o que está sendo feito", peso no
    //    progresso e a função que executa e devolve um resultado
    //    interpretado ("o que aconteceu").
    //  - Contexto: ambiente de execução (online ou WinPE + Windows
    //    alvo), log, status e helpers para rodar comandos.
    // ---------------------------------------------------------------

    public class EtapaResultado
    {
        public bool Ok = true;
        public bool Aviso = false;
        public string Mensagem = "";   // interpretacao humana do que aconteceu

        public static EtapaResultado Sucesso(string msg) { var r = new EtapaResultado(); r.Ok = true; r.Mensagem = msg; return r; }
        public static EtapaResultado Alerta(string msg) { var r = new EtapaResultado(); r.Ok = true; r.Aviso = true; r.Mensagem = msg; return r; }
        public static EtapaResultado Falha(string msg) { var r = new EtapaResultado(); r.Ok = false; r.Mensagem = msg; return r; }
    }

    public class Etapa
    {
        public string Fazendo;                          // "Parando o serviço de impressão..."
        public int Peso = 1;                            // participacao na porcentagem
        public Func<Contexto, EtapaResultado> Executar;

        public Etapa(string fazendo, int peso, Func<Contexto, EtapaResultado> exec)
        {
            Fazendo = fazendo; Peso = peso; Executar = exec;
        }
    }

    public class Fix
    {
        public string Id;
        public string Categoria;
        public string Nome;             // texto do botao
        public string Resumo;           // 1 linha (aparece na lista)
        public string Explicacao;       // texto completo "o que este reparo faz"
        public string Aviso;            // efeito colateral relevante (pede confirmacao)
        public bool RequerReinicio;
        public bool DemoradoAviso;      // mostra "pode levar varios minutos"
        public Func<Contexto, bool> Disponivel;  // null = sempre
        public List<Etapa> Etapas = new List<Etapa>();

        // Coleta entradas do tecnico ANTES de executar (roda na thread de UI).
        // Ex.: escolher impressora, digitar nome/senha de usuario.
        // Retornar false cancela a execucao.
        public Func<Contexto, System.Windows.Forms.IWin32Window, bool> Preparar;

        public bool DisponivelEm(Contexto ctx)
        {
            return Disponivel == null || Disponivel(ctx);
        }

        public string DescricaoCompleta()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Explicacao);
            sb.AppendLine();
            sb.AppendLine("O que será feito, passo a passo:");
            foreach (var e in Etapas) sb.AppendLine("  • " + e.Fazendo);
            if (RequerReinicio)
            {
                sb.AppendLine();
                sb.AppendLine("Ao final é necessário REINICIAR o computador para concluir.");
            }
            if (DemoradoAviso)
            {
                sb.AppendLine();
                sb.AppendLine("Este reparo pode levar VÁRIOS MINUTOS. Não desligue o computador.");
            }
            if (!string.IsNullOrEmpty(Aviso))
            {
                sb.AppendLine();
                sb.AppendLine("ATENÇÃO: " + Aviso);
            }
            return sb.ToString();
        }
    }

    // Ambiente em que os reparos rodam.
    public class Contexto
    {
        public SystemInfo Sys;
        public WindowsOffline Alvo;      // no modo PE: instalacao selecionada; null = online
        public Action<string> Log = delegate { };
        public Action<string> Status = delegate { };          // "o que esta sendo feito" na tela
        public Action<double> ProgressoEtapa = delegate { };  // 0..100 dentro da etapa atual

        // Entradas coletadas pelo Preparar do Fix (nome de usuario, senha,
        // impressora escolhida...), consumidas pelas etapas.
        public Dictionary<string, string> Parametros = new Dictionary<string, string>();

        public bool Online { get { return Sys == null || !Sys.ModoPE; } }

        // Letra/pasta do Windows em reparo: online = C:\Windows do proprio
        // sistema; PE = a instalacao selecionada pelo tecnico.
        public string WinDrive
        {
            get
            {
                if (!Online && Alvo != null) return Alvo.Drive;
                string sd = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return string.IsNullOrEmpty(sd) ? "C:" : sd.Substring(0, 2);
            }
        }
        public string WinDir
        {
            get
            {
                if (!Online && Alvo != null) return Alvo.WinDir;
                return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            }
        }

        public string UltimaSaida = "";

        public RunResult Rodar(string exe, string args, int timeoutMs,
                               Runner.Enc enc = Runner.Enc.OEM, string stdin = null)
        {
            Log("  > " + exe + " " + args);
            var r = Runner.Rodar(exe, args, timeoutMs, Log, ProgressoEtapa, enc, stdin);
            UltimaSaida = r.Saida;
            return r;
        }

        public RunResult PowerShell(string comando, int timeoutMs)
        {
            Log("  > powershell: " + (comando.Length > 120 ? comando.Substring(0, 120) + "..." : comando));
            var r = Runner.PowerShell(comando, timeoutMs, Log, ProgressoEtapa);
            UltimaSaida = r.Saida;
            return r;
        }

        // Pasta de dados/logs do aplicativo (no PE cai no ramdisk, o que e ok)
        public static string PastaDados
        {
            get
            {
                string p;
                try
                {
                    p = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "DiagnosticoReparo");
                }
                catch { p = @"X:\DiagnosticoReparo"; }
                try { Directory.CreateDirectory(p); } catch { }
                return p;
            }
        }
    }

    // Resultado consolidado da execucao de um Fix (para a tela final).
    public class FixExecResultado
    {
        public Fix Fix;
        public List<EtapaResultado> Resultados = new List<EtapaResultado>();
        public bool TudoOk = true;
        public bool AlgumAviso = false;

        public string ResumoFinal()
        {
            var sb = new StringBuilder();
            foreach (var r in Resultados)
            {
                string marca = r.Ok ? (r.Aviso ? "[atenção] " : "[ok] ") : "[falhou] ";
                if (!string.IsNullOrEmpty(r.Mensagem))
                    sb.AppendLine(marca + r.Mensagem);
            }
            if (Fix != null && Fix.RequerReinicio)
                sb.AppendLine("→ REINICIE o computador para concluir este reparo.");
            return sb.ToString().TrimEnd();
        }
    }
}
