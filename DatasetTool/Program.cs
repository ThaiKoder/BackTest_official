using DatasetTool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace DatasetTool;

internal static class Program
{
    // Ajuste selon ton besoin
    private const string InputDir = "data/json";
    private const bool IncludeSubDirectories = false;

    // Pour éviter de saturer le disque/CPU. Mets 1 si tu veux strictement séquentiel.
    private static readonly int MaxParallel = Math.Max(1, Environment.ProcessorCount / 2);


    // Lecture des fichiers JSON (pour comparer avec la version binaire)
    //static void Main()
    //{
    //    string jsonDir = Path.Combine(AppContext.BaseDirectory, "data", "json");
    //    var swTotal = Stopwatch.StartNew();
    //    Console.WriteLine($"Lecture des fichiers JSON dans {jsonDir}...");
    //    foreach (var jsonPath in Directory.EnumerateFiles(jsonDir, "*.json"))
    //    {
    //        //Console.WriteLine($"Lecture : {Path.GetFileName(jsonPath)}");
    //        var swFile = Stopwatch.StartNew();
    //        string json = File.ReadAllText(jsonPath);
    //        swFile.Stop();
    //        //Console.WriteLine($"===> {swFile.ElapsedMilliseconds} ms");
    //    }
    //    swTotal.Stop();
    //    Console.WriteLine($"===> Temps total : {swTotal.Elapsed}");
    //}


    //Lecture des fichiers binaires
    //static void Main()
    //{
    //    string binDir = Path.Combine(AppContext.BaseDirectory, "data", "bin");

    //    var swTotal = Stopwatch.StartNew();
    //    Console.WriteLine($"===> Lecture des fichiers binaires dans {binDir}...");

    //    const int CandleSize = 44;
    //    const int BufferCandles = 16_384;
    //    byte[] sharedBuffer = new byte[CandleSize * BufferCandles];

    //    int gen0Before = GC.CollectionCount(0);
    //    int gen1Before = GC.CollectionCount(1);
    //    int gen2Before = GC.CollectionCount(2);

    //    foreach (var binPath in Directory.EnumerateFiles(binDir, "*.bin"))
    //    {


    //        Binary.ReadAllFast(
    //            binPath,
    //            sharedBuffer,
    //            (ts, o, h, l, c, v) =>
    //            {
    //                if (v > 1_000_000)
    //                {
    //                    // logique de test
    //                }
    //            });
    //    }

    //    int gen0After = GC.CollectionCount(0);
    //    int gen1After = GC.CollectionCount(1);
    //    int gen2After = GC.CollectionCount(2);

    //    Console.WriteLine($"GC Gen0: {gen0After - gen0Before}");
    //    Console.WriteLine($"GC Gen1: {gen1After - gen1Before}");
    //    Console.WriteLine($"GC Gen2: {gen2After - gen2Before}");

    //    swTotal.Stop();
    //    Console.WriteLine($"===> Temps total : {swTotal.Elapsed}");


    //}


    //static void Main()
    //{
    //    string binDir = Path.Combine(AppContext.BaseDirectory, "data", "bin", "glbx-mdp3-20100606-20100612.ohlcv-1m.bin");
    //    Binary.ReadFile(binDir);
    //}



    //LECTURE OK
    static async Task<int> ReadJC(string[] args)
    {
        bool enableConvert = false; // Passe à true pour convertire JSON->BIN
        int limitFiles = -1; // Limite lecture fichier bin
        int limitCandles = -1; // Limite lecture bougies par fichier


        //string inputDir = Path.Combine(AppContext.BaseDirectory, "data", "json");

        //if (!Directory.Exists(inputDir))
        //{
        //    Console.WriteLine($"Dossier introuvable: {inputDir}");
        //    return 1;
        //}


        //var jsonFiles = Directory.GetFiles(inputDir, "*.json");

        //if (jsonFiles.Length == 0)
        //{
        //    Console.WriteLine("Aucun JSON trouvé.");
        //    return 0;
        //}

        //string binDir = Path.Combine(inputDir, "..", "bin");
        //Directory.CreateDirectory(binDir);
        //if (enableConvert)
        //{
        //    Console.WriteLine("=== CONVERSION JSON -> BIN ===");
        //    Console.WriteLine($"Conversion de {jsonFiles.Length} fichier(s)...");

        //    foreach (var json in jsonFiles)
        //    {
        //        string binPath = Path.Combine(
        //            binDir,
        //            Path.GetFileNameWithoutExtension(json) + ".bin");

        //        Console.WriteLine($"→ {Path.GetFileName(json)}");

        //        await JsonToBinaryConverter.ConvertJsonAsync(json, binPath);
        //    }

        //    Console.WriteLine();
        //}
        Console.WriteLine("=== TEST LECTURE ===");
        string binDir = Path.Combine("data", "bin");
        var binFiles = Directory.GetFiles(binDir, "*.bin");

        if (binFiles.Length == 0)
        {
            Console.WriteLine("Aucun .bin trouvé.");
            return 0;
        }

        long globalCount = 0;
        long localCount = 0;
        int totalCandles = 0;
        var swGlobal = Stopwatch.StartNew();
        int C1 = 0, C2 = 0, C3 = 0, C4 = 0;

        var contractsByDate = new Dictionary<long, List<byte>>();
        long currentDate = -1;
        List<byte>? currentList = null;

        const int CONTINUATION_LIMIT = 60*23*5; 
        const int BREAK_LIMIT = 60*23*30;


        int nbDates = 0;
        long lastDate = -1;

        //Parcourir les fichiers binaires
        foreach (var binPath in binFiles)
        {

            if (globalCount >= limitFiles && limitFiles != -1) break;

            //Console.WriteLine($"=== {Path.GetFileName(binPath)} ===");

            using var bin = new Binary(binPath);



            // Accès rapide à toute les bougies d'un fichier
            bin.ReadAllFast((ts, o, h, l, c, v, symbol) =>
            {
                if (localCount >= limitCandles && limitCandles != -1) return;

                long ms = ts / 1_000_000L;
                var dto = DateTimeOffset.FromUnixTimeMilliseconds(ms);

                if (ts != currentDate)
                {
                    currentDate = ts;
                    currentList = new List<byte>();
                    contractsByDate[ts] = currentList;
                }

                currentList!.Add(symbol);
                //Console.WriteLine(
                //    $"{dto:O} | {symbol} | O={o} H={h} L={l} C={c} V={v}");

                localCount++;
                totalCandles++;

            });

            //Console.WriteLine();
            //Console.WriteLine();
            localCount = 0;
            globalCount++;

        }

        foreach (var kvp in contractsByDate)
        {
            long date = kvp.Key;
            List<byte> contracts = kvp.Value;
       
            if (date != lastDate)
            {
                nbDates++;
                lastDate = date;
            }
            //Console.Write($"{DateTime.UnixEpoch.AddTicks(date / 100)} => [");
            //Console.Write(string.Join(",", contracts));
            //Console.WriteLine("]");
        }
        Console.WriteLine($"Total dates uniques: {nbDates}");

        // Compteurs de continuation par contrat
        var contCounts = new Dictionary<byte, int>();

        // Quand un contrat a atteint le seuil, on ne le recompte plus
        var locked = new HashSet<byte>();

        int breakCount = 0;

        // Pour détecter "pas de contrat détecté vs date précédente"
        HashSet<byte>? prevUnique = null;

        // Si ton dictionnaire n'est pas garanti trié, trie les dates :
        var dates = new List<long>(contractsByDate.Keys);
        dates.Sort();

        int[] continuation = new int[5];
        int[] coupure = new int[5];

        Console.WriteLine("=== ANALYSE CONTINUITY / BREAK ===");
        byte lastContContract = 0;
        bool hasLastContContract = false;

        bool hasShowBreak = false;
        bool[] hasShowContinuation = new bool[5];
        foreach (var date in dates)
        {
            List<byte> contracts = contractsByDate[date];

            // ===== CONTINUATION :
            foreach (var c in contracts)
            {
                if (continuation[c] < CONTINUATION_LIMIT)
                {

                    continuation[c]++;

                } 

                if (!hasShowContinuation[c] && continuation[c] >= CONTINUATION_LIMIT)
                {
                    Console.WriteLine($"[CONT] {DateTime.UnixEpoch.AddTicks(date / 100)} => {c} contrats");
                    hasShowContinuation[c] = true;
                }

            }

            // Affichage brut (comme ton exemple)
            //Console.Write($"{date} => [");
            //Console.Write(string.Join(",", contracts));
            //Console.WriteLine("]");

            // Unique set pour la date courante (pour comparer avec la précédente)
            var currUnique = new HashSet<byte>(contracts);

            // ===== COUPURE : aucun contrat commun avec la date précédente =====
            if (prevUnique != null)
            {
                bool hasContinuation = false;

                for (byte cc = 1; cc < 5; cc++)
                {
                    if (!prevUnique.Contains(cc))
                    {
                        if (coupure[cc] >= BREAK_LIMIT && hasShowContinuation[cc])
                        {
                            Console.WriteLine($"[BREAK] {DateTime.UnixEpoch.AddTicks(date / 100)} Contrat {cc} atteint {coupure[cc]} coupures. Stop counting for this contract.");

                            coupure[cc] = 0; // reset
                            continuation[cc] = 0; // reset
                            hasShowContinuation[cc] = false;
                            hasShowBreak = true;
                        } 

                        if (coupure[cc] < BREAK_LIMIT)
                        {
                            coupure[cc]++;
                        }

                    }
                }

 
            }

            // ===== CONTINUATION : compter occurrences par symbole (byte) =====
            // Ici on compte chaque occurrence de 'contracts' (pas seulement unique).
            //foreach (var c in contracts)
            //{
            //    if (locked.Contains(c))
            //        continue;

            //    if (!contCounts.TryGetValue(c, out int v))
            //        v = 0;

            //    v++;
            //    contCounts[c] = v;

            //    if (v >= CONTINUATION_LIMIT)
            //    {
            //        Console.WriteLine($"[CONT] {DateTime.UnixEpoch.AddTicks(date / 100)} Contrat {c} atteint {v} occurrences. Stop counting for this contract.");
            //        locked.Add(c); // ne plus compter ce contrat
            //    }
            //}

            prevUnique = currUnique;
        }

        Console.WriteLine($"{continuation[1]}");
        Console.WriteLine($"{DateTime.UnixEpoch.AddTicks(1375236060000000000 / 100)} -  {DateTime.UnixEpoch.AddTicks(1275861600000000000 / 100)}");

        swGlobal.Stop();
        Console.WriteLine();
        Console.WriteLine("=================================");
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"Total Fichier: {globalCount}");
        Console.WriteLine($"Total Bougies: {totalCandles}");
        Console.WriteLine();
        Console.WriteLine($"Temps total lecture: {swGlobal.ElapsedMilliseconds} ms");

        return 0;
    }








    //static int Main()
    //{
    //    string inputDir = Path.Combine(AppContext.BaseDirectory, "data", "json");
    //    string binDir = Path.Combine(inputDir, "..", "bin");
    //    var binFiles = Directory.GetFiles(binDir, "*.bin");

    //    if (binFiles.Length == 0)
    //    {
    //        Console.WriteLine("Aucun .bin trouvé.");
    //        return 0;
    //    }

    //    long globalCount = 0;
    //    var sw = Stopwatch.StartNew();

    //    foreach (var binPath in binFiles)
    //    {
    //        using var bin = new Binary(binPath);

    //        bin.ReadAllFast((ts, o, h, l, c, v, s) =>
    //        {
    //            globalCount++;
    //        });
    //    }

    //    sw.Stop();

    //    Console.WriteLine($"TOTAL GLOBAL: {globalCount}");
    //    Console.WriteLine($"Temps lecture: {sw.ElapsedMilliseconds} ms");

    //    return 0;
    //}


    //static void Main()
    //{
    //    string binDir = Path.Combine(AppContext.BaseDirectory, "data", "bin");

    //    var swTotal = Stopwatch.StartNew();
    //    long totalCandles = 0;

    //    Console.WriteLine($"Lecture des fichiers bin dans : {binDir}");

    //    foreach (var binPath in Directory.EnumerateFiles(binDir, "*.bin"))
    //    {
    //        //Console.WriteLine($"--- {Path.GetFileName(binPath)}");

    //        using var bin = new Binary(binPath);

    //        //Console.WriteLine($"Bougies : {bin.CandleCount:N0}");
    //        totalCandles += bin.CandleCount;

    //        // 🔹 Accès rapide à la dernière bougie du fichier
    //        long lastIndex = bin.CandleCount - 1;

    //        bin.GetCandle(lastIndex,
    //            out var ts, out var o, out var h, out var l, out var c, out var v);

    //        //Console.WriteLine($"Last TS={ts} O={o} H={h} L={l} C={c} V={v}");

    //        // 🔹 Lecture complète (backtest / indicateurs)
    //        bin.ReadAllFast((ts, o, h, l, c, v) =>
    //        {
    //            // logique trading / indicateurs ici
    //        });
    //    }

    //    swTotal.Stop();
    //    Console.WriteLine();
    //    Console.WriteLine($"Total bougies lues : {totalCandles:N0}");
    //    Console.WriteLine($"Temps total        : {swTotal.Elapsed}");
    //}












    //Conversion des fichiers JSON en BIN
    static async Task<int> WriteJC(string[] args)
    {
        string inputDir = args.Length > 0 ? args[0] : InputDir;
        bool recursive = args.Length > 1
            ? bool.TryParse(args[1], out var r) && r
            : IncludeSubDirectories;

        if (!Directory.Exists(inputDir))
        {
            Console.Error.WriteLine($"Dossier introuvable: {inputDir}");
            return 1;
        }

        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        var jsonFiles = Directory.EnumerateFiles(inputDir, "*.json", opt).ToList();

        if (jsonFiles.Count == 0)
        {
            Console.WriteLine("Aucun fichier .json trouvé.");
            return 0;
        }

        Console.WriteLine($"Trouvé {jsonFiles.Count} fichier(s) .json dans {Path.GetFullPath(inputDir)}");
        Console.WriteLine($"Parallelisme: {MaxParallel}");

        // Bin à côté de inputDir (…/inputDir/../bin)
        var binDir = Path.GetFullPath(Path.Combine(inputDir, "..", "bin"));
        Directory.CreateDirectory(binDir);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Annulation demandée (Ctrl+C)...");
        };

        long ok = 0, skipped = 0, failed = 0;
        var swAll = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            jsonFiles,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = cts.Token },
            async (jsonPath, ct) =>
            {
                var binPath = Path.Combine(
                    binDir,
                    Path.GetFileNameWithoutExtension(jsonPath) + ".bin"
                );

                try
                {
                    // Skip si déjà converti
                    if (File.Exists(binPath))
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }

                    var fi = new FileInfo(jsonPath);
                    if (fi.Length == 0)
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }

                    var sw = Stopwatch.StartNew();
                    await JsonToBinaryConverter.ConvertJsonAsync(jsonPath, binPath, ct);
                    sw.Stop();

                    Interlocked.Increment(ref ok);
                    Console.WriteLine($"OK  {Path.GetFileName(jsonPath)} -> {Path.GetFileName(binPath)}  ({sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (OperationCanceledException)
                {
                    // Annulation => ne pas compter comme failed
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Console.Error.WriteLine($"FAIL {jsonPath}\n     {ex.GetType().Name}: {ex.Message}");
                    try { if (File.Exists(binPath)) File.Delete(binPath); } catch { /* ignore */ }
                }
            });

        swAll.Stop();

        Console.WriteLine();
        Console.WriteLine($"Terminé en {swAll.Elapsed.TotalSeconds:F1}s | OK={ok} | Skip={skipped} | Fail={failed}");

        return failed == 0 ? 0 : 2;
    }



    public static async Task<int> Main(string[] args)
    {
        //return await WriteJC(args);
        return await ReadJC(args);
    }


}