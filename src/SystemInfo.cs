using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiagnosticoReparo
{
    public enum DiskKind { HDD, SSD, Desconhecido }

    // Uma instalacao de Windows encontrada em disco (relevante no modo WinPE,
    // quando o Windows do cliente nao inicia e o reparo e feito "de fora").
    public class WindowsOffline
    {
        public string Drive;      // ex.: "C:" (letra que o PE deu ao disco)
        public string WinDir;     // ex.: "C:\Windows"
        public string Nome;       // ex.: "Windows 10 Pro" (lido do hive offline, se possivel)
        public int Build;

        public string Rotulo()
        {
            return string.Format("{0} — {1}", WinDir, string.IsNullOrEmpty(Nome) ? "Windows" : Nome);
        }
    }

    public class SystemInfo
    {
        // Ambiente
        public bool ModoPE = false;            // rodando dentro de WinPE (Sergei Strelec etc.)
        public bool FirmwareUEFI = false;
        public string WindowsNome = "Windows";
        public string WindowsVersao = "";
        public int WindowsBuild = 0;
        public bool EhWindows10 = false;
        public bool EhWindows11 = false;

        // Hardware (para o teste final e para o diagnostico)
        public string CpuName = "Desconhecido";
        public int CpuCores = 1;
        public int CpuThreads = 1;
        public int CpuMhz = 0;
        public int RamMB = 0;
        public DiskKind Disco = DiskKind.Desconhecido;
        public string DiscoModelo = "";
        public int DiscoGB = 0;
        public int DiscoIndice = -1;

        // Instalacoes de Windows encontradas nos discos (modo PE)
        public List<WindowsOffline> Instalacoes = new List<WindowsOffline>();

        public bool SistemaSuportado { get { return ModoPE || EhWindows10 || EhWindows11; } }
        public double RamGB { get { return Math.Round(RamMB / 1024.0, 1); } }

        [DllImport("kernel32.dll")]
        static extern bool GetFirmwareType(out uint firmwareType); // 1=BIOS, 2=UEFI

        public static SystemInfo Detectar()
        {
            var si = new SystemInfo();
            try { DetectarPE(si); } catch { }
            try { DetectarWindows(si); } catch { }
            try { DetectarFirmware(si); } catch { }
            try { DetectarCpu(si); } catch { }
            try { DetectarRam(si); } catch { }
            try { DetectarDisco(si); } catch { }
            if (si.ModoPE)
            {
                try { si.Instalacoes = ProcurarWindowsOffline(); } catch { }
            }
            return si;
        }

        // WinPE se identifica pela chave MiniNT (presente em qualquer PE,
        // incluindo os do Sergei Strelec e o WinRE).
        static void DetectarPE(SystemInfo si)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
                si.ModoPE = (k != null);
            // Reforco: unidade do sistema X: e o padrao do WinPE
            if (!si.ModoPE)
            {
                string sd = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
                if (sd.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) si.ModoPE = true;
            }
        }

        static void DetectarWindows(SystemInfo si)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (k == null) return;
                si.WindowsNome = Convert.ToString(k.GetValue("ProductName", "Windows"));
                object dv = k.GetValue("DisplayVersion");
                if (dv == null) dv = k.GetValue("ReleaseId");
                si.WindowsVersao = Convert.ToString(dv ?? "");
                int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber", "0")), out si.WindowsBuild);
                object major = k.GetValue("CurrentMajorVersionNumber");
                bool nucleo10 = major != null && Convert.ToInt32(major) == 10;
                // O registro do Windows 11 ainda informa ProductName "Windows 10 ...";
                // a distincao confiavel e o build (>= 22000 = Windows 11).
                si.EhWindows11 = nucleo10 && si.WindowsBuild >= 22000;
                si.EhWindows10 = nucleo10 && !si.EhWindows11
                    && si.WindowsNome.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0;
                if (si.EhWindows11)
                    si.WindowsNome = si.WindowsNome.Replace("Windows 10", "Windows 11");
            }
        }

        static void DetectarFirmware(SystemInfo si)
        {
            uint tipo;
            if (GetFirmwareType(out tipo)) si.FirmwareUEFI = (tipo == 2);
        }

        static void DetectarCpu(SystemInfo si)
        {
            using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    si.CpuName = Convert.ToString(mo["Name"]).Trim();
                    si.CpuCores = SafeInt(mo["NumberOfCores"], 1);
                    si.CpuThreads = SafeInt(mo["NumberOfLogicalProcessors"], 1);
                    si.CpuMhz = SafeInt(mo["MaxClockSpeed"], 0);
                    break;
                }
            }
        }

        static void DetectarRam(SystemInfo si)
        {
            using (var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    ulong bytes = Convert.ToUInt64(mo["TotalPhysicalMemory"]);
                    si.RamMB = (int)(bytes / (1024UL * 1024UL));
                    break;
                }
            }
        }

        static void DetectarDisco(SystemInfo si)
        {
            // No PE o "disco de sistema" (X:) e um ramdisk; o que interessa e o
            // disco fisico 0. No Windows normal, o disco que hospeda o C:.
            int indice = -1;
            if (!si.ModoPE)
            {
                try
                {
                    using (var s = new ManagementObjectSearcher(
                        "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='C:'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                    {
                        foreach (ManagementObject part in s.Get())
                        {
                            string devId = Convert.ToString(part["DeviceID"]); // "Disk #0, Partition #1"
                            int pos = devId.IndexOf("Disk #");
                            if (pos >= 0)
                            {
                                string resto = devId.Substring(pos + 6);
                                int fim = resto.IndexOf(',');
                                if (fim > 0) resto = resto.Substring(0, fim);
                                int idx;
                                if (int.TryParse(resto.Trim(), out idx)) indice = idx;
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
            if (indice < 0) indice = 0;
            si.DiscoIndice = indice;

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Index, Model, Size FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        int idx = SafeInt(mo["Index"], -1);
                        if (idx != indice) continue;
                        si.DiscoModelo = Convert.ToString(mo["Model"]).Trim();
                        try { si.DiscoGB = (int)(Convert.ToUInt64(mo["Size"]) / (1000UL * 1000UL * 1000UL)); }
                        catch { }
                        break;
                    }
                }
            }
            catch { }

            try
            {
                var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DeviceId, MediaType, SpindleSpeed FROM MSFT_PhysicalDisk")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceId"]);
                        if (devId != indice.ToString()) continue;
                        int mediaType = SafeInt(mo["MediaType"], 0); // 3=HDD, 4=SSD
                        if (mediaType == 4) si.Disco = DiskKind.SSD;
                        else if (mediaType == 3) si.Disco = DiskKind.HDD;
                        else
                        {
                            ulong spindle = ulong.MaxValue;
                            try { spindle = Convert.ToUInt64(mo["SpindleSpeed"]); } catch { }
                            if (spindle == 0) si.Disco = DiskKind.SSD;
                            else if (spindle > 0 && spindle < uint.MaxValue) si.Disco = DiskKind.HDD;
                        }
                        break;
                    }
                }
            }
            catch { }

            if (si.Disco == DiskKind.Desconhecido)
            {
                string m = (si.DiscoModelo ?? "").ToUpperInvariant();
                if (m.Contains("SSD") || m.Contains("NVME")) si.Disco = DiskKind.SSD;
                else if (m.Length > 0) si.Disco = DiskKind.HDD;
            }
        }

        // -------------------------------------------------------------
        // Modo PE: varre as unidades fixas atras de instalacoes de
        // Windows (pasta \Windows\System32 com ntoskrnl + hive SYSTEM).
        // -------------------------------------------------------------
        public static List<WindowsOffline> ProcurarWindowsOffline()
        {
            var achadas = new List<WindowsOffline>();
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.DriveType != DriveType.Fixed || !di.IsReady) continue;
                    string root = di.RootDirectory.FullName;          // "C:\"
                    if (root.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) continue; // ramdisk do PE
                    string winDir = Path.Combine(root, "Windows");
                    if (!File.Exists(Path.Combine(winDir, @"System32\ntoskrnl.exe"))) continue;
                    if (!File.Exists(Path.Combine(winDir, @"System32\config\SYSTEM"))) continue;

                    var w = new WindowsOffline();
                    w.Drive = root.Substring(0, 2);
                    w.WinDir = winDir;
                    LerVersaoOffline(w);
                    achadas.Add(w);
                }
                catch { }
            }
            return achadas;
        }

        // Le nome/build do Windows offline carregando temporariamente o hive
        // SOFTWARE. Melhor esforco: se falhar, fica so "Windows".
        static void LerVersaoOffline(WindowsOffline w)
        {
            const string TEMP_KEY = "_DIAG_SOFT_TMP";
            string hive = Path.Combine(w.WinDir, @"System32\config\SOFTWARE");
            if (!File.Exists(hive)) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("reg.exe",
                    string.Format("load HKLM\\{0} \"{1}\"", TEMP_KEY, hive));
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                using (var p = System.Diagnostics.Process.Start(psi)) { p.WaitForExit(15000); }

                using (var k = Registry.LocalMachine.OpenSubKey(TEMP_KEY + @"\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        w.Nome = Convert.ToString(k.GetValue("ProductName", "Windows"));
                        int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber", "0")), out w.Build);
                        if (w.Build >= 22000) w.Nome = w.Nome.Replace("Windows 10", "Windows 11");
                    }
                }
            }
            catch { }
            finally
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", "unload HKLM\\" + TEMP_KEY);
                    psi.CreateNoWindow = true; psi.UseShellExecute = false;
                    using (var p = System.Diagnostics.Process.Start(psi)) { p.WaitForExit(15000); }
                }
                catch { }
            }
        }

        static int SafeInt(object o, int padrao)
        {
            try { return o == null ? padrao : Convert.ToInt32(o); }
            catch { return padrao; }
        }

        public string Resumo()
        {
            if (ModoPE)
            {
                string inst = Instalacoes.Count == 0
                    ? "NENHUMA instalação de Windows encontrada nos discos!"
                    : string.Format("{0} instalação(ões) de Windows encontrada(s)", Instalacoes.Count);
                return string.Format(
                    "Ambiente: WinPE (modo de recuperação — ex.: Sergei Strelec)\r\nFirmware: {0}\r\n{1}\r\nCPU: {2}\r\nRAM: {3:0.0} GB\r\nDisco 0: {4} ({5} GB)",
                    FirmwareUEFI ? "UEFI" : "BIOS (Legado)", inst, CpuName, RamGB, DiscoModelo, DiscoGB);
            }
            return string.Format(
                "Sistema: {0} {1} (build {2})\r\nFirmware: {3}\r\nCPU: {4} ({5} núcleo(s)/{6} thread(s))\r\nRAM: {7:0.0} GB\r\nDisco: {8} {9} ({10} GB)",
                WindowsNome, WindowsVersao, WindowsBuild,
                FirmwareUEFI ? "UEFI" : "BIOS (Legado)",
                CpuName, CpuCores, CpuThreads, RamGB,
                Disco == DiskKind.SSD ? "SSD" : (Disco == DiskKind.HDD ? "HDD" : "?"),
                DiscoModelo, DiscoGB);
        }
    }
}
