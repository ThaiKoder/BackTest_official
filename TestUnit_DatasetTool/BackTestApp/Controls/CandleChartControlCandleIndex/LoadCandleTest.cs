using Avalonia;
using BacktestApp.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;


namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlCandleIndex
{
    public class LoadCandleTest
    {
        [Fact]
        public void Test_Read_All_Candles_From_Current_File_After_One_FilesNext()
        {
            // Arrange
            var chart = new global::BacktestApp.Controls.CandleChartControl();
            chart.Test_LoadIndexFile("data/bin/_index.bin");

            int range = 3;
            int firstCursor = 0;

            // Act 1: un seul FilesNext
            var step = chart.FilesNext(firstCursor, range);

            Assert.True(step.CurrentIdx >= 0, "CurrentIdx doit être valide après le premier FilesNext.");

            int currentIdx = step.CurrentIdx;
            Debug.WriteLine($"[TEST] CurrentIdx = {currentIdx}");

            // Récupère start/end depuis l'index
            var (startYmd, endYmd) = chart.getFileIndex.Read(currentIdx);

            string filePath = Path.Combine(
                "data",
                "bin",
                $"glbx-mdp3-{startYmd}-{endYmd}.ohlcv-1m.bin");

            Debug.WriteLine($"[TEST] FilePath = {filePath}");

            Assert.True(File.Exists(filePath), $"Le fichier doit exister: {filePath}");

            // Act 2: ouvre le fichier courant et lit toutes les candles
            using var file = new global::BacktestApp.Controls.CandleChartControl.MmapCandleFile(filePath);

            Assert.True(file.Count > 0, $"Le fichier {filePath} doit contenir au moins une candle.");

            Span<byte> sym = stackalloc byte[global::BacktestApp.Controls.CandleChartControl.MmapCandleFile.SymbolSize];

            long readCount = 0;

            for (long i = 0; i < file.Count; i++)
            {
                bool ok = file.ReadAt(
                    i,
                    out long ts,
                    out long o,
                    out long h,
                    out long l,
                    out long c,
                    out uint v,
                    sym);

                Assert.True(ok, $"Lecture impossible à l'index {i} dans {filePath}");

                byte symbol = sym[0];

                long sec = ts / 1_000_000_000L;
                long ns = ts % 1_000_000_000L;

                var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                Debug.WriteLine(
                    $"[CANDLE] fileIdx={currentIdx} candleIdx={i} " +
                    $"date={dt:yyyy-MM-dd HH:mm:ss}.{ns:D9} " +
                    $"o={o} h={h} l={l} c={c} v={v} sym={symbol}");

                readCount++;
            }

            // Assert
            Assert.Equal(file.Count, readCount);
        }

        [Fact]
        public void Test_Read_All_Candles_From_Current_File_After_One_CandlesNext()
        {
            // Arrange
            var chart = new global::BacktestApp.Controls.CandleChartControl();

            // 1) Charger l'index des fichiers
            chart.Test_LoadIndexFile("data/bin/_index.bin");

            int fileRange = 3;
            int firstFileCursor = 0;

            // 2) Un seul FilesNext pour choisir le fichier courant
            var fileStep = chart.FilesNext(firstFileCursor, fileRange);

            Assert.True(fileStep.CurrentIdx >= 0, "CurrentIdx fichier doit être valide après le premier FilesNext.");

            int currentFileIdx = fileStep.CurrentIdx;
            Debug.WriteLine($"[TEST] currentFileIdx = {currentFileIdx}");

            // 3) Initialiser CandlesNext sur le fichier courant
            chart.Test_CandlesLoadFromCurrentFileIndex(currentFileIdx);

            int candleRange = 3;
            int firstCandleCursor = 0;

            // 4) Un seul CandlesNext
            //var candleStep = chart.CandlesNext(firstCandleCursor, candleRange);
            var candleStep = chart.CandlesNext(0, candleRange);


            Assert.True(candleStep.CurrentIdx >= 0, "CurrentIdx candle doit être valide après le premier CandlesNext.");

            Debug.WriteLine($"[TEST] candle currentIdx = {candleStep.CurrentIdx}");
            Debug.WriteLine($"[TEST] candle window count = {candleStep.Window.Count}");

            Debug.WriteLine($"{chart.Test_CandleCount}");

            int totalRead = 0;
            long lastTs = -1;

            while (candleStep.NextCursorIdx != -1)
            {
                foreach (var candle in candleStep.Added)
                {
                    if (candle.Idx == -1)
                    {
                        Debug.WriteLine($"[CANDLE] idx=-1 (candle invalide, probablement un placeholder pour le début de la fenêtre)");
                        Debug.WriteLine($"[CANDLE] ts={candle.Ts} o={candle.O} h={candle.H} l={candle.L} c={candle.C} v={candle.V} sym={candle.Sym}");
                        continue;

                    }

                    byte symbol = candle.Sym;

                    long sec = candle.Ts / 1_000_000_000L;
                    var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                    //    Debug.WriteLine(
                    //        $"[CANDLE] idx={candle.Idx} " +
                    //        $"ts={dt:yyyy-MM-dd HH:mm:ss} " +
                    //        $"o={candle.O} " +
                    //        $"h={candle.H} " +
                    //        $"l={candle.L} " +
                    //        $"c={candle.C} " +
                    //        $"v={candle.V} " +
                    //        $"sym={symbol}");

                    Assert.True(lastTs != candle.Ts, $"Dupplication candle {dt}");


                    if (candle.V == 37)
                    {
                        Debug.WriteLine("");
                    }
                    if (lastTs != candle.Ts)
                    {
                        lastTs = candle.Ts;
                    }
                    else
                    {
                        Debug.WriteLine($"[ERROR] Timestamp dupliqué détecté: ts={candle.Ts} (dt={dt:yyyy-MM-dd HH:mm:ss}) " +
                            $"(fileIdx={currentFileIdx}, candleIdx={candle.Idx})");
                    }


                    totalRead++;
                }

        
                candleStep = chart.CandlesNext(candleStep.NextCursorIdx, candleRange);

            }



            // 5) Debug de toutes les candles de la fenêtre courante

            Assert.True(totalRead > 0, "Au moins une itération de lecture de candles doit avoir eu lieu.");
            Assert.True(totalRead == chart.Test_CandleCount, $"Le nombre total de lectures de candles ({totalRead}) doit correspondre au nombre de candles dans le fichier ({chart.Test_CandleCount}).");
            
            // Assert minimal
            Assert.NotNull(candleStep.Window);
            Assert.Empty(candleStep.Window); // Fin fichier
        }

        [Fact]
        public void Test_Read_All_Candles_From_All_Files_Using_FilesNext_Added()
        {
            var chart = new global::BacktestApp.Controls.CandleChartControl();

            chart.Test_LoadIndexFile("data/bin/_index.bin");

            int fileRange = 3;
            int candleRange = 3;

            int fileCursor = 0;
            int totalFilesRead = 0;
            int totalCandlesRead = 0;

            long? previousTs = null;
            var uniqueTs = new HashSet<long>();


            while (true)
            {
                var fileStep = chart.FilesNext(fileCursor, fileRange);

                Debug.WriteLine(
                    $"[FILES] current={fileStep.CurrentIdx} next={fileStep.NextCursorIdx} " +
                    $"window=[{string.Join(", ", fileStep.Window.Select(f => f.Idx))}] " +
                    $"added=[{string.Join(", ", fileStep.Added.Select(f => f.Idx))}]");

                foreach (var file in fileStep.Added)
                {
                    if (file.Idx == -1)
                        continue;

                    int currentFileIdx = file.Idx;

                    Debug.WriteLine($"================ FILE {currentFileIdx} =================");

                    chart.Test_CandlesLoadFromCurrentFileIndex(currentFileIdx);

                    var candleStep = chart.CandlesNext(0, candleRange);

                    int totalReadForThisFile = 0;

                    while (true)
                    {
                        foreach (var candle in candleStep.Added)
                        {


                            long sec = candle.Ts / 1_000_000_000L;
                            long ns = candle.Ts % 1_000_000_000L;
                            var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                            if (candle.Idx == -1)
                                continue;


                            // 1) ordre strict des timestamps
                            if (previousTs.HasValue)
                            {

                                long? psec = previousTs / 1_000_000_000L;
                                long? pns = previousTs % 1_000_000_000L;
                                var pdt = DateTimeOffset.FromUnixTimeSeconds((long)psec).UtcDateTime;

                                if (! (candle.Ts > previousTs.Value))
                                {
                                    Debug.WriteLine($"[ERROR] Timestamp non croissant détecté: currentTs={candle.Ts} (dt={dt:yyyy-MM-dd HH:mm:ss}.{ns:D9}) <= previousTs={previousTs.Value} (pdt={pdt:yyyy-MM-dd HH:mm:ss}.{pns:D9}) " +
                                        $"(fileIdx={currentFileIdx}, candleIdx={candle.Idx})");
                                }
                                Assert.True(
                                    candle.Ts > previousTs.Value,
                                    $"Timestamp non croissant: currentTs={candle.Ts} <= previousTs={previousTs.Value} " +
                                    $"(fileIdx={currentFileIdx}, candleIdx={candle.Idx})");
                            }

                            // 2) pas de doublon global
                            bool added = uniqueTs.Add(candle.Ts);
                            Assert.True(
                                added,
                                $"Doublon de timestamp détecté: ts={candle.Ts} " +
                                $"(fileIdx={currentFileIdx}, candleIdx={candle.Idx})");


                            Debug.WriteLine(
                                $"[CANDLE] fileIdx={currentFileIdx} " +
                                $"candleIdx={candle.Idx} " +
                                $"ts={dt:yyyy-MM-dd HH:mm:ss}.{ns:D9} " +
                                $"o={candle.O} h={candle.H} l={candle.L} c={candle.C} v={candle.V} sym={(char)candle.Sym}");

                            previousTs = candle.Ts;

                            totalReadForThisFile++;
                            totalCandlesRead++;
                        }

                        if (candleStep.NextCursorIdx == -1)
                            break;

                        candleStep = chart.CandlesNext(candleStep.NextCursorIdx, candleRange);
                    }

                    Assert.Equal(
                        chart.Test_CandleCount,
                        totalReadForThisFile);

                    totalFilesRead++;
                }

                if (fileStep.NextCursorIdx == -1)
                    break;

                fileCursor = fileStep.NextCursorIdx;
            }

            Assert.True(totalFilesRead > 0);
            Assert.True(totalCandlesRead > 0);
            Assert.Equal(chart.Test_IndexCount, totalFilesRead);
        }

    }
}
