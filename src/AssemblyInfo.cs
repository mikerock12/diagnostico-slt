using System.Reflection;
using System.Runtime.InteropServices;

// Metadados do executavel. O csc.exe converte estes atributos no bloco
// VERSIONINFO do Win32 (o que aparece em Propriedades > Detalhes).
// Um binario SEM esses dados e um dos sinais de maior peso nos motores
// heuristicos do Windows Defender e do SmartScreen.
[assembly: AssemblyTitle("Diagnóstico e Reparo")]
[assembly: AssemblyDescription("Diagnóstico inteligente e reparo do Windows 10/11: arquivos de sistema, inicialização, rede, impressoras HP/Epson, contas de usuário e Windows Update. Funciona também em ambiente de recuperação (WinPE).")]
[assembly: AssemblyProduct("Diagnóstico e Reparo")]
[assembly: AssemblyCompany("Smells Like Tech Informática")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Maicon Nunes - Smells Like Tech Informática")]
[assembly: AssemblyTrademark("Smells Like Tech Informática - www.smellsliketech.com.br")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCulture("")]

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

[assembly: ComVisible(false)]
