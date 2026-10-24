using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace DiagnosticoReparo
{
    public enum Gravidade { Ok, Info, Atencao, Critico }

    // Um achado do pré-diagnóstico: o que foi detectado, a gravidade e os
    // reparos recomendados (ids do catálogo) para tratá-lo.
    public class Achado
    {
        public Gravidade Nivel;
        public string Titulo;
        public string Detalhe;
        public List<string> FixIds = new List<string>();

        public Achado(Gravidade nivel, string titulo, string detalhe, params string[] fixIds)
        {
            Nivel = nivel; Titulo = titulo; Detalhe = detalhe;
            if (fixIds != null) FixIds.AddRange(fixIds);
        }
    }

    // ---------------------------------------------------------------
    // Pré-diagnóstico rápido: uma bateria de verificações leves que
    // aponta prováveis problemas e já sugere os reparos certos.
    // Adapta-se ao ambiente (Windows normal x WinPE de recuperação).
    // ---------------------------------------------------------------
    public static class PreDiag
    {
        public static List<Achado> Executar(SystemInfo sys, Action<string> passo, Action<int> pct)
        {
            var achados = new List<Achado>();
            if (sys.ModoPE) ExecutarPE(sys, achados, passo, pct);
            else ExecutarOnline(sys, achados, passo, pct);
            if (pct != null) pct(100);
            if (achados.Count == 0)
                achados.Add(new Achado(Gravidade.Ok, "Nenhum problema evidente encontrado",
                    "As verificações rápidas não apontaram falhas. Se o cliente relata um problema específico, use a aba \"Reparos\" e escolha a categoria correspondente."));
            return achados;
        }

        // ============================================================
        // Windows em funcionamento
        // ============================================================
        static void ExecutarOnline(SystemInfo sys, List<Achado> achados, Action<string> passo, Action<int> pct)
        {
            passo("Verificando integridade dos arquivos do sistema...");
            if (pct != null) pct(8);
            VerificarDismRapido(achados);

            passo("Verificando o disco (bit de sujeira e SMART)...");
            if (pct != null) pct(20);
            VerificarDirtyBit(achados);
            VerificarSmart(sys, achados);
            VerificarEspaco(achados);

            passo("Verificando a rede e a internet...");
            if (pct != null) pct(38);
            VerificarRede(achados);
            VerificarProxy(achados);

            passo("Verificando o serviço e a fila de impressão...");
            if (pct != null) pct(55);
            VerificarImpressao(achados);

            passo("Verificando o perfil de usuário...");
            if (pct != null) pct(68);
            VerificarPerfilTemporario(achados);

            passo("Verificando o Windows Update...");
            if (pct != null) pct(78);
            VerificarUpdateBloqueado(achados);

            passo("Verificando eventos e estado do sistema...");
            if (pct != null) pct(88);
            VerificarReinicioPendente(achados);
            VerificarHora(achados);
            VerificarEventosCriticos(achados);
        }

        // ============================================================
        // Ambiente de recuperação (WinPE)
        // ============================================================
        static void ExecutarPE(SystemInfo sys, List<Achado> achados, Action<string> passo, Action<int> pct)
        {
            passo("Procurando instalações do Windows nos discos...");
            if (pct != null) pct(20);
            if (sys.Instalacoes.Count == 0)
            {
                achados.Add(new Achado(Gravidade.Critico,
                    "Nenhuma instalação de Windows encontrada nos discos",
                    "Não localizei uma pasta \\Windows válida em nenhum disco. Causas possíveis: disco não inicializado/sem letra, partição do sistema perdida, ou disco com falha física. " +
                    "Verifique no gerenciador de discos do pendrive de recuperação se o disco aparece; se aparecer sem partições, houve perda da tabela de partições.",
                    "sys-chkdsk-corrigir"));
                return;
            }

            achados.Add(new Achado(Gravidade.Info,
                sys.Instalacoes.Count + " instalação(ões) de Windows encontrada(s)",
                "Selecione no topo qual instalação será reparada. Os reparos abaixo agirão sobre ela."));

            passo("Verificando os arquivos de inicialização (BCD/bootmgr)...");
            if (pct != null) pct(45);
            VerificarBootOffline(sys, achados);

            passo("Verificando atualização travada...");
            if (pct != null) pct(65);
            VerificarPendingOffline(sys, achados);

            passo("Verificando o disco...");
            if (pct != null) pct(85);
            achados.Add(new Achado(Gravidade.Info,
                "Recomendado antes de tudo: verificar o disco",
                "Como o Windows do cliente não está em uso, dá para corrigir o disco agora (chkdsk /f). Erro de disco é a causa mais comum de falha de inicialização.",
                "sys-chkdsk-corrigir", "boot-reparo-auto"));
        }

        // ------------------------------------------------------------
        // Verificações individuais
        // ------------------------------------------------------------
        static void VerificarDismRapido(List<Achado> achados)
        {
            try
            {
                var r = Runner.Rodar("dism.exe", "/Online /Cleanup-Image /CheckHealth", 60000, null, null);
                if (r.Contem("repairable", "reparável"))
                    achados.Add(new Achado(Gravidade.Atencao,
                        "A imagem do Windows está marcada como danificada",
                        "O DISM indica dano (reparável) no depósito de componentes. Isso causa erros diversos e falhas de atualização.",
                        "sys-reparo-completo", "sys-dism-restore"));
                else if (r.Contem("not repairable", "não reparável"))
                    achados.Add(new Achado(Gravidade.Critico,
                        "Imagem do Windows danificada e não reparável localmente",
                        "Será preciso reparar com uma fonte externa (ISO/pendrive do Windows da mesma versão) ou considerar reinstalação por cima (upgrade in-place).",
                        "sys-dism-restore"));
            }
            catch { }
        }

        static void VerificarDirtyBit(List<Achado> achados)
        {
            try
            {
                var r = Runner.Rodar("fsutil.exe", "dirty query C:", 20000, null, null);
                if (r.Contem("está suja", "is dirty"))
                    achados.Add(new Achado(Gravidade.Atencao,
                        "O disco C: está marcado como \"sujo\"",
                        "O Windows detectou que o volume C: precisa de verificação (desligamento incorreto ou erro de disco). Recomenda-se agendar a correção na próxima inicialização.",
                        "sys-chkdsk-agendar"));
            }
            catch { }
        }

        static void VerificarSmart(SystemInfo sys, List<Achado> achados)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\wmi");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        if (Convert.ToBoolean(mo["PredictFailure"]))
                        {
                            achados.Add(new Achado(Gravidade.Critico,
                                "O disco está avisando FALHA IMINENTE (SMART)",
                                "O próprio disco prevê que vai falhar. FAÇA BACKUP DOS DADOS IMEDIATAMENTE e troque o disco (de preferência por um SSD). Reparos de software não resolvem falha física.",
                                "sys-ponto"));
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        static void VerificarEspaco(List<Achado> achados)
        {
            try
            {
                var di = new DriveInfo("C");
                if (di.IsReady)
                {
                    double pctLivre = 100.0 * di.AvailableFreeSpace / di.TotalSize;
                    double livreGB = di.AvailableFreeSpace / 1e9;
                    if (pctLivre < 8 || livreGB < 5)
                        achados.Add(new Achado(Gravidade.Atencao,
                            string.Format("Pouco espaço livre em C: ({0:0.0} GB, {1:0}%)", livreGB, pctLivre),
                            "Com o disco quase cheio o Windows fica lento, trava e não atualiza. Libere espaço com a limpeza.",
                            "limp-cleanmgr", "limp-temp", "limp-lixeira"));
                }
            }
            catch { }
        }

        static void VerificarRede(List<Achado> achados)
        {
            try
            {
                bool temIP = false, temGateway = false;
                using (var s = new ManagementObjectSearcher(
                    "SELECT IPAddress, DefaultIPGateway FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        var ips = mo["IPAddress"] as string[];
                        if (ips != null)
                            foreach (string ip in ips)
                                if (!ip.StartsWith("169.254") && ip.IndexOf(':') < 0) temIP = true;
                        var gw = mo["DefaultIPGateway"] as string[];
                        if (gw != null && gw.Length > 0) temGateway = true;
                    }
                }
                if (!temIP)
                {
                    achados.Add(new Achado(Gravidade.Atencao,
                        "Sem endereço IP válido (rede desconectada ou sem resposta do roteador)",
                        "Nenhum adaptador tem um IP válido. Pode ser cabo/Wi-Fi desconectado ou o roteador não respondeu (IP 169.254.x.x). Tente renovar o IP e reiniciar os adaptadores.",
                        "net-renovar-ip", "net-adaptadores", "net-servicos"));
                    return;
                }
                if (!temGateway)
                {
                    achados.Add(new Achado(Gravidade.Atencao,
                        "Sem gateway padrão (tem IP, mas não chega à internet)",
                        "A máquina tem IP mas não há gateway — normalmente não navega. Verifique o roteador e faça o reparo geral de rede.",
                        "net-reparo-geral", "net-renovar-ip"));
                    return;
                }
                // Tem IP e gateway: testa internet de verdade
                bool internet = false;
                try
                {
                    using (var ping = new System.Net.NetworkInformation.Ping())
                    {
                        var reply = ping.Send("1.1.1.1", 2500);
                        internet = reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                    }
                }
                catch { }
                if (internet)
                {
                    // IP+gateway+ping ok: checa se o DNS resolve
                    bool dnsOk = false;
                    try { System.Net.Dns.GetHostEntry("www.microsoft.com"); dnsOk = true; } catch { }
                    if (!dnsOk)
                        achados.Add(new Achado(Gravidade.Atencao,
                            "Internet conecta, mas os sites não abrem pelo nome (falha de DNS)",
                            "O ping funciona (há internet), porém os nomes de sites não são resolvidos. Clássico problema de DNS: limpe o cache e/ou troque para um DNS confiável.",
                            "net-dns-publico", "net-flushdns"));
                }
                else
                {
                    achados.Add(new Achado(Gravidade.Atencao,
                        "Conectado à rede local, mas SEM internet",
                        "A máquina tem IP e gateway, mas não alcança a internet (ping falhou). Pode ser problema no roteador/provedor ou na configuração de rede do Windows.",
                        "net-reparo-geral", "net-dns-publico"));
                }
            }
            catch { }
        }

        static void VerificarProxy(List<Achado> achados)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (k == null) return;
                    object en = k.GetValue("ProxyEnable");
                    object autoUrl = k.GetValue("AutoConfigURL");
                    if ((en != null && Convert.ToInt32(en) != 0) || autoUrl != null)
                        achados.Add(new Achado(Gravidade.Atencao,
                            "Há um proxy configurado no Windows",
                            "Um proxy está ativo. Se o cliente não usa proxy corporativo, quase sempre é sobra de malware/programa e derruba a navegação. Convém remover.",
                            "net-proxy"));
                }
            }
            catch { }
        }

        static void VerificarImpressao(List<Achado> achados)
        {
            // Serviço de spooler
            try
            {
                if (FixCatalog.ServicoExiste("Spooler"))
                {
                    using (var sc = new System.ServiceProcess.ServiceController("Spooler"))
                    {
                        if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                        {
                            achados.Add(new Achado(Gravidade.Atencao,
                                "O serviço de impressão (Spooler) está parado",
                                "Sem o Spooler nenhuma impressora funciona. Vamos religá-lo.",
                                "imp-spooler-reparo", "imp-fila"));
                        }
                    }
                }
            }
            catch { }
            // Fila presa
            try
            {
                string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"spool\PRINTERS");
                if (Directory.Exists(spool))
                {
                    int arquivos = Directory.GetFiles(spool).Length;
                    if (arquivos >= 2)
                        achados.Add(new Achado(Gravidade.Info,
                            "Há trabalhos parados na fila de impressão",
                            arquivos + " arquivo(s) na fila de impressão. Se algo não sai nem cancela, desentupir a fila resolve.",
                            "imp-fila"));
                }
            }
            catch { }
            // Impressora offline
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, WorkOffline FROM Win32_Printer"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        object wo = mo["WorkOffline"];
                        if (wo != null && Convert.ToBoolean(wo))
                        {
                            achados.Add(new Achado(Gravidade.Info,
                                "Impressora marcada como \"Offline\"",
                                "A impressora \"" + Convert.ToString(mo["Name"]) + "\" está em modo offline. Se ela está ligada e conectada, dá para forçar o modo on-line.",
                                "imp-online", "imp-fila"));
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        static void VerificarPerfilTemporario(List<Achado> achados)
        {
            try
            {
                // Perfil temporário: SID de sistema (S-1-5-18/19/20) ativo como usuário,
                // ou chaves .bak em ProfileList.
                bool bak = false;
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var pl = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                {
                    if (pl != null)
                        foreach (string sk in pl.GetSubKeyNames())
                            if (sk.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) { bak = true; break; }
                }
                bool logadoTemporario = Environment.GetEnvironmentVariable("USERPROFILE") != null &&
                    Environment.GetEnvironmentVariable("USERPROFILE").IndexOf("TEMP", StringComparison.OrdinalIgnoreCase) >= 0;
                if (bak || logadoTemporario)
                    achados.Add(new Achado(Gravidade.Atencao,
                        "Perfil de usuário com problema (possível perfil temporário)",
                        "Encontrei sinais de perfil corrompido/temporário — a área de trabalho aparece vazia e nada que você salva persiste. Dá para corrigir a marcação no registro.",
                        "usr-perfil-bak", "usr-novo-admin"));
            }
            catch { }
        }

        static void VerificarUpdateBloqueado(List<Achado> achados)
        {
            try
            {
                bool bloqueado = false;
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("NoAutoUpdate");
                        if (v != null && Convert.ToInt32(v) == 1) bloqueado = true;
                    }
                }
                if (FixCatalog.ServicoExiste("wuauserv"))
                {
                    using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv"))
                    {
                        if (k != null && Convert.ToInt32(k.GetValue("Start", 3)) == 4) bloqueado = true;
                    }
                }
                if (bloqueado)
                    achados.Add(new Achado(Gravidade.Info,
                        "O Windows Update parece desativado",
                        "Há política ou serviço desativando as atualizações (comum após \"otimizadores\"). Se o cliente reclama que não atualiza, dá para reativar.",
                        "wu-reativar", "wu-reset"));
            }
            catch { }
        }

        static void VerificarReinicioPendente(List<Achado> achados)
        {
            try
            {
                bool pendente = false;
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var k = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                        if (k != null) pendente = true;
                    using (var k = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                        if (k != null) pendente = true;
                }
                if (pendente)
                    achados.Add(new Achado(Gravidade.Info,
                        "Há uma reinicialização pendente",
                        "Uma atualização ou instalação espera reinício para concluir. Vários problemas somem só com o reinício — vale reiniciar antes de reparos maiores."));
            }
            catch { }
        }

        static void VerificarHora(List<Achado> achados)
        {
            try
            {
                // Se a hora local diverge muito do build/relógio, sites seguros quebram.
                // Sem internet aqui; então só alertamos se o ano for claramente absurdo.
                int ano = DateTime.Now.Year;
                if (ano < 2020 || ano > 2035)
                    achados.Add(new Achado(Gravidade.Atencao,
                        "Data e hora do computador parecem erradas",
                        "O relógio está em " + DateTime.Now.ToString("dd/MM/yyyy") + ". Hora errada derruba sites seguros e apps de banco. Sincronize com a internet.",
                        "net-hora"));
            }
            catch { }
        }

        static void VerificarEventosCriticos(List<Achado> achados)
        {
            try
            {
                // Kernel-Power 41 (desligamento inesperado) nas últimas 72h
                var r = Runner.PowerShell(
                    "$d=(Get-Date).AddHours(-72); " +
                    "$e=Get-WinEvent -FilterHashtable @{LogName='System';Id=41;StartTime=$d} -ErrorAction SilentlyContinue; " +
                    "if ($e) { Write-Output ('COUNT=' + $e.Count) }", 30000, null, null);
                int idx = r.Saida.IndexOf("COUNT=");
                if (idx >= 0)
                {
                    string num = "";
                    for (int i = idx + 6; i < r.Saida.Length && char.IsDigit(r.Saida[i]); i++) num += r.Saida[i];
                    int c;
                    if (int.TryParse(num, out c) && c >= 2)
                        achados.Add(new Achado(Gravidade.Atencao,
                            c + " desligamento(s) inesperado(s) nas últimas 72h",
                            "O evento Kernel-Power 41 indica que a máquina desligou/reiniciou sem avisar várias vezes. Causas comuns: fonte/bateria, superaquecimento, memória defeituosa ou driver. Vale testar a RAM e verificar o disco.",
                            "sys-memdiag", "sys-chkdsk-agendar"));
                }
            }
            catch { }
        }

        static void VerificarBootOffline(SystemInfo sys, List<Achado> achados)
        {
            string bcd = FixCatalog.EncontrarBCDOffline();
            if (bcd == null)
                achados.Add(new Achado(Gravidade.Critico,
                    "Arquivos de inicialização (BCD) não encontrados",
                    "Não localizei a lista de boot (BCD) nos discos. Isso explica \"BOOTMGR is missing\", \"BCD error\" e afins. Recriar os arquivos de boot costuma resolver.",
                    "boot-reparo-auto", "boot-bcdboot"));
            else
                achados.Add(new Achado(Gravidade.Info,
                    "Arquivos de inicialização localizados",
                    "O BCD foi encontrado. Se ainda assim não dá boot, recrie os arquivos de inicialização e verifique o disco.",
                    "boot-bcdboot", "boot-reparo-auto"));
        }

        static void VerificarPendingOffline(SystemInfo sys, List<Achado> achados)
        {
            try
            {
                foreach (var w in sys.Instalacoes)
                {
                    string pend = Path.Combine(w.WinDir, @"WinSxS\pending.xml");
                    if (File.Exists(pend))
                    {
                        achados.Add(new Achado(Gravidade.Atencao,
                            "Atualização travada detectada (" + w.Drive + ")",
                            "Há uma atualização do Windows interrompida no meio (pending.xml) em " + w.WinDir + ". Isso deixa o Windows preso reiniciando. Dá para desfazer as ações pendentes.",
                            "sys-revert-pending", "boot-reparo-auto"));
                        break;
                    }
                }
            }
            catch { }
        }
    }
}
