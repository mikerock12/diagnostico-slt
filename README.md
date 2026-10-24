# Diagnóstico e Reparo — Windows 10/11

> Criado por Maicon Nunes, da Smells Like Tech Informática — www.smellsliketech.com.br

Programa desktop (WinForms, .NET Framework 4.8) que **diagnostica e conserta** máquinas
com Windows 10 ou 11 em poucos cliques. Cada botão roda um "prompt pré-programado" com
mensagem personalizada, texto explicativo do que faz e **interpretação inteligente do
resultado** (traduz a saída bruta do comando em "o que aconteceu").

Cobre problemas de: **arquivos do sistema**, **inicialização/boot**, **rede e internet**,
**impressoras (HP, Epson e outras)**, **conta de usuário**, **Windows Update**,
**aplicativos/interface** e **limpeza/desempenho**.

Funciona tanto com o Windows ligado quanto **fora dele**, a partir de um pendrive de
recuperação (WinPE — ex.: **Sergei Strelec**), para quando o Windows do cliente nem inicia.

Mesma identidade visual do **Otimizador Low Hardware**: abertura em fade in com o logo,
nome da empresa, nome e site fixos no rodapé.

## Como compilar

Não precisa de Visual Studio nem SDK: usa o `csc.exe` do .NET Framework 4.x, presente em
qualquer Windows 10/11.

```bat
build.bat
```

Saída: `bin\DiagnosticoReparo.exe` (pede elevação de administrador ao abrir — necessário
para mexer em serviços, registro HKLM, boot e rede). O `build.bat` usa `/codepage:65001`
porque os fontes são UTF-8 (acentos corretos no executável).

## Fluxo do programa

1. **Abertura**: splash de 5 s com fade in/out, logo, nome e site clicável.
2. **Detecção de ambiente**: identifica Windows 10 × 11 (pelo build ≥ 22000), UEFI × BIOS,
   tipo de disco e — o mais importante — se está rodando **dentro do Windows** ou num
   **WinPE de recuperação**. No WinPE, varre os discos e lista as instalações de Windows
   encontradas para o técnico escolher qual reparar.
3. **Pré-diagnóstico rápido** (roda sozinho na tela inicial): bateria de verificações leves
   que aponta prováveis problemas e **já sugere os reparos certos** em cartões coloridos por
   gravidade, cada um com botões "Corrigir: …".
4. **Reparos** (aba lateral por categoria, ou "Todos os comandos"): lista de comandos; ao
   clicar num item, a descrição completa aparece e o **botão de baixo "vira" aquele comando**
   ("Executar: …"). Um clique executa.
5. **Tela de status** (a cada reparo): porcentagem, explicação do que está sendo feito no
   momento, log ao vivo e, ao final, o resumo **"O que aconteceu"** (interpretação de cada
   etapa). Reparos que pedem reinício oferecem reiniciar ao final.
6. **Teste final e nota**: teste rápido de RAM, disco e CPU com **nota de 1 a 10** e veredito,
   e botão de reiniciar.

## Pré-diagnóstico — o que ele checa

**Windows ligado:** integridade da imagem (DISM CheckHealth), bit de "disco sujo",
SMART (falha iminente), espaço livre, rede (IP/gateway/ping/DNS), proxy indevido, serviço e
fila de impressão, impressora offline, perfil temporário, Windows Update bloqueado, reinício
pendente, data/hora absurda e desligamentos inesperados (Kernel-Power 41) nas últimas 72h.

**WinPE (recuperação):** instalações de Windows nos discos, presença do BCD/bootmgr,
atualização travada (`pending.xml`) e recomendação de verificar o disco antes de tudo.

## Catálogo de reparos (resumo)

| Categoria | Exemplos |
|---|---|
| **Sistema e arquivos** | Reparo completo DISM+SFC, SFC, DISM Check/Restore, chkdsk (agendar/`/f`), desfazer atualização travada (`RevertPendingActions`), ponto de restauração, teste de memória |
| **Inicialização (Boot)** | Reparo automático completo, `bcdboot`, `bootrec` clássico, `bootsect`, Modo de Segurança on/off, sair do loop de Reparo Automático |
| **Rede e Internet** | Reparo geral, flush DNS, renovar IP, Winsock, TCP/IP, remover proxy, DNS público/automático, reiniciar adaptadores e serviços, descoberta de rede, firewall, hora, redefinição total |
| **Impressoras** | Desentupir a fila, reparar Spooler, tirar do offline, reparo HP, reparo Epson, página de teste, remover drivers |
| **Conta de usuário** | Corrigir perfil temporário (.bak), criar admin novo, ativar/desativar Administrador interno, redefinir senha local, restaurar shell do usuário |
| **Windows Update** | Reset completo (SoftwareDistribution + catroot2), reativar Update bloqueado |
| **Aplicativos e interface** | Menu Iniciar, reiniciar Explorer, reparar Microsoft Store, reconstruir cache de ícones |
| **Limpeza e desempenho** | Temporários, Lixeira, Limpeza de Disco, otimizar disco (defrag/TRIM), varredura do Defender |

Cada reparo se adapta ao ambiente: os comandos ficam **online** (`sfc /scannow`,
`dism /Online`) ou **offline** (`sfc /offbootdir /offwindir`, `dism /Image:`, `bcdboot`,
`chkdsk /f` direto) conforme rode dentro do Windows ou no WinPE. Reparos que só fazem
sentido online (ou só no PE) aparecem apenas no ambiente certo.

## Uso no Sergei Strelec (WinPE)

Quando o Windows do cliente não inicia:

1. Dê boot pelo pendrive do Sergei Strelec (WinPE 10/11 com .NET Framework — versão "Full").
2. Copie/abra o `DiagnosticoReparo.exe` (pode rodar de um pendrive próprio).
3. O programa detecta o modo recuperação, lista as instalações de Windows nos discos e
   deixa você escolher qual reparar no seletor do topo.
4. Comece pelo **pré-diagnóstico** e pelo **"Reparo automático de inicialização"** ou
   **"Corrigir erros do disco agora (chkdsk /f)"**.

O Sergei Strelec **não** faz parte deste projeto — é apenas uma das plataformas onde o
programa roda.

## Estrutura do código

| Arquivo | Papel |
|---|---|
| `src/Program.cs` | Ponto de entrada: splash → janela principal |
| `src/SplashForm.cs` | Abertura com fade in/out, logo e site clicável |
| `src/SystemInfo.cs` | Detecção de ambiente (Win10/11, UEFI/BIOS, WinPE, instalações offline, disco) |
| `src/Runner.cs` | Execução de comandos ocultos com captura de saída (OEM/Unicode) e parse de % |
| `src/Fix.cs` | Modelo: `Fix`, `Etapa`, `Contexto` (online × alvo offline), resultado |
| `src/FixCatalog.cs` | Catálogo — Sistema, Boot e Rede (+ helpers de serviço e interpretação) |
| `src/FixCatalog2.cs` | Catálogo — Impressoras, Usuário, Update, Apps/Interface, Limpeza |
| `src/PreDiag.cs` | Pré-diagnóstico inteligente (online e WinPE) → achados + reparos sugeridos |
| `src/ExecForm.cs` | Tela de status: %, "o que está sendo feito", log ao vivo, "o que aconteceu" |
| `src/InputDialog.cs` | Coleta de entrada do técnico (usuário, senha, escolha em lista) |
| `src/Bench.cs` | Teste rápido de RAM, disco e CPU com nota 1–10 |
| `src/MainForm.cs` | Navegação: Diagnóstico → Reparos (lista de comandos) → Teste final |
| `src/AssemblyInfo.cs` | Metadados do executável (peso para SmartScreen/Defender) |
| `app.manifest` | Elevação de administrador + DPI aware |
| `assets/logo.png` | Logo embutido no .exe como recurso |

## Notas de segurança e antivírus

Seguindo as lições do projeto Otimizador: preferência por **APIs nativas** (registro,
`ServiceController`, WMI) em vez de encadear `cmd.exe`/`reg.exe`/`powershell` ocultos; sem
`-ExecutionPolicy Bypass`; metadados de versão preenchidos; manifest de administrador
explícito. Cada reparo com efeito colateral relevante **pede confirmação** antes de agir e
salva um **log** em `%ProgramData%\DiagnosticoReparo` (no WinPE, no ramdisk).

**O que o programa não faz:** não apaga dados do usuário sem confirmação, não desativa o
antivírus, não força ativação de Windows, não altera o SAM automaticamente.


## Portal e publicações

Demonstração, recursos e download da última versão: [Softwares SLT](https://softwares.smellsliketech.com.br/apps/diagnostico/).

Cada merge em `main` passa pelo CI e gera uma nova release estável com SHA-256. O portal consulta a release mais recente automaticamente (cache de até cinco minutos). Código-fonte é mantido neste repositório; o projeto central apenas o referencia como submódulo.

### Melhorias 1.1

Senhas são passadas diretamente às APIs do Windows, sem shells nem argumentos de processo; parâmetros são limpos após o reparo. PowerShell recebe comandos UTF-16 codificados para preservar aspas e Unicode. O log usa buffer sincronizado, sem ler controles de interface em outra thread. A cópia de subchaves de perfis preserva a hierarquia e valores expansíveis. Reparos de contas locais exigem Windows em execução.
