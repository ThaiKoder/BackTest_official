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
            var candleStep = chart.CandlesNext(firstCandleCursor, candleRange);

            Assert.True(candleStep.CurrentIdx >= 0, "CurrentIdx candle doit être valide après le premier CandlesNext.");

            Debug.WriteLine($"[TEST] candle currentIdx = {candleStep.CurrentIdx}");
            Debug.WriteLine($"[TEST] candle window count = {candleStep.Window.Count}");

            // 5) Debug de toutes les candles de la fenêtre courante
            foreach (var candle in candleStep.Window)
            {

                if (candle.Idx == -1) continue;

                byte symbol = candle.Sym;

                long sec = candle.Ts / 1_000_000_000L;
                long ns = candle.Ts % 1_000_000_000L;

                var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                Debug.WriteLine(
                    $"[CANDLE] idx={candle.Idx} " +
                    $"ts={dt:yyyy-MM-dd HH:mm:ss} " +
                    $"o={candle.O} " +
                    $"h={candle.H} " +
                    $"l={candle.L} " +
                    $"c={candle.C} " +
                    $"v={candle.V} " +
                    $"sym={symbol}");
            }

            // Assert minimal
            Assert.NotNull(candleStep.Window);
            Assert.NotEmpty(candleStep.Window);
        }
    }
}
