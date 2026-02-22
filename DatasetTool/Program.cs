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


    static void createIndex()
    {
        string jsonDir = Path.Combine("data", "json");
        string outBin = Path.Combine("data", "bin", "_index.bin");

        JsonToBinaryIndex.BuildRangesFromJsonFilenames_FastestPractical(jsonDir, outBin);
    }


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

        const int CONTINUATION_LIMIT = 60 * 23 * 5;
        const int BREAK_LIMIT = 30 ;


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

                    currentDate = ts;
                    currentList = new List<byte>();
                    contractsByDate[ts] = currentList;
                

                currentList!.Add(symbol);
                Console.WriteLine(
                    $"{dto:O} | {symbol} | O={o} H={h} L={l} C={c} V={v}");

                localCount++;
                totalCandles++;

            });

            //Console.WriteLine();
            //Console.WriteLine();
            localCount = 0;
            globalCount++;

        }

        //foreach (var kvp in contractsByDate)
        //{
        //    long date = kvp.Key;
        //    List<byte> contracts = kvp.Value;

        //    if (date != lastDate)
        //    {
        //        nbDates++;
        //        lastDate = date;
        //    }
        //    //Console.Write($"{DateTime.UnixEpoch.AddTicks(date / 100)} => [");
        //    //Console.Write(string.Join(",", contracts));
        //    //Console.WriteLine("]");
        //}
        //Console.WriteLine($"Total dates uniques: {nbDates}");

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
        bool hasLastContContract = false;

        bool hasShowBreak = false;
        bool[] hasShowContinuation = new bool[5];
        long[] firstContDate = new long[5];
        byte[] firstContContract = new byte[5];
        long[] lastContDate = new long[5];
        byte[] lastContContract = new byte[5];

        var quarterContracts = new Dictionary<int, string>
           {
               { 1, "NQH" },
               { 2, "NQM" },
               { 3, "NQU" },
               { 4, "NQZ" },

            };

        int missingCount = 0;
        int presentCount = 0;

        foreach (var kvp in contractsByDate)
        {
            byte myQuarter = JsonToBinaryConverter.GetQuarter(kvp.Key);
            bool isInContract = kvp.Value.Contains(myQuarter);

            if (!isInContract)
                missingCount++;
            else
                presentCount++;
        }

        Console.WriteLine($"Total de bougies: { (missingCount + presentCount)}");
        Console.WriteLine($"Total dates avec contrat manquant: {missingCount}");
        Console.WriteLine($"Total dates avec contrat présent: {presentCount}");

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








    public static async Task<int> WriteJCMain(string[] args)
    {
        // Exemples d'usage:
        //  - (par défaut)         Program.exe <inputDir> <recursive>
        //  - commande explicite   Program.exe writejc <inputDir> <recursive>

        if (args.Length > 0 && (args[0].Equals("-h", StringComparison.OrdinalIgnoreCase)
                             || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintHelp();
            return 0;
        }

        // Router simple: si le 1er arg est une commande connue, on route.
        // Sinon, on considère que l'utilisateur veut WriteJC directement.
        string cmd = (args.Length > 0 && IsCommand(args[0])) ? args[0].ToLowerInvariant() : "writejc";
        string[] cmdArgs = cmd == "writejc" && (args.Length == 0 || !IsCommand(args[0]))
            ? args // args = directement ceux de WriteJC
            : args.Skip(1).ToArray(); // args = après la commande

        return cmd switch
        {
            "writejc" => await WriteJC(cmdArgs),
            _ => UnknownCommand(cmd)
        };
    }

    private static bool IsCommand(string s)
        => s.Equals("writejc", StringComparison.OrdinalIgnoreCase);

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Commande inconnue: {cmd}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Program.exe [writejc] <inputDir> <recursive>");
        Console.WriteLine();
        Console.WriteLine("Exemples:");
        Console.WriteLine(@"  Program.exe C:\data\json true");
        Console.WriteLine(@"  Program.exe writejc C:\data\json false");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - <recursive> : true/false (optionnel). Par défaut: IncludeSubDirectories.");
        Console.WriteLine("  - Les .bin sont écrits dans ../bin à côté du dossier d'entrée.");
    }

    // =======================
    //      WRITEJC
    // =======================
    private static async Task<int> WriteJC(string[] args)
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

        using var cts = new CancellationTokenSource();
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
                    JsonToBinaryConverter.ConvertJson(jsonPath, binPath, ct);
                    sw.Stop();

                    Interlocked.Increment(ref ok);

                    // ⚠️ Console.WriteLine en parallèle peut ralentir; si besoin, log tous les N
                    // Console.WriteLine($"OK  {Path.GetFileName(jsonPath)} -> {Path.GetFileName(binPath)}  ({sw.Elapsed.TotalSeconds:F1}s)");
                    if (ok % 50 == 0)
                        Console.WriteLine($"Progress OK={ok} | Skip={skipped} | Fail={failed}");
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
        //createIndex();
        //return 0;
        int nbRecords = 0;
        JsonToBinaryIndex.ReadAll("data/bin/_index.bin", (startYmd, endYmd) =>
        {
            Console.WriteLine($"{startYmd} -> {endYmd}");
            nbRecords++;

        });
        Console.WriteLine($"Total records: {nbRecords}");
        return 0;

        ////////////

        //return await WriteJCMain(args);
        //return await ReadJC(args);
    }


}