using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DiagnosticoReparo
{
    public class BenchResult
    {
        public long RamTestadaMB;
        public int RamErros;
        public double SeqWriteMBs;
        public double SeqReadMBs;
        public double Rand4kIops;
        public double CpuMops;
        public double RamMBs;
        public double NotaCpu, NotaRam, NotaDisco, NotaFinal;
        public string Veredito = "";
    }

    // ---------------------------------------------------------------
    // Teste rápido final: memória, disco e processador, com nota de 1
    // a 10. Mesma metodologia validada no projeto Otimizador.
    // ---------------------------------------------------------------
    public static class Bench
    {
        public static BenchResult Executar(SystemInfo sys, Action<string> log, Action<int> pct)
        {
            var r = new BenchResult();
            bool ehHdd = sys != null && sys.Disco != DiskKind.SSD;

            log("=== Teste rápido de memória RAM ===");
            pct(5);
            TestarRam(r, log);
            pct(30);

            log("");
            log("=== Teste de processador (CPU) ===");
            r.CpuMops = BenchCpu(log);
            pct(45);
            r.RamMBs = BenchRam(log);
            pct(55);

            log("");
            log("=== Teste de armazenamento (disco) ===");
            BenchDisco(r, log);
            pct(95);

            Pontuar(r);
            pct(100);
            return r;
        }

        static void TestarRam(BenchResult r, Action<string> log)
        {
            const int BLOCO = 32 * 1024 * 1024;
            int alvoBlocos = 16; // ~512 MB
            var blocos = new List<byte[]>();
            try
            {
                for (int i = 0; i < alvoBlocos; i++)
                {
                    try { blocos.Add(new byte[BLOCO]); }
                    catch (OutOfMemoryException) { break; }
                }
                if (blocos.Count == 0) { log("Sem memória livre suficiente para o teste."); return; }
                r.RamTestadaMB = (long)blocos.Count * BLOCO / (1024 * 1024);
                log(string.Format("Testando {0} MB de RAM (padrões + verificação)...", r.RamTestadaMB));

                int erros = 0;
                for (int passada = 0; passada < 2; passada++)
                {
                    byte padraoBase = passada == 0 ? (byte)0xA5 : (byte)0x5A;
                    for (int b = 0; b < blocos.Count; b++)
                    {
                        byte[] blk = blocos[b];
                        byte v = (byte)(padraoBase ^ b);
                        for (int i = 0; i < blk.Length; i++) blk[i] = (byte)(v + (i & 0x3F));
                    }
                    for (int b = 0; b < blocos.Count && erros < 100; b++)
                    {
                        byte[] blk = blocos[b];
                        byte v = (byte)(padraoBase ^ b);
                        for (int i = 0; i < blk.Length; i++)
                            if (blk[i] != (byte)(v + (i & 0x3F))) { erros++; if (erros >= 100) break; }
                    }
                }
                r.RamErros = erros;
                log(erros == 0
                    ? string.Format("Resultado: OK — nenhum erro em {0} MB.", r.RamTestadaMB)
                    : string.Format("ATENÇÃO: {0} erro(s) de memória! Faça um teste completo (MemTest86) e/ou troque o módulo.", erros));
            }
            finally
            {
                blocos.Clear();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
        }

        static long _cpuSink;
        static double BenchCpu(Action<string> log)
        {
            int nThreads = Environment.ProcessorCount;
            log(string.Format("CPU: medindo em {0} thread(s)...", nThreads));
            long totalIters = 0;
            object trava = new object();
            var pronto = new ManualResetEvent(false);
            var threads = new List<Thread>();
            var sw = new Stopwatch();

            for (int t = 0; t < nThreads; t++)
            {
                var th = new Thread(delegate()
                {
                    pronto.WaitOne();
                    long iters = 0; long x = 12345; double d = 1.0001;
                    var swLocal = Stopwatch.StartNew();
                    while (swLocal.ElapsedMilliseconds < 800)
                    {
                        for (int i = 0; i < 10000; i++)
                        {
                            x = x * 6364136223846793005L + 1442695040888963407L;
                            d = d * 1.0000001 + 0.000001;
                            if ((x & 0xFF) == 0) d += 0.5;
                        }
                        iters += 10000;
                    }
                    Interlocked.Add(ref _cpuSink, x + (long)d);
                    lock (trava) { totalIters += iters; }
                });
                th.IsBackground = true; th.Priority = ThreadPriority.AboveNormal;
                threads.Add(th); th.Start();
            }
            sw.Start(); pronto.Set();
            foreach (var th in threads) th.Join();
            sw.Stop();
            double mops = totalIters * 3.0 / sw.Elapsed.TotalSeconds / 1e6;
            log(string.Format("CPU: {0:0} MOPS (todas as threads).", mops));
            return mops;
        }

        static double BenchRam(Action<string> log)
        {
            const int TAM = 64 * 1024 * 1024;
            byte[] src, dst;
            try { src = new byte[TAM]; dst = new byte[TAM]; }
            catch (OutOfMemoryException) { log("RAM: sem memória para o teste de banda."); return 1000; }
            new Random(1).NextBytes(src);
            Buffer.BlockCopy(src, 0, dst, 0, TAM);
            var sw = Stopwatch.StartNew();
            int reps = 12;
            for (int i = 0; i < reps; i++) Buffer.BlockCopy(src, 0, dst, 0, TAM);
            sw.Stop();
            double mbs = (double)TAM * reps * 2 / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
            log(string.Format("RAM: banda de cópia ~{0:0} MB/s.", mbs));
            return mbs;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFilePointerEx(IntPtr hFile, long liDistanceToMove, IntPtr lpNewFilePointer, uint dwMoveMethod);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        const uint GENERIC_READ = 0x80000000;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        const uint MEM_COMMIT_RESERVE = 0x3000;
        const uint PAGE_READWRITE = 0x04;
        const uint MEM_RELEASE = 0x8000;

        static void BenchDisco(BenchResult r, Action<string> log)
        {
            const int TAM_MB = 128;
            const int CHUNK = 1 << 20;
            string caminho = Path.Combine(Path.GetTempPath(), "diag_bench.tmp");
            try
            {
                log(string.Format("Disco: escrevendo arquivo de teste de {0} MB...", TAM_MB));
                byte[] chunk = new byte[CHUNK];
                new Random(2).NextBytes(chunk);
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write, FileShare.None,
                    CHUNK, FileOptions.WriteThrough))
                {
                    for (int i = 0; i < TAM_MB; i++) fs.Write(chunk, 0, CHUNK);
                    fs.Flush(true);
                }
                sw.Stop();
                r.SeqWriteMBs = TAM_MB / sw.Elapsed.TotalSeconds;
                log(string.Format("Disco: escrita sequencial ~{0:0} MB/s.", r.SeqWriteMBs));

                IntPtr h = CreateFileW(caminho, GENERIC_READ, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
                if (h == new IntPtr(-1)) { log("Disco: leitura sem cache indisponível; pulando."); return; }
                IntPtr buf = VirtualAlloc(IntPtr.Zero, (UIntPtr)CHUNK, MEM_COMMIT_RESERVE, PAGE_READWRITE);
                try
                {
                    sw.Restart();
                    uint lidos; long totalLido = 0;
                    SetFilePointerEx(h, 0, IntPtr.Zero, 0);
                    for (int i = 0; i < TAM_MB; i++)
                    {
                        if (!ReadFile(h, buf, CHUNK, out lidos, IntPtr.Zero) || lidos == 0) break;
                        totalLido += lidos;
                    }
                    sw.Stop();
                    if (totalLido > 0) r.SeqReadMBs = totalLido / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
                    log(string.Format("Disco: leitura sequencial ~{0:0} MB/s.", r.SeqReadMBs));

                    log("Disco: leitura aleatória 4K (aqui HDD e SSD se separam)...");
                    var rnd = new Random(3);
                    long tamArq = (long)TAM_MB * CHUNK;
                    int nLeituras = 192; int okCount = 0;
                    sw.Restart();
                    for (int i = 0; i < nLeituras; i++)
                    {
                        long off = ((long)(rnd.NextDouble() * (tamArq - 4096))) & ~4095L;
                        SetFilePointerEx(h, off, IntPtr.Zero, 0);
                        if (ReadFile(h, buf, 4096, out lidos, IntPtr.Zero) && lidos > 0) okCount++;
                    }
                    sw.Stop();
                    if (okCount > 0) r.Rand4kIops = okCount / sw.Elapsed.TotalSeconds;
                    log(string.Format("Disco: ~{0:0} operações 4K/s.", r.Rand4kIops));
                }
                finally
                {
                    if (buf != IntPtr.Zero) VirtualFree(buf, UIntPtr.Zero, MEM_RELEASE);
                    CloseHandle(h);
                }
            }
            catch (Exception ex) { log("Disco: teste incompleto (" + ex.Message + ")"); }
            finally { try { File.Delete(caminho); } catch { } }
        }

        static double Log2(double x) { return Math.Log(x) / Math.Log(2.0); }
        static double Clamp(double v) { return Math.Max(1.0, Math.Min(10.0, v)); }

        static void Pontuar(BenchResult r)
        {
            r.NotaCpu = Clamp(5.0 + 1.5 * Log2(Math.Max(1, r.CpuMops) / 1800.0));
            r.NotaRam = Clamp(5.0 + 1.5 * Log2(Math.Max(1, r.RamMBs) / 6000.0));
            double seqMedia = Math.Max(1, (r.SeqWriteMBs + r.SeqReadMBs) / 2.0);
            double notaSeq = 3.0 + 1.7 * Log2(seqMedia / 90.0);
            double notaRand = 2.0 + 1.2 * Log2(Math.Max(1, r.Rand4kIops) / 100.0);
            r.NotaDisco = Clamp(0.55 * Clamp(notaSeq) + 0.45 * Clamp(notaRand));
            r.NotaFinal = Clamp(0.35 * r.NotaCpu + 0.25 * r.NotaRam + 0.40 * r.NotaDisco);
            if (r.RamErros > 0) r.NotaFinal = Math.Min(r.NotaFinal, 2.0);

            if (r.RamErros > 0)
                r.Veredito = "ERROS DE MEMÓRIA — resolva isto antes de tudo (teste os módulos, limpe os contatos, substitua se preciso).";
            else if (r.NotaFinal < 3)
                r.Veredito = "Crítica: só dá conta de tarefas muito básicas. Upgrade de SSD e/ou RAM é fortemente recomendado.";
            else if (r.NotaFinal < 5)
                r.Veredito = "Limitada: navegação leve e documentos, com paciência. Um SSD (se ainda for HDD) transformaria esta máquina.";
            else if (r.NotaFinal < 6.5)
                r.Veredito = "Razoável: o uso do dia a dia flui bem.";
            else if (r.NotaFinal < 8)
                r.Veredito = "Boa: responde bem para produtividade e multitarefa moderada.";
            else
                r.Veredito = "Excelente: máquina rápida para qualquer uso comum.";
        }

        public static string Relatorio(BenchResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format("NOTA FINAL: {0:0.0} / 10", r.NotaFinal));
            sb.AppendLine(string.Format("  CPU: {0:0.0}  |  RAM: {1:0.0}  |  Disco: {2:0.0}", r.NotaCpu, r.NotaRam, r.NotaDisco));
            sb.AppendLine(r.Veredito);
            return sb.ToString();
        }
    }
}
