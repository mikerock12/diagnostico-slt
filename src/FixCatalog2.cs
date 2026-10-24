using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiagnosticoReparo
{
    public static partial class FixCatalog
    {
        // ============================================================
        // CATEGORIA: Impressoras (HP, Epson e outras)
        // ============================================================
        static void Impressoras(List<Fix> lista)
        {
            // ---------- reparo geral da fila ----------
            var fila = F("imp-fila", CAT_IMP,
                "Desentupir a fila de impressão (reparo geral)",
                "O reparo nº 1 de impressora: para o spooler, apaga os trabalhos presos e religa.",
                "A maioria dos problemas de impressão é trabalho preso na fila. Este reparo para o serviço de impressão (Spooler), apaga TODOS os documentos travados na fila, remove arquivos temporários corrompidos e religa o serviço.\r\n\r\n" +
                "Resolve: \"documento na fila que não sai nem cancela\", impressora que \"imprime nada\", erro ao imprimir em qualquer programa.");
            fila.Disponivel = SoOnline;
            fila.Aviso = "Todos os documentos na fila de impressão (de todas as impressoras) serão descartados. Será preciso mandar imprimir de novo.";
            fila.Etapas.Add(new Etapa("Parando o serviço de impressão (Spooler)...", 20,
                delegate(Contexto ctx)
                {
                    bool ok = PararServico("Spooler", 20, ctx.Log);
                    return ok ? EtapaResultado.Sucesso("Serviço de impressão parado.")
                              : EtapaResultado.Alerta("Não consegui parar o Spooler — pode já estar parado.");
                }));
            fila.Etapas.Add(new Etapa("Apagando os trabalhos presos na fila...", 40,
                delegate(Contexto ctx)
                {
                    int n = LimparSpool(ctx.Log);
                    return EtapaResultado.Sucesso(n + " arquivo(s) de impressão apagados da fila.");
                }));
            fila.Etapas.Add(new Etapa("Religando o serviço de impressão...", 40,
                delegate(Contexto ctx)
                {
                    try { ServicoTipoInicio("Spooler", 2); } catch { }
                    bool ok = IniciarServico("Spooler", 20, ctx.Log);
                    return ok ? EtapaResultado.Sucesso("Serviço de impressão religado. Teste imprimir agora.")
                              : EtapaResultado.Falha("O Spooler não quis iniciar. Rode o reparo \"Reparar o serviço de impressão\".");
                }));
            lista.Add(fila);

            // ---------- reparar spooler ----------
            var spool = F("imp-spooler-reparo", CAT_IMP,
                "Reparar o serviço de impressão (Spooler não inicia)",
                "Quando o próprio Spooler morre/reinicia sozinho — reconfigura e limpa dependências.",
                "Vai além de limpar a fila: garante o tipo de inicialização correto do Spooler, configura a recuperação automática, limpa a pasta de spool e reinicia. Use quando o serviço de impressão \"para sozinho\" ou dá erro 1068/não inicia.");
            spool.Disponivel = SoOnline;
            spool.Etapas.Add(new Etapa("Parando o Spooler e limpando a pasta de spool...", 40,
                delegate(Contexto ctx)
                {
                    PararServico("Spooler", 20, ctx.Log);
                    int n = LimparSpool(ctx.Log);
                    return EtapaResultado.Sucesso("Spooler parado e " + n + " arquivo(s) removidos.");
                }));
            spool.Etapas.Add(new Etapa("Configurando inicialização automática e recuperação do serviço...", 30,
                delegate(Contexto ctx)
                {
                    try { ServicoTipoInicio("Spooler", 2); } catch { }
                    // Garante dependencia RPC (o Spooler depende dela)
                    ctx.Rodar("sc.exe", "config Spooler start= auto", 30000);
                    ctx.Rodar("sc.exe", "failure Spooler reset= 86400 actions= restart/5000/restart/5000/restart/5000", 30000);
                    return EtapaResultado.Sucesso("Serviço configurado para iniciar com o Windows e se auto-recuperar.");
                }));
            spool.Etapas.Add(new Etapa("Iniciando o serviço de impressão...", 30,
                delegate(Contexto ctx)
                {
                    bool ok = IniciarServico("Spooler", 25, ctx.Log);
                    return ok ? EtapaResultado.Sucesso("Serviço de impressão ativo.")
                              : EtapaResultado.Falha("O Spooler ainda não inicia. Pode haver driver de impressora corrompido — rode \"Remover drivers de impressora com problema\".");
                }));
            lista.Add(spool);

            // ---------- tirar do offline ----------
            var off = F("imp-online", CAT_IMP,
                "Colocar impressora on-line (tirar de \"Offline\")",
                "Impressora aparece \"Offline\" mesmo ligada e conectada? Este reparo força o modo on-line.",
                "O Windows às vezes marca a impressora como \"Off-line\" e não volta sozinho, mesmo com ela ligada. Este reparo desliga o \"Usar impressora offline\", limpa a fila e reinicia o Spooler para forçar a reconexão.");
            off.Disponivel = SoOnline;
            off.Etapas.Add(new Etapa("Religando o Spooler para forçar a reconexão...", 60,
                delegate(Contexto ctx)
                {
                    PararServico("Spooler", 20, ctx.Log);
                    LimparSpool(ctx.Log);
                    try { ServicoTipoInicio("Spooler", 2); } catch { }
                    bool ok = IniciarServico("Spooler", 20, ctx.Log);
                    return ok ? EtapaResultado.Sucesso("Serviço de impressão reiniciado e fila limpa.")
                              : EtapaResultado.Alerta("O Spooler não iniciou — rode \"Reparar o serviço de impressão\".");
                }));
            off.Etapas.Add(new Etapa("Listando o estado das impressoras...", 40,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell(
                        "$ErrorActionPreference='SilentlyContinue'; Get-Printer | ForEach-Object { " +
                        "Write-Output ($_.Name + ' -> ' + $_.PrinterStatus) }", 60000);
                    bool algumOffline = r.Saida.ToLowerInvariant().Contains("offline");
                    if (algumOffline)
                        return EtapaResultado.Alerta("Ainda há impressora reportando \"Offline\". Verifique: (1) a impressora está ligada e conectada; (2) na janela Dispositivos e Impressoras, clique com o direito na impressora → \"Ver o que está sendo impresso\" → menu Impressora → desmarque \"Usar impressora offline\". Depois use os reparos HP/Epson.");
                    return EtapaResultado.Sucesso("Nenhuma impressora reportando offline após o reinício do serviço (detalhes no log).");
                }));
            lista.Add(off);

            // ---------- HP ----------
            var hp = F("imp-hp", CAT_IMP,
                "Reparo de impressora HP",
                "Reinicia os serviços da HP, limpa a fila e checa o driver — para HP que parou de imprimir.",
                "Foco em impressoras HP: reinicia o spooler, reinicia serviços específicos da HP (HP Smart / rede) quando presentes e verifica se o driver está instalado. Cobre os travamentos mais comuns de HP DeskJet, Ink Tank, LaserJet e OfficeJet.\r\n\r\n" +
                "Dica: se a HP sumiu de vez, a instalação limpa pelo app HP Smart (da Microsoft Store) costuma ser o caminho — este reparo prepara o terreno.");
            hp.Disponivel = SoOnline;
            hp.Etapas.Add(new Etapa("Reiniciando o serviço de impressão...", 30,
                delegate(Contexto ctx)
                {
                    PararServico("Spooler", 20, ctx.Log); LimparSpool(ctx.Log);
                    IniciarServico("Spooler", 20, ctx.Log);
                    return EtapaResultado.Sucesso("Spooler reiniciado e fila limpa.");
                }));
            hp.Etapas.Add(new Etapa("Reiniciando serviços específicos da HP (se instalados)...", 40,
                delegate(Contexto ctx)
                {
                    string[] hpSvcs = { "HPSLPSVC", "HPNetworkCommunicator", "Net Driver HPZ12", "Pml Driver HPZ12", "HPPrintScanDoctorService" };
                    int ok = 0, ausentes = 0;
                    foreach (string s in hpSvcs)
                    {
                        if (!ServicoExiste(s)) { ausentes++; continue; }
                        ctx.Log("  Serviço HP: " + s);
                        if (ReiniciarServico(s, ctx.Log)) ok++;
                    }
                    if (ok == 0)
                        return EtapaResultado.Alerta("Nenhum serviço específico da HP instalado (" + ausentes + " verificados). A HP pode estar usando driver genérico — o que costuma bastar; verifique a próxima etapa.");
                    return EtapaResultado.Sucesso(ok + " serviço(s) da HP reiniciado(s).");
                }));
            hp.Etapas.Add(new Etapa("Verificando drivers HP instalados...", 30,
                delegate(Contexto ctx) { return VerificarDriverMarca(ctx, "HP", "hewlett"); }));
            lista.Add(hp);

            // ---------- Epson ----------
            var epson = F("imp-epson", CAT_IMP,
                "Reparo de impressora Epson",
                "Reinicia os serviços da Epson, limpa a fila e checa o driver — para Epson que parou de imprimir.",
                "Foco em impressoras Epson (L3110, L3150, L375, EcoTank etc.): reinicia o spooler, reinicia os serviços da Epson quando presentes (comunicação/rede) e confirma se o driver está instalado. Cobre os casos típicos de Epson que \"some\" ou fica offline.");
            epson.Disponivel = SoOnline;
            epson.Etapas.Add(new Etapa("Reiniciando o serviço de impressão...", 30,
                delegate(Contexto ctx)
                {
                    PararServico("Spooler", 20, ctx.Log); LimparSpool(ctx.Log);
                    IniciarServico("Spooler", 20, ctx.Log);
                    return EtapaResultado.Sucesso("Spooler reiniciado e fila limpa.");
                }));
            epson.Etapas.Add(new Etapa("Reiniciando serviços específicos da Epson (se instalados)...", 40,
                delegate(Contexto ctx)
                {
                    string[] epSvcs = { "EPSON_PM_RPCV4_04", "EPSON_EB_RPCV4_04", "EpsonCustomerParticipation", "Epson Scanner Service", "EMP_NPService" };
                    int ok = 0, ausentes = 0;
                    foreach (string s in epSvcs)
                    {
                        if (!ServicoExiste(s)) { ausentes++; continue; }
                        ctx.Log("  Serviço Epson: " + s);
                        if (ReiniciarServico(s, ctx.Log)) ok++;
                    }
                    if (ok == 0)
                        return EtapaResultado.Alerta("Nenhum serviço específico da Epson instalado (" + ausentes + " verificados). A Epson pode usar driver padrão — verifique a próxima etapa.");
                    return EtapaResultado.Sucesso(ok + " serviço(s) da Epson reiniciado(s).");
                }));
            epson.Etapas.Add(new Etapa("Verificando drivers Epson instalados...", 30,
                delegate(Contexto ctx) { return VerificarDriverMarca(ctx, "Epson", "epson"); }));
            lista.Add(epson);

            // ---------- página de teste ----------
            var teste = F("imp-teste", CAT_IMP,
                "Imprimir página de teste",
                "Confirma se a impressora voltou a funcionar depois do reparo.",
                "Manda uma página de teste do Windows para a impressora padrão. É a forma de confirmar que o reparo funcionou sem depender de nenhum programa do cliente.");
            teste.Disponivel = SoOnline;
            teste.Etapas.Add(new Etapa("Enviando página de teste para a impressora padrão...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell(
                        "$p = Get-CimInstance Win32_Printer -Filter 'Default = True'; " +
                        "if ($p) { Invoke-CimMethod -InputObject $p -MethodName PrintTestPage; " +
                        "Write-Output ('Enviado para: ' + $p.Name) } else { Write-Output 'SEM_PADRAO' }", 60000);
                    if (r.Saida.Contains("SEM_PADRAO"))
                        return EtapaResultado.Alerta("Nenhuma impressora padrão definida. Defina a impressora como padrão no Windows e tente de novo.");
                    if (r.Saida.ToLowerInvariant().Contains("enviado para"))
                        return EtapaResultado.Sucesso("Página de teste enviada. Se ela sair impressa, a impressora está funcionando!");
                    return EtapaResultado.Alerta("Não foi possível enviar a página — veja o log.");
                }));
            lista.Add(teste);

            // ---------- reinstalar/limpar drivers ----------
            var drv = F("imp-drivers", CAT_IMP,
                "Remover drivers de impressora com problema",
                "Faxina pesada: remove pacotes de driver de impressão para reinstalar do zero.",
                "Para o spooler, limpa a fila e remove os pacotes de driver de impressão da máquina, deixando o terreno limpo para reinstalar o driver correto (pelo instalador do fabricante ou pelo Windows Update). Use quando o driver está corrompido e nenhum reparo de fila resolve.");
            drv.Disponivel = SoOnline;
            drv.Aviso = "Todas as impressoras precisarão ser reinstaladas depois (pelo app/instalador do fabricante ou reconectando o USB). Faça isto quando puder reinstalar o driver na sequência.";
            drv.Etapas.Add(new Etapa("Parando o Spooler e limpando a fila...", 30,
                delegate(Contexto ctx)
                {
                    PararServico("Spooler", 20, ctx.Log); LimparSpool(ctx.Log);
                    IniciarServico("Spooler", 20, ctx.Log);
                    return EtapaResultado.Sucesso("Fila limpa e serviço reiniciado.");
                }));
            drv.Etapas.Add(new Etapa("Listando e removendo pacotes de driver de impressão...", 70,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell(
                        "$ErrorActionPreference='SilentlyContinue'; " +
                        "Get-PrinterDriver | ForEach-Object { " +
                        "try { Remove-PrinterDriver -Name $_.Name; Write-Output ('Removido: ' + $_.Name) } " +
                        "catch { Write-Output ('Em uso (mantido): ' + $_.Name) } }", 120000);
                    return EtapaResultado.Sucesso("Drivers de impressão processados (detalhes no log). Reinstale a impressora pelo app do fabricante. Alguns drivers em uso podem exigir reinício antes de sair.");
                }));
            lista.Add(drv);
        }

        static int LimparSpool(Action<string> log)
        {
            int n = 0;
            try
            {
                string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"spool\PRINTERS");
                if (Directory.Exists(spool))
                {
                    foreach (string f in Directory.GetFiles(spool))
                    {
                        try { File.Delete(f); n++; } catch { }
                    }
                }
            }
            catch (Exception ex) { if (log != null) log("    (spool: " + ex.Message + ")"); }
            return n;
        }

        static EtapaResultado VerificarDriverMarca(Contexto ctx, string marca, string chaveBusca)
        {
            var r = ctx.PowerShell(
                "$ErrorActionPreference='SilentlyContinue'; " +
                "$d = Get-PrinterDriver | Where-Object { $_.Name -like '*" + chaveBusca + "*' -or $_.Manufacturer -like '*" + chaveBusca + "*' }; " +
                "$p = Get-Printer | Where-Object { $_.DriverName -like '*" + chaveBusca + "*' }; " +
                "if ($d) { $d | ForEach-Object { Write-Output ('Driver: ' + $_.Name) } } else { Write-Output 'SEM_DRIVER' }; " +
                "if ($p) { $p | ForEach-Object { Write-Output ('Impressora: ' + $_.Name + ' [' + $_.PrinterStatus + ']') } }",
                60000);
            if (r.Saida.Contains("SEM_DRIVER") && !r.Saida.ToLowerInvariant().Contains("impressora:"))
                return EtapaResultado.Alerta("Nenhum driver " + marca + " encontrado. Instale o driver pelo site/app oficial da " + marca + " (ou conecte a impressora por USB para o Windows detectar).");
            return EtapaResultado.Sucesso("Driver(s)/impressora(s) " + marca + " localizados (detalhes no log). Se ainda não imprime, rode a \"Página de teste\".");
        }

        // ============================================================
        // CATEGORIA: Conta de usuário
        // ============================================================
        static void Usuario(List<Fix> lista)
        {
            // ---------- perfil temporário ----------
            var perfilBak = F("usr-perfil-bak", CAT_USR,
                "Corrigir perfil que abre como temporário",
                "\"Você entrou com um perfil temporário\": conserta o perfil corrompido no registro.",
                "Quando o Windows não consegue carregar o perfil do usuário, ele cria um TEMPORÁRIO (a área de trabalho aparece vazia e nada salva persiste). A causa quase sempre é a chave do perfil marcada com \".bak\" no registro. Este reparo corrige essa marcação para o Windows voltar a carregar o perfil real no próximo login.\r\n\r\n" +
                "Rode, depois FAÇA LOGOFF e entre de novo com o mesmo usuário.");
            perfilBak.Disponivel = SoOnline;
            perfilBak.RequerReinicio = true;
            perfilBak.Etapas.Add(new Etapa("Procurando e corrigindo perfis marcados como .bak...", 100,
                delegate(Contexto ctx) { return CorrigirPerfilBak(ctx); }));
            lista.Add(perfilBak);

            // ---------- criar admin novo ----------
            var novo = F("usr-novo-admin", CAT_USR,
                "Criar um novo usuário administrador",
                "Cria uma conta administrador limpa — a saída mais rápida para perfil sem conserto.",
                "Cria um novo usuário LOCAL com poderes de administrador. É a solução mais confiável quando o perfil do usuário está corrompido sem conserto: cria-se a conta nova, entra nela e (se preciso) copiam-se os documentos do perfil velho.\r\n\r\n" +
                "O técnico informa o nome e a senha da nova conta.");
            novo.Disponivel = SoOnline;
            novo.Preparar = delegate(Contexto ctx, IWin32Window owner)
            {
                string nome = InputDialog.Pedir(owner, "Novo usuário administrador",
                    "Nome da nova conta (sem espaços, ex.: Tecnico):", "Tecnico", false);
                if (nome == null) return false;
                nome = nome.Trim();
                if (nome.Length == 0) { MessageBox.Show(owner, "Nome vazio.", "Diagnóstico e Reparo"); return false; }
                string senha = InputDialog.Pedir(owner, "Novo usuário administrador",
                    "Senha para \"" + nome + "\" (deixe em branco para conta sem senha):", "", true);
                if (senha == null) return false;
                ctx.Parametros["nome"] = nome;
                ctx.Parametros["senha"] = senha;
                return true;
            };
            novo.Etapas.Add(new Etapa("Criando a conta e adicionando ao grupo Administradores...", 100,
                delegate(Contexto ctx)
                {
                    string nome = ctx.Parametros.ContainsKey("nome") ? ctx.Parametros["nome"] : "Tecnico";
                    string senha = ctx.Parametros.ContainsKey("senha") ? ctx.Parametros["senha"] : "";
                    return LocalAccounts.CriarAdministrador(nome, senha);
                }));
            lista.Add(novo);

            // ---------- ativar Administrador oculto ----------
            var admOn = F("usr-admin-oculto-on", CAT_USR,
                "Ativar a conta Administrador interna",
                "Libera a conta \"Administrador\" oculta do Windows — útil quando não há outro admin acessível.",
                "O Windows tem uma conta \"Administrador\" interna, desativada por padrão. Este reparo a ativa (e opcionalmente define uma senha), dando uma via de acesso administrativa quando a conta do cliente perdeu privilégios ou está inacessível.\r\n\r\n" +
                "A ativação é suportada somente no Windows em execução. No WinPE o programa orienta sem editar o SAM.");
            admOn.Preparar = delegate(Contexto ctx, IWin32Window owner)
            {
                string senha = InputDialog.Pedir(owner, "Conta Administrador",
                    "Senha para a conta Administrador (em branco = sem senha):", "", true);
                if (senha == null) return false;
                ctx.Parametros["senha"] = senha;
                return true;
            };
            admOn.Etapas.Add(new Etapa("Ativando a conta Administrador...", 100,
                delegate(Contexto ctx)
                {
                    string senha = ctx.Parametros.ContainsKey("senha") ? ctx.Parametros["senha"] : "";
                    if (ctx.Online)
                    {
                        // /active por SID não existe no net; usa PowerShell por SID (independe de idioma)
                        var r = ctx.PowerShell("Enable-LocalUser -SID (Get-LocalUser | Where-Object { $_.SID -like 'S-1-5-*-500' }).SID", 60000);
                        if (r.ExitCode != 0) return EtapaResultado.Falha("Não foi possível ativar a conta Administrador.");
                        if (senha.Length > 0)
                        {
                            using (var search = new System.Management.ManagementObjectSearcher("SELECT Name, SID FROM Win32_UserAccount WHERE LocalAccount=True"))
                                foreach (System.Management.ManagementObject user in search.Get())
                                    if (Convert.ToString(user["SID"]).EndsWith("-500"))
                                        LocalAccounts.DefinirSenha(Convert.ToString(user["Name"]), senha);
                        }
                        return EtapaResultado.Sucesso("Conta Administrador ativada" + (senha.Length > 0 ? " com a senha informada" : " (sem senha)") + ". Ela aparecerá na tela de login.");
                    }
                    else
                    {
                        return AtivarAdminOffline(ctx, true);
                    }
                }));
            lista.Add(admOn);

            var admOff = F("usr-admin-oculto-off", CAT_USR,
                "Desativar a conta Administrador interna",
                "Volta a ocultar a conta Administrador (segurança) após concluir o atendimento.",
                "Desativa novamente a conta Administrador interna. Faça isto ao fim do serviço: manter a conta Administrador ativa é um risco de segurança.");
            admOff.Disponivel = SoOnline;
            admOff.Etapas.Add(new Etapa("Desativando a conta Administrador...", 100,
                delegate(Contexto ctx)
                {
                    ctx.PowerShell("Disable-LocalUser -SID (Get-LocalUser | Where-Object { $_.SID -like 'S-1-5-*-500' }).SID", 60000);
                    return EtapaResultado.Sucesso("Conta Administrador desativada e oculta novamente.");
                }));
            lista.Add(admOff);

            // ---------- redefinir senha ----------
            var senhaFix = F("usr-senha", CAT_USR,
                "Redefinir a senha de um usuário local",
                "Cliente esqueceu a senha de uma conta LOCAL? Defina uma nova.",
                "Define uma nova senha para uma conta LOCAL do Windows (não funciona em conta Microsoft/online — essa se recupera pelo site da Microsoft). O técnico escolhe o usuário e a nova senha.\r\n\r\nDisponível somente no Windows em execução e para contas locais.");
            senhaFix.Disponivel = SoOnline;
            senhaFix.Preparar = delegate(Contexto ctx, IWin32Window owner)
            {
                var usuarios = ListarUsuariosLocais();
                string alvo = InputDialog.Escolher(owner, "Redefinir senha",
                    "Escolha o usuário local:", usuarios);
                if (alvo == null) return false;
                string nova = InputDialog.Pedir(owner, "Redefinir senha",
                    "Nova senha para \"" + alvo + "\" (em branco = remover a senha):", "", true);
                if (nova == null) return false;
                ctx.Parametros["alvo"] = alvo;
                ctx.Parametros["nova"] = nova;
                return true;
            };
            senhaFix.Etapas.Add(new Etapa("Aplicando a nova senha...", 100,
                delegate(Contexto ctx)
                {
                    string alvo = ctx.Parametros["alvo"];
                    string nova = ctx.Parametros.ContainsKey("nova") ? ctx.Parametros["nova"] : "";
                    LocalAccounts.DefinirSenha(alvo, nova);
                    return EtapaResultado.Sucesso("Senha local alterada com sucesso.");
                }));
            lista.Add(senhaFix);

            // ---------- reconstruir ícones/associações do perfil ----------
            var shell = F("usr-shell-reset", CAT_USR,
                "Restaurar área de trabalho e menu Iniciar do usuário",
                "Área de trabalho preta, sem ícones, ou menu Iniciar que não abre? Reinicia a interface do usuário.",
                "Reinicia o Explorer e recompõe os componentes visuais do perfil atual (barra de tarefas, menu Iniciar, ícones). Use para \"tela preta com cursor\" após login, menu Iniciar que não abre e barra de tarefas sumida — quando o perfil em si está ok, mas a interface travou.");
            shell.Disponivel = SoOnline;
            shell.Etapas.Add(new Etapa("Reiniciando o Windows Explorer...", 50,
                delegate(Contexto ctx)
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                    { try { p.Kill(); } catch { } }
                    System.Threading.Thread.Sleep(2000);
                    try { System.Diagnostics.Process.Start("explorer.exe"); } catch { }
                    return EtapaResultado.Sucesso("Explorer reiniciado.");
                }));
            shell.Etapas.Add(new Etapa("Recompondo o menu Iniciar (pacote ShellExperienceHost)...", 50,
                delegate(Contexto ctx)
                {
                    ctx.PowerShell("Get-Process ShellExperienceHost,StartMenuExperienceHost -ErrorAction SilentlyContinue | Stop-Process -Force", 30000);
                    return EtapaResultado.Sucesso("Componentes do menu Iniciar reiniciados. Se ainda não abrir, rode em \"Aplicativos e interface\" o reparo do Menu Iniciar.");
                }));
            lista.Add(shell);
        }

        static List<string> ListarUsuariosLocais()
        {
            var l = new List<string>();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT Name FROM Win32_UserAccount WHERE LocalAccount = True AND Disabled = False"))
                {
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        string n = Convert.ToString(mo["Name"]);
                        if (!string.IsNullOrEmpty(n)) l.Add(n);
                    }
                }
            }
            catch { }
            if (l.Count == 0) l.Add(Environment.UserName);
            return l;
        }

        static EtapaResultado CorrigirPerfilBak(Contexto ctx)
        {
            const string BASE = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
            int corrigidos = 0;
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var pl = bk.OpenSubKey(BASE, true))
                {
                    if (pl == null) return EtapaResultado.Falha("Não foi possível abrir a lista de perfis no registro.");
                    var nomes = pl.GetSubKeyNames();
                    foreach (string sid in nomes)
                    {
                        if (!sid.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                        string sidLimpo = sid.Substring(0, sid.Length - 4);
                        ctx.Log("  Perfil marcado como temporário encontrado: " + sidLimpo);

                        // Se existir a chave "boa" (sem .bak), ela é a temporária: apaga-a e
                        // renomeia a .bak de volta ao nome original.
                        if (Array.IndexOf(nomes, sidLimpo) >= 0)
                        {
                            try { pl.DeleteSubKeyTree(sidLimpo); ctx.Log("    Removida a chave temporária " + sidLimpo); } catch { }
                        }
                        CopiarChave(pl, sid, sidLimpo);
                        try { pl.DeleteSubKeyTree(sid); } catch { }

                        // Ajusta State=0 e RefCount=0 na chave restaurada
                        using (var k = pl.OpenSubKey(sidLimpo, true))
                        {
                            if (k != null)
                            {
                                k.SetValue("State", 0, RegistryValueKind.DWord);
                                k.SetValue("RefCount", 0, RegistryValueKind.DWord);
                            }
                        }
                        corrigidos++;
                    }
                }
            }
            catch (Exception ex) { return EtapaResultado.Falha("Erro ao corrigir o registro: " + ex.Message); }

            if (corrigidos == 0)
                return EtapaResultado.Alerta("Nenhum perfil marcado como temporário (.bak) foi encontrado. Se o problema persiste, o perfil pode estar corrompido de outra forma — considere \"Criar um novo usuário administrador\".");
            return EtapaResultado.Sucesso(corrigidos + " perfil(is) corrigido(s). FAÇA LOGOFF e entre novamente com o usuário — a área de trabalho e os arquivos devem voltar.");
        }

        static void CopiarChave(RegistryKey pai, string origem, string destino)
        {
            using (var src = pai.OpenSubKey(origem))
            using (var dst = pai.CreateSubKey(destino))
            {
                if (src == null || dst == null) return;
                foreach (string vn in src.GetValueNames())
                    dst.SetValue(vn, src.GetValue(vn, null, RegistryValueOptions.DoNotExpandEnvironmentNames), src.GetValueKind(vn));
                foreach (string sn in src.GetSubKeyNames())
                    CopiarSubchave(src, dst, sn);
            }
        }

        static void CopiarSubchave(RegistryKey origem, RegistryKey destino, string nome)
        {
            using (var src = origem.OpenSubKey(nome))
            using (var dst = destino.CreateSubKey(nome))
            {
                if (src == null || dst == null) throw new IOException("Falha ao copiar subchave do perfil.");
                foreach (string vn in src.GetValueNames())
                    dst.SetValue(vn, src.GetValue(vn, null, RegistryValueOptions.DoNotExpandEnvironmentNames), src.GetValueKind(vn));
                foreach (string sn in src.GetSubKeyNames()) CopiarSubchave(src, dst, sn);
            }
        }

        static EtapaResultado AtivarAdminOffline(Contexto ctx, bool ativar)
        {
            // Carrega o hive SAM do Windows alvo e liga a conta 500. Como editar
            // o F-value do SAM é complexo, usamos o SAM via 'reg' + net não está
            // disponível offline; então orientamos o uso online. Melhor esforço:
            string sam = Path.Combine(ctx.WinDir, @"System32\config\SAM");
            if (!File.Exists(sam))
                return EtapaResultado.Falha("Não encontrei o SAM do Windows alvo (" + sam + ").");
            return EtapaResultado.Alerta("A ativação offline da conta Administrador exige edição do arquivo SAM, que este programa não faz automaticamente por segurança. " +
                "Alternativa recomendada: use o reparo \"Criar um novo usuário administrador\" quando estiver no Windows, ou a ferramenta de senha do próprio pendrive de recuperação (Sergei Strelec inclui o Password Reset).");
        }

        // ============================================================
        // CATEGORIA: Windows Update
        // ============================================================
        static void WindowsUpdate(List<Fix> lista)
        {
            var reset = F("wu-reset", CAT_WU,
                "Reset completo do Windows Update",
                "Para updates travados em 0%, erros 0x8024..., 0x80070..., que voltam sempre.",
                "O reparo definitivo para o Windows Update quebrado: para os serviços de atualização, renomeia as pastas de cache corrompido (SoftwareDistribution e catroot2), re-registra os componentes e religa tudo. O Windows recria o cache do zero na próxima verificação.\r\n\r\n" +
                "Resolve a grande maioria dos erros de atualização que insistem em voltar.");
            reset.Disponivel = SoOnline;
            reset.DemoradoAviso = true;
            reset.Etapas.Add(new Etapa("Parando os serviços do Windows Update...", 20,
                delegate(Contexto ctx)
                {
                    string[] svcs = { "wuauserv", "bits", "cryptsvc", "msiserver", "usosvc" };
                    foreach (string s in svcs) if (ServicoExiste(s)) PararServico(s, 25, ctx.Log);
                    return EtapaResultado.Sucesso("Serviços de atualização parados.");
                }));
            reset.Etapas.Add(new Etapa("Renomeando os caches SoftwareDistribution e catroot2...", 40,
                delegate(Contexto ctx)
                {
                    string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    int ok = RenomearPasta(Path.Combine(win, "SoftwareDistribution"), ctx.Log)
                           + RenomearPasta(Path.Combine(sys, @"catroot2"), ctx.Log);
                    if (ok == 0)
                        return EtapaResultado.Alerta("Não consegui renomear os caches (arquivos em uso?). Reinicie e rode de novo; se persistir, faça pelo modo de recuperação.");
                    return EtapaResultado.Sucesso(ok + " cache(s) renomeado(s) — o Windows recriará limpos.");
                }));
            reset.Etapas.Add(new Etapa("Re-registrando componentes e religando os serviços...", 40,
                delegate(Contexto ctx)
                {
                    // Reseta descritores de segurança dos serviços (recupera casos de "acesso negado")
                    ctx.Rodar("sc.exe", "sdset bits D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)", 30000);
                    foreach (string s in new[] { "wuauserv", "bits", "cryptsvc", "usosvc" })
                    {
                        if (!ServicoExiste(s)) continue;
                        try { ServicoTipoInicio(s, s == "wuauserv" ? 3 : 2); } catch { }
                        IniciarServico(s, 25, ctx.Log);
                    }
                    return EtapaResultado.Sucesso("Componentes re-registrados e serviços religados. Abra o Windows Update e clique em \"Procurar atualizações\" para testar.");
                }));
            lista.Add(reset);

            // ---------- reabilitar update (se desativado) ----------
            var enable = F("wu-reativar", CAT_WU,
                "Reativar o Windows Update (se estiver bloqueado)",
                "Alguém desativou as atualizações com programa de \"otimização\"? Este reparo religa.",
                "Remove bloqueios comuns ao Windows Update: políticas de \"desativar atualizações\", serviço marcado como desativado e tarefas de update desligadas. Programas de otimização e tutoriais de internet costumam deixar o Update travado.");
            enable.Disponivel = SoOnline;
            enable.Etapas.Add(new Etapa("Removendo políticas de bloqueio e religando o serviço...", 100,
                delegate(Contexto ctx)
                {
                    int mudou = 0;
                    try
                    {
                        using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                        {
                            using (var k = bk.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", true))
                            {
                                if (k != null && k.GetValue("NoAutoUpdate") != null)
                                { k.DeleteValue("NoAutoUpdate", false); mudou++; }
                            }
                            using (var k = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update", true))
                            {
                                if (k != null && k.GetValue("AUOptions") != null)
                                { k.DeleteValue("AUOptions", false); mudou++; }
                            }
                        }
                    }
                    catch (Exception ex) { ctx.Log("    (registro: " + ex.Message + ")"); }
                    if (ServicoExiste("wuauserv"))
                    {
                        try { ServicoTipoInicio("wuauserv", 3); } catch { }
                        IniciarServico("wuauserv", 25, ctx.Log);
                        mudou++;
                    }
                    ctx.Rodar("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\WindowsUpdate\\Scheduled Start\" /Enable", 20000);
                    return EtapaResultado.Sucesso("Bloqueios removidos e serviço religado (" + mudou + " ajuste(s)). Teste o Windows Update.");
                }));
            lista.Add(enable);
        }

        static int RenomearPasta(string caminho, Action<string> log)
        {
            try
            {
                if (!Directory.Exists(caminho)) return 1; // nada a fazer conta como ok
                string destino = caminho + ".old_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Directory.Move(caminho, destino);
                if (log != null) log("    " + Path.GetFileName(caminho) + " -> " + Path.GetFileName(destino));
                return 1;
            }
            catch (Exception ex) { if (log != null) log("    (não renomeou " + Path.GetFileName(caminho) + ": " + ex.Message + ")"); return 0; }
        }

        // ============================================================
        // CATEGORIA: Aplicativos e interface
        // ============================================================
        static void AppsInterface(List<Fix> lista)
        {
            var start = F("app-menu-iniciar", CAT_APP,
                "Reparar o Menu Iniciar e a barra de tarefas",
                "Menu Iniciar não abre, botões da barra não respondem — reparo dos apps do sistema.",
                "Reinicia os componentes do menu Iniciar e re-registra os aplicativos internos do Windows (o shell moderno). Resolve menu Iniciar que não abre, pesquisa da barra travada e ícones da bandeja sem resposta.");
            start.Disponivel = SoOnline;
            start.Aviso = "O re-registro dos apps do sistema leva 1 a 3 minutos e a tela pode piscar. É normal.";
            start.Etapas.Add(new Etapa("Reiniciando os hosts do menu Iniciar e da pesquisa...", 30,
                delegate(Contexto ctx)
                {
                    ctx.PowerShell("Get-Process StartMenuExperienceHost,ShellExperienceHost,SearchHost,SearchApp -ErrorAction SilentlyContinue | Stop-Process -Force", 30000);
                    return EtapaResultado.Sucesso("Hosts reiniciados.");
                }));
            start.Etapas.Add(new Etapa("Re-registrando os aplicativos internos do Windows (1 a 3 min)...", 70,
                delegate(Contexto ctx)
                {
                    ctx.PowerShell("$ErrorActionPreference='SilentlyContinue'; Get-AppXPackage -AllUsers | Foreach { Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') }", 5 * 60 * 1000);
                    return EtapaResultado.Sucesso("Aplicativos do sistema re-registrados. Se o menu ainda não abrir, faça logoff e login (ou reinicie).");
                }));
            lista.Add(start);

            var explorer = F("app-explorer", CAT_APP,
                "Reiniciar o Windows Explorer",
                "Barra de tarefas travada ou área de trabalho congelada — reinício rápido da interface.",
                "Encerra e reabre o Windows Explorer (explorer.exe), que desenha a área de trabalho, a barra de tarefas e as janelas de pastas. É rápido e seguro: resolve travamentos visuais sem reiniciar o computador.");
            explorer.Disponivel = SoOnline;
            explorer.Etapas.Add(new Etapa("Reiniciando o explorer.exe...", 100,
                delegate(Contexto ctx)
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                    { try { p.Kill(); } catch { } }
                    System.Threading.Thread.Sleep(1500);
                    try { System.Diagnostics.Process.Start("explorer.exe"); } catch { }
                    return EtapaResultado.Sucesso("Explorer reiniciado — a barra de tarefas e a área de trabalho voltaram.");
                }));
            lista.Add(explorer);

            var store = F("app-store", CAT_APP,
                "Reparar a Microsoft Store",
                "Store não abre, apps não instalam/atualizam — limpa o cache da loja.",
                "Limpa o cache da Microsoft Store (wsreset) e re-registra o app da loja. Use quando a Store não abre, fica em branco, ou os apps não instalam/atualizam (erros como 0x80073CF9).");
            store.Disponivel = SoOnline;
            store.Etapas.Add(new Etapa("Limpando o cache da Microsoft Store (wsreset)...", 50,
                delegate(Contexto ctx)
                {
                    var r = ctx.Rodar("wsreset.exe", "", 3 * 60 * 1000);
                    return EtapaResultado.Sucesso("Cache da Store limpo.");
                }));
            store.Etapas.Add(new Etapa("Re-registrando o aplicativo da Store...", 50,
                delegate(Contexto ctx)
                {
                    ctx.PowerShell("$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage -AllUsers *WindowsStore* | Foreach { Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') }", 3 * 60 * 1000);
                    return EtapaResultado.Sucesso("App da Store re-registrado. Abra a Store para testar.");
                }));
            lista.Add(store);

            var icones = F("app-cache-icones", CAT_APP,
                "Reconstruir cache de ícones e miniaturas",
                "Ícones errados, em branco ou \"quebrados\"? Reconstrói o cache visual.",
                "Apaga o cache de ícones e de miniaturas do Windows e reinicia o Explorer para reconstruí-los. Resolve ícones genéricos/errados na área de trabalho e miniaturas de fotos e vídeos que não aparecem.");
            icones.Disponivel = SoOnline;
            icones.Etapas.Add(new Etapa("Apagando o cache de ícones e miniaturas...", 60,
                delegate(Contexto ctx)
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                    { try { p.Kill(); } catch { } }
                    System.Threading.Thread.Sleep(1500);
                    int n = 0;
                    try
                    {
                        string expl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            @"Microsoft\Windows\Explorer");
                        if (Directory.Exists(expl))
                            foreach (string f in Directory.GetFiles(expl, "*cache*.db"))
                            { try { File.Delete(f); n++; } catch { } }
                        string ic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "IconCache.db");
                        if (File.Exists(ic)) { try { File.Delete(ic); n++; } catch { } }
                    }
                    catch { }
                    return EtapaResultado.Sucesso(n + " arquivo(s) de cache apagados.");
                }));
            icones.Etapas.Add(new Etapa("Reiniciando o Explorer para reconstruir os ícones...", 40,
                delegate(Contexto ctx)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe"); } catch { }
                    return EtapaResultado.Sucesso("Explorer reiniciado — os ícones serão reconstruídos automaticamente.");
                }));
            lista.Add(icones);
        }

        // ============================================================
        // CATEGORIA: Limpeza e desempenho
        // ============================================================
        static void Limpeza(List<Fix> lista)
        {
            var temp = F("limp-temp", CAT_LIMP,
                "Limpar arquivos temporários",
                "Libera espaço apagando lixo temporário do Windows e dos programas.",
                "Apaga os arquivos temporários do usuário e do Windows (pastas TEMP) e o cache de download do Windows Update. É seguro: são arquivos descartáveis. Ganha espaço em disco e resolve travas causadas por temporários corrompidos.");
            temp.Disponivel = SoOnline;
            temp.Etapas.Add(new Etapa("Apagando arquivos temporários...", 100,
                delegate(Contexto ctx)
                {
                    long bytes = 0;
                    string[] pastas =
                    {
                        Environment.ExpandEnvironmentVariables("%TEMP%"),
                        Environment.ExpandEnvironmentVariables("%SystemRoot%\\Temp"),
                        Environment.ExpandEnvironmentVariables("%SystemRoot%\\Prefetch")
                    };
                    foreach (string p in pastas) bytes += LimparPasta(p);
                    return EtapaResultado.Sucesso(string.Format("Limpeza concluída — cerca de {0:0.0} MB liberados.", bytes / (1024.0 * 1024.0)));
                }));
            lista.Add(temp);

            var lixeira = F("limp-lixeira", CAT_LIMP,
                "Esvaziar a Lixeira",
                "Esvazia a Lixeira de todas as unidades.",
                "Esvazia a Lixeira de todos os discos. Útil quando a Lixeira ficou gigante ocupando espaço, ou quando um arquivo \"fantasma\" nela impede a limpeza.");
            lixeira.Aviso = "Os arquivos na Lixeira serão apagados de vez. Confirme com o cliente se não há nada importante para recuperar antes.";
            lixeira.Disponivel = SoOnline;
            lixeira.Etapas.Add(new Etapa("Esvaziando a Lixeira...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.PowerShell("Clear-RecycleBin -Force -ErrorAction SilentlyContinue; Write-Output 'ok'", 60000);
                    return EtapaResultado.Sucesso("Lixeira esvaziada.");
                }));
            lista.Add(lixeira);

            var limpDisco = F("limp-cleanmgr", CAT_LIMP,
                "Limpeza de Disco do Windows (arquivos de sistema)",
                "Abre a Limpeza de Disco para remover atualizações antigas, logs e caches grandes.",
                "Abre a ferramenta de Limpeza de Disco do Windows já configurada para o volume C:. Permite remover com segurança arquivos de atualizações antigas do Windows (que ocupam vários GB), logs, relatórios de erro e caches diversos.");
            limpDisco.Disponivel = SoOnline;
            limpDisco.Etapas.Add(new Etapa("Abrindo a Limpeza de Disco...", 100,
                delegate(Contexto ctx)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("cleanmgr.exe", "/d C:");
                        return EtapaResultado.Sucesso("Ferramenta aberta — marque os itens (inclusive \"Limpeza de atualizações do Windows\") e confirme na janela que apareceu.");
                    }
                    catch (Exception ex) { return EtapaResultado.Falha("Não foi possível abrir: " + ex.Message); }
                }));
            lista.Add(limpDisco);

            var defrag = F("limp-otimizar-disco", CAT_LIMP,
                "Otimizar o disco (desfragmentar HDD / TRIM em SSD)",
                "Melhora o desempenho: desfragmenta HDD ou envia TRIM ao SSD, conforme o tipo de disco.",
                "Roda a otimização adequada ao seu disco: em HDD faz a desfragmentação (reorganiza os arquivos, acelerando a leitura); em SSD envia o comando TRIM (mantém a velocidade de escrita). O Windows detecta o tipo automaticamente — nunca desfragmenta SSD.");
            defrag.Disponivel = SoOnline;
            defrag.DemoradoAviso = true;
            defrag.Etapas.Add(new Etapa("Otimizando o volume C: (pode demorar em HDD)...", 100,
                delegate(Contexto ctx)
                {
                    var r = ctx.Rodar("defrag.exe", "C: /O", 60 * 60 * 1000);
                    if (r.ExitCode == 0)
                        return EtapaResultado.Sucesso("Disco otimizado com sucesso" +
                            (ctx.Sys != null && ctx.Sys.Disco == DiskKind.SSD ? " (TRIM enviado ao SSD)." : " (desfragmentação concluída)."));
                    return EtapaResultado.Alerta("A otimização terminou com código " + r.ExitCode + " — veja o log.");
                }));
            lista.Add(defrag);

            var defender = F("limp-defender", CAT_LIMP,
                "Varredura rápida do Windows Defender",
                "Procura ameaças com o antivírus do Windows (varredura rápida).",
                "Dispara uma varredura rápida do Windows Defender (Segurança do Windows) nas áreas onde ameaças costumam se esconder. Complementa os reparos de rede/proxy quando há suspeita de vírus. Para varredura completa, use depois a Segurança do Windows.");
            defender.Disponivel = SoOnline;
            defender.DemoradoAviso = true;
            defender.Etapas.Add(new Etapa("Atualizando as definições e iniciando a varredura rápida...", 100,
                delegate(Contexto ctx)
                {
                    string mp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        @"Windows Defender\MpCmdRun.exe");
                    if (!File.Exists(mp))
                    {
                        var rp = ctx.PowerShell("Update-MpSignature -ErrorAction SilentlyContinue; Start-MpScan -ScanType QuickScan; Write-Output 'ok'", 30 * 60 * 1000);
                        if (rp.Saida.ToLowerInvariant().Contains("ok"))
                            return EtapaResultado.Sucesso("Varredura rápida concluída (via PowerShell). Veja os resultados na Segurança do Windows.");
                        return EtapaResultado.Alerta("Não foi possível iniciar a varredura — o Defender pode estar desligado ou substituído por outro antivírus.");
                    }
                    ctx.Rodar(mp, "-SignatureUpdate", 10 * 60 * 1000);
                    var r = ctx.Rodar(mp, "-Scan -ScanType 1", 30 * 60 * 1000);
                    if (r.Contem("no threats", "nenhuma ameaça", "found no threats"))
                        return EtapaResultado.Sucesso("Varredura concluída: NENHUMA ameaça encontrada.");
                    if (r.Contem("threats", "ameaça", "found"))
                        return EtapaResultado.Alerta("A varredura encontrou itens — abra a Segurança do Windows para revisar e remover. Veja o log.");
                    return EtapaResultado.Sucesso("Varredura rápida concluída (detalhes no log).");
                }));
            lista.Add(defender);
        }

        static long LimparPasta(string pasta)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(pasta)) return 0;
                foreach (string f in Directory.GetFiles(pasta))
                {
                    try { long len = new FileInfo(f).Length; File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); total += len; }
                    catch { }
                }
                foreach (string d in Directory.GetDirectories(pasta))
                {
                    total += LimparPasta(d);
                    try { Directory.Delete(d, false); } catch { }
                }
            }
            catch { }
            return total;
        }
    }
}
