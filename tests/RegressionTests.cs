using System;
using System.Collections.Generic;
using DiagnosticoReparo;
class RegressionTests {
 static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
 static int Main() {
  try {
   var lines=new List<string>();
   var r=Runner.Rodar("cmd.exe","/d /c echo output-test",10000,lines.Add,null);
   Check(r.ExitCode==0 && r.Saida.Contains("output-test") && lines.Count>0,"Output must be captured");
   r=Runner.Rodar("cmd.exe","/d /c exit 7",10000,null,null);
   Check(r.ExitCode==7,"Nonzero exit code must be preserved");
   r=Runner.Rodar("slt-executable-that-does-not-exist.exe","",10000,null,null);
   Check(r.NaoEncontrado && r.ExitCode!=0,"Missing executable must not be success");
   r=Runner.PowerShell("$s = 'single''quote \"double\"'; Write-Output $s; exit 0",15000,null,null);
   Check(r.ExitCode==0 && r.Saida.Contains("single'quote \"double\""),"PowerShell quote preservation");
   r=Runner.PowerShell("[Console]::WriteLine(('x' * 100000)); [Console]::Error.WriteLine(('e' * 100000)); exit 3",15000,null,null);
   Check(r.ExitCode==3 && !r.Timeout && r.Saida.Length>=200000,"Both output streams must drain");
   Console.WriteLine("OK: 5 runner regression checks; no account or repair actions executed.");return 0;
  } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
 }
}
