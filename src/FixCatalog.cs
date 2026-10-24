using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace DiagnosticoReparo
{
    // ---------------------------------------------------------------
    // Catálogo de reparos — parte 1: infraestrutura + categorias
    // "Sistema", "Inicialização (Boot)" e "Rede e Internet".
    // Parte 2 (FixCatalog2.cs): Impressoras, Conta de usuário,
    // Windows Update, Aplicativos/Interface e Limpeza.
    // ---------------------------------------------------------------
    public static partial class FixCatalog
    {
        public const string CAT_SIS = "Sistema e arquivos do Windows";
        public const string CAT_BOOT = "Inicialização (Boot)";
        public const string CAT_NET = "Rede e Internet";
        public const string CAT_IMP = "Impressoras (HP, Epson e outras)";
        public const string CAT_USR = "Conta de usuário";
        public const string CAT_WU = "Windows Update";
        public const string CAT_APP = "Aplicativos e interface";
        public const string CAT_LIMP = "Limpeza e desempenho";

        static readonly Func<Contexto, bool> SoOnline = delegate(Contexto c) { return c.Online; };
        static readonly Func<Contexto, bool> SoPE = delegate(Contexto c) { return !c.Online; };

        public static List<Fix> Construir()
        {
            var lista = new List<Fix>();
            Sistema(lista);
            Boot(lista);
            Rede(lista);
            Impressoras(lista);
            Usuario(lista);
            WindowsUpdate(lista);
            AppsInterface(lista);
            Limpeza(lista);
            return lista;
        }

        public static Fix PorId(List<Fix> lista, string id)
        {
            return lista.FirstOrDefault(f => f.Id == id);
        }

        // ============================================================
        // Helpers de construção
        // ============================================================
        static Fix F(string id, string cat, string nome, string resumo, string explicacao)
        {
            var f = new Fix();
            f.Id = id; f.Categoria = cat; f.Nome = nome; f.Resumo = resumo; f.Explicacao = explicacao;
            return f;
        }

        // Etapa que roda um comando e interpreta o resultado.
        static Etapa ECmd(string fazendo, int peso, string exe, string args, int timeoutMs,
                          Func<RunResult, Contexto, EtapaResultado> interpretar = null,
                          Runner.Enc enc = Runner.Enc.OEM, string stdin = null)
        {
            return new Etapa(fazendo, peso, delegate(Contexto ctx)
            {
                var r = ctx.Rodar(exe, args, timeoutMs, enc, stdin);
                if (interpretar != null) return interpretar(r, ctx);
                if (r.NaoEncontrado) return EtapaResultado.Alerta("\"" + exe + "\" não existe neste ambiente — etapa ignorada.");
                if (r.Timeout) return EtapaResultado.Falha("O comando demorou demais e foi interrompido.");
                if (r.ExitCode == 0) return EtapaResultado.Sucesso(fazendo.TrimEnd('.') + " — concluído.");
                return EtapaResultado.Alerta(fazendo.TrimEnd('.') + " — código de saída " + r.ExitCode + " (veja o log).");
            });
        }

        // ---------------- serviços (API nativa, sem sc.exe) ----------
        public static bool ServicoExiste(string nome)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + nome))
                    return k != null;
            }
            catch { return false; }
        }

        public static void ServicoTipoInicio(string nome, int start) // 2=auto 3=manual 4=desativado
        {
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + nome, true))
            {
                if (k != null) k.SetValue("Start", start, RegistryValueKind.DWord);
            }
        }

        public static bool PararServico(string nome, int segundos, Action<string> log)
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController(nome))
                {
                    if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped) return true;
                    if (!sc.CanStop) return false;
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(segundos));
                    return true;
                }
            }
            catch (Exception ex) { if (log != null) log("    (serviço " + nome + ": " + ex.Message + ")"); return false; }
        }

        public static bool IniciarServico(string nome, int segundos, Action<string> log)
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController(nome))
                {
                    if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) return true;
                    sc.Start();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,
                        TimeSpan.FromSeconds(segundos));
                    return true;
                }
            }
            catch (Exception ex) { if (log != null) log("    (serviço " + nome + ": " + ex.Message + ")"); return false; }
        }

        public static bool ReiniciarServico(string nome, Action<string> log)
        {
            PararServico(nome, 20, log);
            return IniciarServico(nome, 20, log);
        }

        // ============================================================
        // Interpretadores inteligentes de saída
        // ============================================================
        static EtapaResultado InterpretarSfc(RunResult r, Contexto ctx)
        {
            if (r.NaoEncontrado) return EtapaResultado.Falha("sfc.exe não está disponível neste ambiente.");
            if (r.Contem("não encontrou nenhuma violação", "did not find any integrity violations"))
                return EtapaResultado.Sucesso("O SFC verificou todos os arquivos protegidos do Windows e NÃO encontrou nenhum corrompido. Os arquivos do sistema estão íntegros.");
            if (r.Contem("reparou com êxito", "reparados com êxito", "successfully repaired"))
                return EtapaResultado.Sucesso("O SFC encontrou arquivos do sistema corrompidos e REPAROU todos com sucesso. Detalhes em CBS.log.");
            if (r.Contem("não conseguiu corrigir", "não foi capaz de corrigir", "unable to fix"))
                return EtapaResultado.Alerta("O SFC encontrou arquivos corrompidos mas NÃO conseguiu reparar todos. Rode o reparo \"Reparar a imagem do Windows (DISM)\" e depois repita este.");
            if (r.Contem("não pôde executar a operação", "could not perform the requested operation"))
                return EtapaResultado.Falha("O SFC não conseguiu executar. Tente novamente após reiniciar; se persistir, rode este reparo a partir do modo de recuperação (WinPE).");
            if (r.Contem("proteção de recursos do windows encontrou", "another servicing or repair operation", "reparação já está em execução", "pendente de reparo"))
                return EtapaResultado.Alerta("Há outra operação de manutenção em andamento ou pendente. Reinicie o computador e rode novamente.");
            if (r.Timeout) return EtapaResultado.Falha("O SFC demorou demais e foi interrompido.");
            if (r.ExitCode == 0) return EtapaResultado.Sucesso("SFC concluído (veja o log acima para o resultado).");
            return EtapaResultado.Alerta("SFC terminou com resultado não reconhecido — confira o log acima.");
        }

        static EtapaResultado InterpretarDismRestore(RunResult r, Contexto ctx)
        {
            if (r.NaoEncontrado) return EtapaResultado.Falha("dism.exe não está disponível neste ambiente.");
            if (r.Contem("0x800f081f", "source files could not be found", "arquivos de origem não puderam ser encontrados"))
                return EtapaResultado.Alerta("O DISM precisa de uma FONTE de arquivos: conecte o computador à internet e rode de novo, ou use um ISO/pendrive do Windows da mesma versão como origem.");
            if (r.Contem("concluída com êxito", "operação de restauração", "completed successfully", "êxito"))
                return EtapaResultado.Sucesso("A imagem do Windows foi verificada e reparada com sucesso pelo DISM.");
            if (r.Contem("0x800f0906", "0x800f0907"))
                return EtapaResultado.Alerta("O DISM não conseguiu baixar os arquivos de reparo (sem internet ou Windows Update com problema). Repare a rede/Update primeiro.");
            if (r.Timeout) return EtapaResultado.Falha("O DISM demorou demais e foi interrompido.");
            if (r.ExitCode == 0) return EtapaResultado.Sucesso("DISM concluído.");
            return EtapaResultado.Alerta("DISM terminou com código " + r.ExitCode + " — confira o log acima.");
        }

        static EtapaResultado InterpretarChkdsk(RunResult r, Contexto ctx)
        {
            if (r.NaoEncontrado) return EtapaResultado.Falha("chkdsk.exe não está disponível neste ambiente.");
            bool problemas = r.Contem("encontrou problemas", "found problems", "corrigiu erros", "fixed errors", "made corrections");
            bool semProblemas = r.Contem("não encontrou problemas", "found no problems", "verificou o sistema de arquivos e não encontrou");
            long kbRuins = ExtrairKbDefeituosos(r.Saida);
            if (kbRuins > 0)
                return EtapaResultado.Alerta(string.Format(
                    "O disco tem {0} KB em SETORES DEFEITUOSOS (dano físico). O sistema de arquivos foi verificado, mas recomende BACKUP e troca do disco (de preferência por SSD).", kbRuins));
            if (problemas && !semProblemas)
                return EtapaResultado.Sucesso("O chkdsk encontrou e CORRIGIU erros no sistema de arquivos.");
            if (semProblemas)
                return EtapaResultado.Sucesso("O chkdsk não encontrou problemas no sistema de arquivos.");
            if (r.Timeout) return EtapaResultado.Falha("O chkdsk demorou demais e foi interrompido — rode manualmente depois.");
            return EtapaResultado.Sucesso("Verificação de disco concluída (detalhes no log acima).");
        }

        static long ExtrairKbDefeituosos(string saida)
        {
            foreach (string l in (saida ?? "").Split('\n'))
            {
                string baixa = l.ToLowerInvariant();
                if (!(baixa.Contains("defeituoso") || baixa.Contains("bad sector"))) continue;
                string digitos = "";
                foreach (char c in l)
                {
                    if (char.IsDigit(c)) digitos += c;
                    else if (digitos.Length > 0 && (c == '.' || c == ',')) continue;
                    else if (digitos.Length > 0) break;
                }
                long v;
                if (long.TryParse(digitos, out v)) return v;
            }
            return 0;
        }

        // ============================================================
        // CATEGORIA: Sistema e arquivos do Windows
        // ============================================================
        static void Sistema(List<Fix> lista)
        {
            // ---------- Reparo completo (o "botão mágico") ----------
            var completo = F("sys-reparo-completo", CAT_SIS,
                "Reparo completo dos arquivos do sistema (DISM + SFC)",
                "A sequência recomendada pela Microsoft: repara a imagem do Windows e depois os arquivos do sistema.",
                "Este é o reparo mais completo para Windows lento, travando, com erros estranhos, atualizações falhando ou programas que não abrem.\r\n\r\n" +
                "Primeiro o DISM repara a \"imagem\" do Windows (o depósito de onde o sistema tira cópias boas dos arquivos); depois o SFC compara cada arquivo do sistema com essa imagem e substitui os corrompidos. É a ordem correta: se a imagem estiver danificada, o SFC sozinho não resolve.");
            completo.DemoradoAviso = true;
            completo.Etapas.Add(new Etapa("Reparando a imagem do Windows (DISM RestoreHealth) — pode demorar...", 60,
                delegate(Contexto ctx)
                {
                    string args = ctx.Online
                        ? "/Online /Cleanup-Image /RestoreHealth"
                        : "/Image:" + ctx.WinDrive + "\\ /Cleanup-Image /RestoreHealth";
                    var r = ctx.Rodar("dism.exe", args, 90 * 60 * 1000);
                    return InterpretarDismRestore(r, ctx);
                }));
            completo.Etapas.Add(new Etapa("Verificando e reparando arquivos do sistema (SFC /scannow) — pode demorar...", 40,
                delegate(Contexto ctx)
                {
                    string args = ctx.Online
                        ? "/scannow"
                        : "/scannow /offbootdir=" + ctx.WinDrive + "\\ /offwindir=" + ctx.WinDir;
                    var r = ctx.Rodar("sfc.exe", args, 90 * 60 * 1000, Runner.Enc.Unicode);
                    return InterpretarSfc(r, ctx);
                }));
            lista.Add(completo);

            // ---------- SFC sozinho ----------
            var sfc = F("sys-sfc", CAT_SIS,
                "Verificar e reparar arquivos do sistema (SFC)",
                "Compara cada arquivo do Windows com a cópia original e substitui os corrompidos.",
                "O SFC (Verificador de Arquivos do Sistema) examina todos os arquivos protegidos do Windows e substitui automaticamente os que estiverem corrompidos ou faltando, usando as cópias guardadas na própria máquina.\r\n\r\n" +
                "Use quando: o Windows apresenta erros aleatórios, telas azuis, ferramentas do sistema não abrem, ícones/janelas com defeito.");
            sfc.DemoradoAviso = true;
            sfc.Etapas.Add(new Etapa("Executando SFC /scannow (5 a 30 minutos)...", 100,
                delegate(Contexto ctx)
                {
                    string args = ctx.Online
                        ? "/scannow"
                        : "/scannow /offbootdir=" + ctx.WinDrive + "\\ /offwindir=" + ctx.WinDir;
                    var r = ctx.Rodar("sfc.exe", args, 90 * 60 * 1000, Runner.Enc.Unicode);
                    return InterpretarSfc(r, ctx);
                }));
            lista.Add(sfc);

            // ---------- DISM verificação ----------
            var dchk = F("sys-dism-check", CAT_SIS,
                "Verificar a saúde da imagem do Windows (DISM)",
                "Diagnóstico: verifica se o depósito de arquivos do Windows está danificado (sem alterar nada).",
                "Roda o DISM em modo de verificação (CheckHealth + ScanHealth). Ele NÃO altera nada — apenas informa se a imagem do Windows está íntegra, danificada ou danificada e reparável.\r\n\r\nSe apontar dano, rode em seguida \"Reparar a imagem do Windows (DISM)\" ou o \"Reparo completo\".");
            dchk.Disponivel = SoOnline;
            dchk.Etapas.Add(ECmd("Verificação rápida de marcações de dano (CheckHealth)...", 20,
                "dism.exe", "/Online /Cleanup-Image /CheckHealth", 5 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.Contem("no component store corruption", "nenhum dano no repositório", "não detectou dano"))
                        return EtapaResultado.Sucesso("Nenhum dano marcado na imagem do Windows.");
                    if (r.Contem("repairable", "reparável"))
                        return EtapaResultado.Alerta("A imagem está marcada como danificada, porém REPARÁVEL.");
                    return EtapaResultado.Sucesso("CheckHealth concluído.");
                }));
            dchk.Etapas.Add(ECmd("Verificação completa da imagem (ScanHealth) — alguns minutos...", 80,
                "dism.exe", "/Online /Cleanup-Image /ScanHealth", 40 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.Contem("no component store corruption", "nenhum dano no repositório", "não detectou dano"))
                        return EtapaResultado.Sucesso("A imagem do Windows está ÍNTEGRA — nenhum dano encontrado.");
                    if (r.Contem("repairable", "reparável"))
                        return EtapaResultado.Alerta("A imagem do Windows está DANIFICADA (reparável). Rode agora o \"Reparo completo dos arquivos do sistema\".");
                    return EtapaResultado.Sucesso("ScanHealth concluído — veja o log.");
                }));
            lista.Add(dchk);

            // ---------- DISM RestoreHealth ----------
            var drest = F("sys-dism-restore", CAT_SIS,
                "Reparar a imagem do Windows (DISM RestoreHealth)",
                "Repara o depósito de arquivos que o Windows usa como fonte para se consertar.",
                "O DISM RestoreHealth baixa/repõe os arquivos danificados do depósito de componentes do Windows (WinSxS). É o conserto da \"fonte\": depois dele, o SFC consegue reparar o resto do sistema.\r\n\r\n" +
                "Com internet, ele baixa arquivos limpos do Windows Update automaticamente.");
            drest.DemoradoAviso = true;
            drest.Etapas.Add(new Etapa("Reparando a imagem do Windows (10 a 40 minutos)...", 100,
                delegate(Contexto ctx)
                {
                    string args = ctx.Online
                        ? "/Online /Cleanup-Image /RestoreHealth"
                        : "/Image:" + ctx.WinDrive + "\\ /Cleanup-Image /RestoreHealth";
                    var r = ctx.Rodar("dism.exe", args, 90 * 60 * 1000);
                    return InterpretarDismRestore(r, ctx);
                }));
            lista.Add(drest);

            // ---------- chkdsk agendado (online) ----------
            var chkAg = F("sys-chkdsk-agendar", CAT_SIS,
                "Verificar e corrigir erros do disco na próxima inicialização",
                "Agenda uma verificação completa do disco C: com correção automática de erros.",
                "O disco em uso não pode ser corrigido com o Windows aberto. Este reparo marca o volume C: para verificação: no próximo reinício, o Windows roda o chkdsk automaticamente ANTES de carregar o sistema, corrigindo erros do sistema de arquivos.\r\n\r\n" +
                "Use quando: travamentos, arquivos que somem/corrompem, lentidão extrema em HDD, desligamentos incorretos recentes.");
            chkAg.Disponivel = SoOnline;
            chkAg.RequerReinicio = true;
            chkAg.Etapas.Add(ECmd("Marcando o volume C: para verificação no próximo reinício...", 100,
                "fsutil.exe", "dirty set C:", 30000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.ExitCode == 0)
                        return EtapaResultado.Sucesso("Volume C: marcado. Na PRÓXIMA REINICIALIZAÇÃO o Windows fará a verificação completa do disco (tela azul de \"Verificando disco\" — é normal e pode demorar). Não desligue durante o processo.");
                    return EtapaResultado.Falha("Não foi possível marcar o volume (código " + r.ExitCode + ").");
                }));
            lista.Add(chkAg);

            // ---------- chkdsk direto (PE / disco fora de uso) ----------
            var chkFix = F("sys-chkdsk-corrigir", CAT_SIS,
                "Corrigir erros do disco agora (chkdsk /f)",
                "Verificação e correção imediata — possível porque o Windows do cliente não está em uso.",
                "Roda o chkdsk com correção (/f) diretamente no disco do Windows do cliente. Como estamos no ambiente de recuperação, o disco não está em uso e pode ser corrigido na hora.\r\n\r\n" +
                "É o primeiro passo recomendado quando o Windows não inicia: erro de disco é causa frequente de falha de boot.");
            chkFix.Disponivel = SoPE;
            chkFix.DemoradoAviso = true;
            chkFix.Etapas.Add(new Etapa("Verificando e corrigindo o disco (pode demorar bastante em HDD)...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.Rodar("chkdsk.exe", ctx.WinDrive + " /f", 120 * 60 * 1000);
                    return InterpretarChkdsk(r, ctx);
                }));
            lista.Add(chkFix);

            // ---------- desfazer atualizações pendentes (PE) ----------
            var revert = F("sys-revert-pending", CAT_SIS,
                "Desfazer atualização travada (RevertPendingActions)",
                "Para Windows preso em \"Não desligue o computador\" ou em loop após atualização.",
                "Quando uma atualização do Windows falha no meio, o sistema pode ficar preso reiniciando em loop ou parado em \"Estamos preparando o Windows\". Este reparo desfaz as ações pendentes da atualização, devolvendo o sistema ao estado anterior para que volte a iniciar.\r\n\r\n" +
                "Só funciona a partir do ambiente de recuperação (o Windows do cliente precisa estar desligado).");
            revert.Disponivel = SoPE;
            revert.Etapas.Add(new Etapa("Desfazendo ações pendentes de atualização...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.Rodar("dism.exe",
                        "/Image:" + ctx.WinDrive + "\\ /Cleanup-Image /RevertPendingActions", 40 * 60 * 1000);
                    if (r.Contem("êxito", "successfully", "concluída"))
                        return EtapaResultado.Sucesso("Ações pendentes desfeitas. Reinicie e deixe o Windows tentar iniciar normalmente.");
                    if (r.Contem("no pending actions", "não há ações pendentes", "0x800f082f"))
                        return EtapaResultado.Alerta("Não havia atualizações pendentes neste Windows — o problema de inicialização deve ter outra causa (tente o Reparo de inicialização e o chkdsk).");
                    return EtapaResultado.Alerta("Comando concluído com código " + r.ExitCode + " — veja o log.");
                }));
            lista.Add(revert);

            // ---------- ponto de restauração ----------
            var ponto = F("sys-ponto", CAT_SIS,
                "Criar ponto de restauração do sistema",
                "Garantia antes de mudanças maiores: permite voltar atrás pelo Windows.",
                "Cria um ponto de restauração chamado \"Diagnóstico e Reparo\". Se qualquer alteração futura causar problema, dá para voltar o sistema a este momento (Restauração do Sistema).\r\n\r\nRecomendado antes de reparos pesados de Update e de conta de usuário.");
            ponto.Disponivel = SoOnline;
            ponto.Etapas.Add(new Etapa("Criando ponto de restauração (1 a 2 minutos)...", 100,
                delegate(Contexto ctx)
                {
                    bool ok = CriarPontoRestauracao();
                    if (ok) return EtapaResultado.Sucesso("Ponto de restauração criado com sucesso.");
                    return EtapaResultado.Alerta("Não foi possível criar o ponto (a Restauração do Sistema pode estar desativada nesta máquina). Nada foi alterado.");
                }));
            lista.Add(ponto);

            // ---------- abrir restauração do sistema ----------
            var rstr = F("sys-restaurar", CAT_SIS,
                "Abrir a Restauração do Sistema",
                "Voltar o Windows para um ponto anterior (desfaz programas/drivers recentes, mantém arquivos).",
                "Abre a ferramenta oficial de Restauração do Sistema para escolher um ponto anterior e voltar o Windows àquele estado. Documentos e fotos NÃO são apagados; programas e drivers instalados depois do ponto são removidos.\r\n\r\n" +
                "Use quando o problema começou \"depois de instalar/atualizar algo\".");
            rstr.Etapas.Add(new Etapa("Abrindo a Restauração do Sistema...", 100,
                delegate(Contexto ctx)
                {
                    try
                    {
                        if (ctx.Online) System.Diagnostics.Process.Start("rstrui.exe");
                        else System.Diagnostics.Process.Start("rstrui.exe", "/offline:" + ctx.WinDir);
                        return EtapaResultado.Sucesso("Ferramenta aberta — siga o assistente na janela que apareceu e escolha um ponto anterior ao problema.");
                    }
                    catch (Exception ex)
                    {
                        return EtapaResultado.Falha("Não foi possível abrir a Restauração do Sistema: " + ex.Message);
                    }
                }));
            lista.Add(rstr);

            // ---------- teste de memória ----------
            var mem = F("sys-memdiag", CAT_SIS,
                "Agendar teste de memória RAM do Windows",
                "Teste profundo de RAM no próximo reinício (para travamentos e telas azuis aleatórias).",
                "Abre o Diagnóstico de Memória do Windows, que testa a RAM a fundo fora do sistema (no próximo reinício). Telas azuis variadas, travamentos aleatórios e corrupção de arquivos frequentemente são memória defeituosa.\r\n\r\n" +
                "O teste rápido de RAM deste programa (tela \"Teste final\") pega erros grosseiros; o do Windows é mais completo.");
            mem.Disponivel = SoOnline;
            mem.Etapas.Add(new Etapa("Abrindo o Diagnóstico de Memória do Windows...", 100,
                delegate(Contexto ctx)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("MdSched.exe");
                        return EtapaResultado.Sucesso("Na janela que abriu, escolha \"Reiniciar agora e verificar\". O resultado aparece após o reinício (e no Visualizador de Eventos).");
                    }
                    catch (Exception ex) { return EtapaResultado.Falha("Não foi possível abrir: " + ex.Message); }
                }));
            lista.Add(mem);
        }

        // Ponto de restauração pela API WMI (sem powershell oculto) —
        // mesma técnica validada no projeto Otimizador.
        public static bool CriarPontoRestauracao()
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                {
                    k.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }
                var scope = new ManagementScope(@"\\.\root\default");
                scope.Connect();
                using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                {
                    try
                    {
                        var pEnable = cls.GetMethodParameters("Enable");
                        pEnable["Drive"] = @"C:\";
                        pEnable["WaitTillEnabled"] = true;
                        cls.InvokeMethod("Enable", pEnable, null);
                    }
                    catch { }
                    var pCreate = cls.GetMethodParameters("CreateRestorePoint");
                    pCreate["Description"] = "Diagnóstico e Reparo";
                    pCreate["RestorePointType"] = 12;
                    pCreate["EventType"] = 100;
                    var saida = cls.InvokeMethod("CreateRestorePoint", pCreate, null);
                    return Convert.ToUInt32(saida["ReturnValue"]) == 0;
                }
            }
            catch { return false; }
        }

        // ============================================================
        // CATEGORIA: Inicialização (Boot)
        // ============================================================
        static void Boot(List<Fix> lista)
        {
            // ---------- reparo automático completo ----------
            var auto = F("boot-reparo-auto", CAT_BOOT,
                "Reparo automático de inicialização (sequência completa)",
                "Para \"o Windows não inicia\": corrige disco, setor de boot e recria os arquivos de inicialização.",
                "Sequência completa para computador que não entra no Windows (tela preta, \"Operating system not found\", \"BOOTMGR is missing\", erro 0xc000000e/0xc0000225 etc.):\r\n\r\n" +
                "1) Corrige erros do disco (causa nº 1 de falha de boot);\r\n" +
                "2) Em máquinas BIOS/Legado, regrava o setor de inicialização;\r\n" +
                "3) Recria os arquivos de boot e a lista BCD apontando para o Windows do cliente (funciona em UEFI e BIOS).\r\n\r\n" +
                "Roda a partir do pendrive de recuperação (Sergei Strelec).");
            auto.Disponivel = SoPE;
            auto.DemoradoAviso = true;
            auto.Etapas.Add(new Etapa("1/3 — Verificando e corrigindo o disco do Windows (chkdsk /f)...", 50,
                delegate(Contexto ctx)
                {
                    var r = ctx.Rodar("chkdsk.exe", ctx.WinDrive + " /f", 120 * 60 * 1000);
                    return InterpretarChkdsk(r, ctx);
                }));
            auto.Etapas.Add(new Etapa("2/3 — Regravando setor de inicialização (apenas BIOS/Legado)...", 20,
                delegate(Contexto ctx)
                {
                    if (ctx.Sys != null && ctx.Sys.FirmwareUEFI)
                        return EtapaResultado.Sucesso("Máquina UEFI — esta etapa não se aplica (pulada).");
                    var r = ctx.Rodar("bootsect.exe", "/nt60 ALL /mbr", 5 * 60 * 1000);
                    if (r.NaoEncontrado)
                    {
                        var r2 = ctx.Rodar("bootrec.exe", "/fixmbr", 5 * 60 * 1000);
                        if (!r2.NaoEncontrado && r2.ExitCode == 0)
                            return EtapaResultado.Sucesso("MBR regravado com bootrec /fixmbr.");
                        return EtapaResultado.Alerta("bootsect/bootrec não disponíveis neste PE — etapa pulada (o bcdboot da próxima etapa costuma bastar).");
                    }
                    if (r.ExitCode == 0) return EtapaResultado.Sucesso("Setor de inicialização regravado em todos os volumes (código de boot atual do Windows).");
                    return EtapaResultado.Alerta("bootsect retornou código " + r.ExitCode + " — veja o log.");
                }));
            auto.Etapas.Add(new Etapa("3/3 — Recriando arquivos de boot e lista BCD (bcdboot)...", 30, EtapaBcdboot));
            lista.Add(auto);

            // ---------- bcdboot ----------
            var bcd = F("boot-bcdboot", CAT_BOOT,
                "Recriar arquivos de boot e BCD (bcdboot)",
                "Recria do zero o menu de inicialização apontando para o Windows do cliente (UEFI e BIOS).",
                "O bcdboot copia os arquivos de inicialização do próprio Windows do cliente para a partição de sistema e recria a lista BCD. Resolve \"BCD ausente/corrompido\", erro 0xc000000f/0xc0000225 e menu de boot vazio.\r\n\r\n" +
                "É o método moderno e mais confiável — funciona tanto em UEFI quanto em BIOS/Legado.");
            bcd.Etapas.Add(new Etapa("Recriando arquivos de boot (bcdboot)...", 100, EtapaBcdboot));
            lista.Add(bcd);

            // ---------- bootrec clássico ----------
            var brec = F("boot-bootrec", CAT_BOOT,
                "Comandos clássicos de boot (bootrec fixmbr/fixboot/rebuildbcd)",
                "A trinca tradicional de reparo de boot, para quem prefere o método clássico.",
                "Roda em sequência: bootrec /fixmbr (regrava o registro mestre de inicialização), bootrec /fixboot (novo setor de boot) e bootrec /rebuildbcd (procura instalações do Windows e refaz a lista de boot).\r\n\r\n" +
                "Observações: em Windows recentes o /fixboot pode retornar \"Acesso negado\" — é limitação conhecida; nesse caso use o reparo \"Recriar arquivos de boot (bcdboot)\", que resolve o mesmo problema.");
            brec.Disponivel = SoPE;
            brec.Etapas.Add(ECmd("bootrec /fixmbr...", 25, "bootrec.exe", "/fixmbr", 5 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.NaoEncontrado) return EtapaResultado.Alerta("bootrec.exe não existe neste PE — use o reparo bcdboot.");
                    return r.ExitCode == 0
                        ? EtapaResultado.Sucesso("MBR regravado.")
                        : EtapaResultado.Alerta("fixmbr retornou código " + r.ExitCode + ".");
                }));
            brec.Etapas.Add(ECmd("bootrec /fixboot...", 25, "bootrec.exe", "/fixboot", 5 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.NaoEncontrado) return EtapaResultado.Alerta("bootrec.exe não disponível.");
                    if (r.Contem("acesso negado", "access is denied"))
                        return EtapaResultado.Alerta("\"Acesso negado\" no /fixboot — limitação conhecida do Windows 10/11. Use o reparo bcdboot, que cumpre o mesmo papel.");
                    return r.ExitCode == 0
                        ? EtapaResultado.Sucesso("Setor de boot regravado.")
                        : EtapaResultado.Alerta("fixboot retornou código " + r.ExitCode + ".");
                }));
            brec.Etapas.Add(ECmd("bootrec /rebuildbcd (respondendo automaticamente)...", 50,
                "bootrec.exe", "/rebuildbcd", 10 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.NaoEncontrado) return EtapaResultado.Alerta("bootrec.exe não disponível.");
                    if (r.Contem("total de instalações do windows identificadas: 0", "total identified windows installations: 0"))
                        return EtapaResultado.Alerta("O rebuildbcd não encontrou instalações (comum quando o BCD atual está OK ou o disco tem problema). Use o reparo bcdboot.");
                    return EtapaResultado.Sucesso("rebuildbcd executado — confira no log se a instalação foi adicionada.");
                },
                Runner.Enc.OEM, "S\r\nA\r\nY\r\nT\r\n"));
            lista.Add(brec);

            // ---------- bootsect ----------
            var bsect = F("boot-bootsect", CAT_BOOT,
                "Regravar setor de inicialização (bootsect /nt60)",
                "Para máquinas BIOS/Legado com setor de boot danificado por vírus ou formatação.",
                "Grava o código de inicialização atual do Windows (BOOTMGR) no setor de boot de todos os volumes e no MBR do disco. Corrige máquinas BIOS/Legado que mostram tela preta com cursor piscando ou \"Invalid partition table\".\r\n\r\nEm máquinas UEFI esta ferramenta não se aplica.");
            bsect.Disponivel = SoPE;
            bsect.Etapas.Add(ECmd("Executando bootsect /nt60 ALL /mbr...", 100,
                "bootsect.exe", "/nt60 ALL /mbr", 5 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.NaoEncontrado) return EtapaResultado.Alerta("bootsect.exe não existe neste PE.");
                    if (r.ExitCode == 0) return EtapaResultado.Sucesso("Código de inicialização regravado em todos os volumes e no MBR.");
                    return EtapaResultado.Alerta("bootsect retornou código " + r.ExitCode + " — veja o log.");
                }));
            lista.Add(bsect);

            // ---------- modo de segurança ON ----------
            var safeOn = F("boot-safemode-on", CAT_BOOT,
                "Ligar Modo de Segurança na próxima inicialização",
                "Força o Windows a iniciar em Modo de Segurança (para remover vírus, drivers ruins etc.).",
                "Configura o Windows para iniciar em Modo de Segurança (mínimo) no próximo boot — sem precisar apertar F8. Ideal para remover programas/drivers problemáticos ou malware que não deixa o Windows normal funcionar.\r\n\r\n" +
                "IMPORTANTE: o Windows vai iniciar SEMPRE em Modo de Segurança até você rodar o reparo \"Desligar Modo de Segurança\".");
            safeOn.RequerReinicio = true;
            safeOn.Etapas.Add(new Etapa("Ativando o Modo de Segurança no BCD...", 100,
                delegate(Contexto ctx) { return EtapaBcdeditSafeboot(ctx, true); }));
            lista.Add(safeOn);

            // ---------- modo de segurança OFF ----------
            var safeOff = F("boot-safemode-off", CAT_BOOT,
                "Desligar Modo de Segurança (voltar ao normal)",
                "Remove a inicialização forçada em Modo de Segurança.",
                "Desfaz a configuração de Modo de Segurança: no próximo reinício o Windows volta a iniciar normalmente. Use também quando um cliente chega com a máquina \"presa\" em Modo de Segurança.");
            safeOff.RequerReinicio = true;
            safeOff.Etapas.Add(new Etapa("Removendo o Modo de Segurança do BCD...", 100,
                delegate(Contexto ctx) { return EtapaBcdeditSafeboot(ctx, false); }));
            lista.Add(safeOff);

            // ---------- loop de reparo automático ----------
            var arOff = F("boot-autorepair-off", CAT_BOOT,
                "Sair do loop de \"Preparando Reparo Automático\"",
                "Desativa o reparo automático que fica em loop infinito e deixa o Windows tentar iniciar.",
                "Algumas máquinas ficam presas num ciclo: \"Preparando Reparo Automático\" → \"Diagnosticando\" → reinicia → repete para sempre. Este reparo desativa o disparo automático do reparo, permitindo que o Windows tente iniciar de verdade (e mostre o erro real, se houver).\r\n\r\n" +
                "Para reativar depois: rode o reparo \"Reativar o Reparo Automático\".");
            arOff.RequerReinicio = true;
            arOff.Etapas.Add(new Etapa("Desativando o disparo do Reparo Automático (recoveryenabled=No)...", 100,
                delegate(Contexto ctx) { return EtapaBcdeditRecovery(ctx, false); }));
            lista.Add(arOff);

            var arOn = F("boot-autorepair-on", CAT_BOOT,
                "Reativar o Reparo Automático do Windows",
                "Restaura o comportamento padrão do Windows de oferecer reparo quando o boot falha.",
                "Reativa o disparo automático do Ambiente de Recuperação quando a inicialização falha (recoveryenabled=Yes). Use depois que o problema de boot estiver resolvido, para o cliente voltar a ter a proteção padrão.");
            arOn.Etapas.Add(new Etapa("Reativando o Reparo Automático (recoveryenabled=Yes)...", 100,
                delegate(Contexto ctx) { return EtapaBcdeditRecovery(ctx, true); }));
            lista.Add(arOn);
        }

        // bcdboot: recria arquivos de boot. Online usa o proprio Windows;
        // no PE usa a instalacao alvo. /f ALL cobre UEFI e BIOS.
        static EtapaResultado EtapaBcdboot(Contexto ctx)
        {
            string win = ctx.WinDir;
            var r = ctx.Rodar("bcdboot.exe", win + " /f ALL /l pt-BR", 5 * 60 * 1000);
            if (r.NaoEncontrado)
                return EtapaResultado.Falha("bcdboot.exe não está disponível neste ambiente.");
            if (r.Contem("criados com êxito", "successfully created", "êxito"))
                return EtapaResultado.Sucesso("Arquivos de inicialização recriados com sucesso apontando para " + win + ". O computador deve voltar a dar boot normalmente.");

            // Plano B (UEFI): montar a partição EFI e gravar direto nela
            if (ctx.Sys != null && ctx.Sys.FirmwareUEFI)
            {
                string letra = MontarESP(ctx);
                if (letra != null)
                {
                    var r2 = ctx.Rodar("bcdboot.exe", win + " /s " + letra + " /f UEFI /l pt-BR", 5 * 60 * 1000);
                    if (r2.Contem("criados com êxito", "successfully created", "êxito"))
                        return EtapaResultado.Sucesso("Arquivos de boot UEFI recriados na partição EFI (" + letra + ") apontando para " + win + ".");
                }
            }
            return EtapaResultado.Falha("O bcdboot não conseguiu criar os arquivos de boot (código " + r.ExitCode + "). Verifique no log se a partição de sistema existe; pode ser preciso recriar a partição EFI/Sistema com o diskpart.");
        }

        // Monta a particao EFI (ESP) numa letra livre via mountvol /S.
        static string MontarESP(Contexto ctx)
        {
            string[] letras = { "S:", "T:", "U:" };
            foreach (string l in letras)
            {
                try
                {
                    if (Directory.Exists(l + "\\")) continue; // ja usada
                    var r = ctx.Rodar("mountvol.exe", l + " /S", 30000);
                    if (r.ExitCode == 0 && Directory.Exists(l + "\\")) return l;
                }
                catch { }
            }
            return null;
        }

        // Localiza o BCD para edicao offline (modo PE).
        public static string EncontrarBCDOffline()
        {
            var candidatos = new List<string>();
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    if (!di.IsReady || di.DriveType != DriveType.Fixed) continue;
                    string root = di.RootDirectory.FullName;
                    if (root.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) continue;
                    candidatos.Add(Path.Combine(root, @"EFI\Microsoft\Boot\BCD"));
                    candidatos.Add(Path.Combine(root, @"Boot\BCD"));
                }
                catch { }
            }
            foreach (string c in candidatos)
                if (File.Exists(c)) return c;
            return null;
        }

        static EtapaResultado EtapaBcdeditSafeboot(Contexto ctx, bool ligar)
        {
            string store = "";
            if (!ctx.Online)
            {
                string bcd = EncontrarBCDOffline();
                if (bcd == null)
                {
                    // tenta montar a ESP e procurar de novo
                    MontarESP(ctx);
                    bcd = EncontrarBCDOffline();
                }
                if (bcd == null)
                    return EtapaResultado.Falha("Não encontrei o arquivo BCD nos discos. Rode antes o reparo \"Recriar arquivos de boot (bcdboot)\".");
                store = "/store \"" + bcd + "\" ";
            }
            string args = ligar
                ? store + "/set {default} safeboot minimal"
                : store + "/deletevalue {default} safeboot";
            var r = ctx.Rodar("bcdedit.exe", args, 60000);
            if (r.ExitCode == 0)
                return EtapaResultado.Sucesso(ligar
                    ? "Modo de Segurança ATIVADO para o próximo boot. Lembre de desativar depois do atendimento."
                    : "Modo de Segurança removido — o Windows volta a iniciar normalmente.");
            if (!ligar && r.Contem("elemento", "element"))
                return EtapaResultado.Sucesso("O Modo de Segurança já estava desligado — nada a fazer.");
            return EtapaResultado.Falha("bcdedit retornou código " + r.ExitCode + " — veja o log.");
        }

        static EtapaResultado EtapaBcdeditRecovery(Contexto ctx, bool ligar)
        {
            string store = "";
            if (!ctx.Online)
            {
                string bcd = EncontrarBCDOffline();
                if (bcd == null) { MontarESP(ctx); bcd = EncontrarBCDOffline(); }
                if (bcd == null)
                    return EtapaResultado.Falha("Não encontrei o arquivo BCD nos discos. Rode antes o reparo \"Recriar arquivos de boot (bcdboot)\".");
                store = "/store \"" + bcd + "\" ";
            }
            var r = ctx.Rodar("bcdedit.exe", store + "/set {default} recoveryenabled " + (ligar ? "Yes" : "No"), 60000);
            if (r.ExitCode == 0)
                return EtapaResultado.Sucesso(ligar
                    ? "Reparo Automático reativado (comportamento padrão restaurado)."
                    : "Reparo Automático desativado. Reinicie: o Windows vai tentar iniciar direto, sem entrar no loop de reparo.");
            return EtapaResultado.Falha("bcdedit retornou código " + r.ExitCode + " — veja o log.");
        }

        // ============================================================
        // CATEGORIA: Rede e Internet
        // ============================================================
        static void Rede(List<Fix> lista)
        {
            // ---------- reparo geral ----------
            var geral = F("net-reparo-geral", CAT_NET,
                "Reparo geral de rede (sequência completa)",
                "O \"pacote completo\": limpa DNS, renova IP e redefine Winsock e TCP/IP.",
                "Executa em sequência os quatro reparos clássicos de rede:\r\n\r\n" +
                "1) Limpa o cache DNS (endereços de sites memorizados errados);\r\n" +
                "2) Libera e renova o endereço IP com o roteador;\r\n" +
                "3) Redefine o Winsock (camada de conexão que programas usam — alvo comum de malware);\r\n" +
                "4) Redefine a pilha TCP/IP para o padrão de fábrica.\r\n\r\n" +
                "Resolve a maioria dos casos de \"conectado mas sem internet\", páginas que não abrem e rede lenta após remoção de vírus.");
            geral.Disponivel = SoOnline;
            geral.RequerReinicio = true;
            geral.Etapas.Add(ECmd("1/4 — Limpando o cache DNS...", 10, "ipconfig.exe", "/flushdns", 30000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("Cache DNS limpo."); }));
            geral.Etapas.Add(new Etapa("2/4 — Liberando e renovando o endereço IP...", 30, EtapaRenovarIP));
            geral.Etapas.Add(ECmd("3/4 — Redefinindo o Winsock...", 30, "netsh.exe", "winsock reset", 60000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("Winsock redefinido para o padrão."); }));
            geral.Etapas.Add(ECmd("4/4 — Redefinindo a pilha TCP/IP...", 30, "netsh.exe", "int ip reset", 60000,
                delegate(RunResult r, Contexto ctx)
                {
                    return EtapaResultado.Sucesso("Pilha TCP/IP redefinida. (Linhas de \"Acesso negado\" em itens isolados são normais e não atrapalham.)");
                }));
            lista.Add(geral);

            // ---------- flush dns ----------
            var dns = F("net-flushdns", CAT_NET,
                "Limpar cache DNS",
                "Sites específicos não abrem ou abrem a página errada? Limpe os endereços memorizados.",
                "O Windows memoriza os endereços dos sites visitados (cache DNS). Quando um site muda de servidor ou o cache corrompe, o navegador tenta o endereço velho e falha. Este reparo apaga o cache — o Windows volta a perguntar os endereços corretos.");
            dns.Disponivel = SoOnline;
            dns.Etapas.Add(ECmd("Limpando o cache de resolução DNS...", 100, "ipconfig.exe", "/flushdns", 30000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("Cache DNS limpo com sucesso. Teste abrir o site problemático de novo."); }));
            lista.Add(dns);

            // ---------- renovar IP ----------
            var renew = F("net-renovar-ip", CAT_NET,
                "Liberar e renovar endereço IP (DHCP)",
                "Pede um endereço novo ao roteador — para conflitos de IP e o famoso IP 169.254.x.x.",
                "Descarta o endereço IP atual e solicita um novo ao roteador. Resolve conflito de IP (duas máquinas com o mesmo endereço), IP \"fantasma\" 169.254.x.x (quando o roteador não respondeu) e mudanças de rede que não pegaram.");
            renew.Disponivel = SoOnline;
            renew.Etapas.Add(new Etapa("Liberando e renovando o IP em todos os adaptadores...", 100, EtapaRenovarIP));
            lista.Add(renew);

            // ---------- winsock ----------
            var ws = F("net-winsock", CAT_NET,
                "Redefinir Winsock",
                "Conserta a camada de conexão dos programas — clássico após remoção de vírus.",
                "O Winsock é a camada que conecta os programas (navegador, e-mail...) à rede. Vírus, antivírus mal removidos e \"otimizadores\" costumam corrompê-la — a máquina conecta no Wi-Fi mas NADA navega. Este reparo devolve o Winsock ao estado de fábrica.");
            ws.Disponivel = SoOnline;
            ws.RequerReinicio = true;
            ws.Etapas.Add(ECmd("Redefinindo o catálogo Winsock...", 100, "netsh.exe", "winsock reset", 60000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("Winsock redefinido com sucesso."); }));
            lista.Add(ws);

            // ---------- tcp/ip ----------
            var ip = F("net-tcpip", CAT_NET,
                "Redefinir pilha TCP/IP",
                "Volta as configurações de rede do Windows ao padrão de fábrica.",
                "Reescreve as chaves de registro da pilha TCP/IP (IPv4 e IPv6) para os valores originais do Windows. Use quando configurações manuais, VPNs ou programas de rede deixaram a conexão bagunçada.");
            ip.Disponivel = SoOnline;
            ip.RequerReinicio = true;
            ip.Etapas.Add(ECmd("Redefinindo TCP/IP (IPv4)...", 60, "netsh.exe", "int ip reset", 60000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("TCP/IP (IPv4) redefinido. Linhas de \"Acesso negado\" isoladas são normais."); }));
            ip.Etapas.Add(ECmd("Redefinindo TCP/IP (IPv6)...", 40, "netsh.exe", "int ipv6 reset", 60000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("TCP/IP (IPv6) redefinido."); }));
            lista.Add(ip);

            // ---------- proxy ----------
            var proxy = F("net-proxy", CAT_NET,
                "Remover proxy indevido",
                "Malware adora instalar proxy para interceptar a navegação — este reparo remove.",
                "Verifica e remove configurações de proxy do Windows (usuário e sistema). Sintoma típico: \"Não é possível conectar-se ao proxy\" em todos os navegadores, ou internet que só funciona em um programa. Após remoção de vírus, este reparo é quase obrigatório.");
            proxy.Disponivel = SoOnline;
            proxy.Etapas.Add(new Etapa("Removendo proxy do usuário (Internet Settings)...", 50,
                delegate(Contexto ctx)
                {
                    int removidos = 0;
                    try
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                        {
                            if (k != null)
                            {
                                object en = k.GetValue("ProxyEnable");
                                if (en != null && Convert.ToInt32(en) != 0) { k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord); removidos++; }
                                if (k.GetValue("ProxyServer") != null) { k.DeleteValue("ProxyServer", false); removidos++; }
                                if (k.GetValue("AutoConfigURL") != null) { k.DeleteValue("AutoConfigURL", false); removidos++; ctx.Log("    Removido script de configuração automática (AutoConfigURL) — vetor comum de malware."); }
                            }
                        }
                    }
                    catch (Exception ex) { return EtapaResultado.Falha("Erro no registro: " + ex.Message); }
                    return removidos > 0
                        ? EtapaResultado.Sucesso("Proxy do usuário REMOVIDO (" + removidos + " configuração(ões) encontradas). Havia proxy configurado — se o cliente não usa proxy corporativo, era indevido.")
                        : EtapaResultado.Sucesso("Nenhum proxy configurado no perfil do usuário — ok.");
                }));
            proxy.Etapas.Add(ECmd("Removendo proxy do sistema (WinHTTP)...", 50,
                "netsh.exe", "winhttp reset proxy", 30000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("Proxy do sistema (WinHTTP) redefinido para acesso direto."); }));
            lista.Add(proxy);

            // ---------- DNS público ----------
            var dnsPub = F("net-dns-publico", CAT_NET,
                "Usar DNS rápido e confiável (Google/Cloudflare)",
                "Troca o DNS do provedor por 8.8.8.8 e 1.1.1.1 — resolve lentidão e sites que não abrem.",
                "Configura os adaptadores ativos para usar os servidores DNS do Google (8.8.8.8) e da Cloudflare (1.1.1.1). O DNS de muitos provedores é lento ou instável — sintoma: páginas demoram a \"começar\" a abrir, apps dizem \"sem conexão\" com o Wi-Fi cheio.\r\n\r\nPara voltar ao automático, use o reparo \"Voltar DNS automático\".");
            dnsPub.Disponivel = SoOnline;
            dnsPub.Etapas.Add(new Etapa("Aplicando DNS 8.8.8.8 / 1.1.1.1 nos adaptadores ativos...", 80,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell(
                        "Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { " +
                        "Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ('8.8.8.8','1.1.1.1'); " +
                        "Write-Output ('DNS aplicado em: ' + $_.Name) }", 120000);
                    if (r.Saida.ToLowerInvariant().Contains("dns aplicado"))
                        return EtapaResultado.Sucesso("DNS público aplicado nos adaptadores ativos (detalhe no log).");
                    return EtapaResultado.Alerta("Nenhum adaptador ativo encontrado ou o comando falhou — veja o log.");
                }));
            dnsPub.Etapas.Add(ECmd("Limpando o cache DNS para valer imediatamente...", 20,
                "ipconfig.exe", "/flushdns", 30000,
                delegate(RunResult r, Contexto ctx) { return EtapaResultado.Sucesso("Cache limpo — o novo DNS já está em uso."); }));
            lista.Add(dnsPub);

            // ---------- DNS automático ----------
            var dnsAuto = F("net-dns-auto", CAT_NET,
                "Voltar DNS automático (do roteador/provedor)",
                "Desfaz DNS manual e volta a receber o DNS automaticamente.",
                "Remove servidores DNS fixados manualmente e volta os adaptadores a obter DNS automaticamente (DHCP). Use quando um DNS manual antigo (ou colocado por malware) está causando problema.");
            dnsAuto.Disponivel = SoOnline;
            dnsAuto.Etapas.Add(new Etapa("Voltando DNS automático nos adaptadores...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell(
                        "Get-NetAdapter -Physical | ForEach-Object { " +
                        "Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses; " +
                        "Write-Output ('DNS automático em: ' + $_.Name) }", 120000);
                    if (r.Saida.ToLowerInvariant().Contains("autom"))
                        return EtapaResultado.Sucesso("Adaptadores voltaram ao DNS automático.");
                    return EtapaResultado.Alerta("Confira o log — o comando pode ter falhado.");
                }));
            lista.Add(dnsAuto);

            // ---------- reiniciar adaptadores ----------
            var adap = F("net-adaptadores", CAT_NET,
                "Reiniciar adaptadores de rede",
                "Desliga e religa as placas de rede/Wi-Fi — o \"tira e põe o cabo\" por software.",
                "Desativa e reativa os adaptadores de rede físicos. Equivale a remover e reconectar o cabo/Wi-Fi, forçando renegociação completa com o roteador. A conexão cai por alguns segundos durante o processo.");
            adap.Disponivel = SoOnline;
            adap.Etapas.Add(new Etapa("Reiniciando os adaptadores físicos (a rede cai por instantes)...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell(
                        "Get-NetAdapter -Physical | Where-Object { $_.Status -ne 'Disabled' } | ForEach-Object { " +
                        "Restart-NetAdapter -Name $_.Name -Confirm:$false; Write-Output ('Reiniciado: ' + $_.Name) }",
                        180000);
                    System.Threading.Thread.Sleep(4000);
                    if (r.Saida.ToLowerInvariant().Contains("reiniciado"))
                        return EtapaResultado.Sucesso("Adaptadores reiniciados. Aguarde ~10 segundos e teste a conexão.");
                    return EtapaResultado.Alerta("Nenhum adaptador reiniciado — veja o log.");
                }));
            lista.Add(adap);

            // ---------- serviços de rede ----------
            var svcs = F("net-servicos", CAT_NET,
                "Reiniciar serviços de rede do Windows",
                "Religa os serviços que cuidam de DHCP, Wi-Fi e identificação de rede.",
                "Reinicia (ou inicia, se parados) os serviços essenciais de rede: Cliente DHCP, Configuração Automática de WLAN (Wi-Fi), Reconhecimento de Localização de Rede e Lista de Redes. Serviço de rede travado é causa comum de \"globo sem internet\" na bandeja.");
            svcs.Disponivel = SoOnline;
            svcs.Etapas.Add(new Etapa("Reiniciando serviços de rede (Dhcp, WlanSvc, NlaSvc, netprofm)...", 100,
                delegate(Contexto ctx)
                {
                    string[] nomes = { "Dhcp", "WlanSvc", "NlaSvc", "netprofm" };
                    int ok = 0, pulados = 0;
                    foreach (string n in nomes)
                    {
                        if (!ServicoExiste(n)) { pulados++; continue; }
                        ctx.Log("  Serviço: " + n);
                        if (ReiniciarServico(n, ctx.Log)) ok++; else pulados++;
                    }
                    return EtapaResultado.Sucesso(ok + " serviço(s) de rede reiniciado(s)" +
                        (pulados > 0 ? " (" + pulados + " não aplicável(is) nesta máquina)" : "") + ".");
                }));
            lista.Add(svcs);

            // ---------- descoberta de rede ----------
            var desc = F("net-descoberta", CAT_NET,
                "Ativar descoberta de rede e compartilhamento",
                "Para quando o PC não enxerga impressoras de rede e outros computadores.",
                "Liga os serviços de descoberta (Function Discovery, SSDP, UPnP) e libera as regras de firewall de \"Descoberta de Rede\" e \"Compartilhamento de Arquivos e Impressoras\". Sem isso, impressoras de rede e pastas compartilhadas ficam invisíveis.");
            desc.Disponivel = SoOnline;
            desc.Etapas.Add(new Etapa("Ligando serviços de descoberta (fdPHost, FDResPub, SSDPSRV, upnphost)...", 50,
                delegate(Contexto ctx)
                {
                    string[] nomes = { "fdPHost", "FDResPub", "SSDPSRV", "upnphost" };
                    int ok = 0;
                    foreach (string n in nomes)
                    {
                        if (!ServicoExiste(n)) continue;
                        try { ServicoTipoInicio(n, 2); } catch { }
                        if (IniciarServico(n, 20, ctx.Log)) ok++;
                    }
                    return EtapaResultado.Sucesso(ok + " serviço(s) de descoberta ativos e configurados para iniciar com o Windows.");
                }));
            desc.Etapas.Add(new Etapa("Liberando regras de firewall (Descoberta de Rede + Compartilhamento)...", 50,
                delegate(Contexto ctx)
                {
                    // Nomes de grupo via string indireta (@FirewallAPI.dll) — independem do idioma
                    var r1 = ctx.Rodar("netsh.exe",
                        "advfirewall firewall set rule group=\"@FirewallAPI.dll,-32752\" new enable=Yes", 60000);
                    var r2 = ctx.Rodar("netsh.exe",
                        "advfirewall firewall set rule group=\"@FirewallAPI.dll,-28502\" new enable=Yes", 60000);
                    if (r1.ExitCode == 0 || r2.ExitCode == 0)
                        return EtapaResultado.Sucesso("Regras de firewall da descoberta de rede e do compartilhamento liberadas.");
                    return EtapaResultado.Alerta("O firewall não aceitou os grupos (veja o log). Se usa antivírus com firewall próprio, ajuste nele.");
                }));
            lista.Add(desc);

            // ---------- firewall reset ----------
            var fw = F("net-firewall", CAT_NET,
                "Restaurar firewall do Windows ao padrão",
                "Remove regras bagunçadas/maliciosas do firewall e volta à configuração de fábrica.",
                "Apaga todas as regras personalizadas do Firewall do Windows e restaura o padrão de fábrica. Use quando programas de \"otimização\", malware ou tentativas anteriores de configuração deixaram o firewall bloqueando o que não devia.");
            fw.Aviso = "Regras criadas manualmente ou por programas (jogos, câmeras, sistemas da empresa) serão APAGADAS e podem precisar ser refeitas/liberadas de novo.";
            fw.Disponivel = SoOnline;
            fw.Etapas.Add(ECmd("Restaurando a política padrão do firewall...", 100,
                "netsh.exe", "advfirewall reset", 60000,
                delegate(RunResult r, Contexto ctx)
                { return EtapaResultado.Sucesso("Firewall restaurado ao padrão de fábrica."); }));
            lista.Add(fw);

            // ---------- hora ----------
            var hora = F("net-hora", CAT_NET,
                "Corrigir data e hora (sincronizar)",
                "Hora errada derruba sites com \"certificado inválido\" — este reparo sincroniza com a internet.",
                "Liga o serviço de Horário do Windows e força a sincronização com os servidores de hora. Relógio atrasado (bateria da placa fraca, por exemplo) faz TODOS os sites seguros reclamarem de certificado e apps de banco falharem.");
            hora.Disponivel = SoOnline;
            hora.Etapas.Add(new Etapa("Ligando o serviço de Horário do Windows...", 30,
                delegate(Contexto ctx)
                {
                    try { ServicoTipoInicio("W32Time", 3); } catch { }
                    IniciarServico("W32Time", 20, ctx.Log);
                    return EtapaResultado.Sucesso("Serviço de horário ativo.");
                }));
            hora.Etapas.Add(ECmd("Sincronizando com o servidor de hora...", 70,
                "w32tm.exe", "/resync /force", 60000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.ExitCode == 0)
                        return EtapaResultado.Sucesso("Data e hora sincronizadas com sucesso. Agora é " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + ".");
                    return EtapaResultado.Alerta("Não foi possível sincronizar (sem internet?). Se o relógio atrasa toda vez que desliga, a bateria da placa-mãe (CR2032) precisa de troca.");
                }));
            lista.Add(hora);

            // ---------- reset total ----------
            var total = F("net-reset-total", CAT_NET,
                "Redefinição TOTAL de rede (último recurso)",
                "Remove e reinstala todos os adaptadores e componentes de rede — como o botão do Windows.",
                "Equivale ao botão \"Redefinição de rede\" das Configurações do Windows: desinstala TODOS os adaptadores de rede e componentes (que o Windows reinstala no próximo reinício) e zera todas as configurações.\r\n\r\nUse somente quando os outros reparos de rede não resolveram.");
            total.Aviso = "Redes Wi-Fi salvas, VPNs e adaptadores virtuais (VirtualBox, VMware...) serão REMOVIDOS. Será preciso reconectar o Wi-Fi com a senha e reinstalar clientes VPN. Reinício obrigatório.";
            total.Disponivel = SoOnline;
            total.RequerReinicio = true;
            total.Etapas.Add(ECmd("Removendo e marcando componentes de rede para reinstalação...", 100,
                "netcfg.exe", "-d", 5 * 60 * 1000,
                delegate(RunResult r, Contexto ctx)
                {
                    if (r.NaoEncontrado) return EtapaResultado.Falha("netcfg.exe não disponível.");
                    return EtapaResultado.Sucesso("Componentes de rede redefinidos. REINICIE o computador: o Windows reinstala tudo do zero no próximo boot.");
                }));
            lista.Add(total);
        }

        static EtapaResultado EtapaRenovarIP(Contexto ctx)
        {
            ctx.Rodar("ipconfig.exe", "/release", 60000);
            var r = ctx.Rodar("ipconfig.exe", "/renew", 120000);
            string baixa = (r.Saida ?? "").ToLowerInvariant();
            if (baixa.Contains("169.254"))
                return EtapaResultado.Alerta("O roteador NÃO respondeu (IP 169.254.x.x). Verifique cabo/Wi-Fi e reinicie o roteador; se persistir, o problema está no roteador ou no caminho até ele.");
            if (r.ExitCode == 0)
                return EtapaResultado.Sucesso("Endereço IP renovado com o roteador.");
            return EtapaResultado.Alerta("A renovação reportou erro em algum adaptador (normal quando há adaptadores sem uso). Veja o log.");
        }
    }
}
