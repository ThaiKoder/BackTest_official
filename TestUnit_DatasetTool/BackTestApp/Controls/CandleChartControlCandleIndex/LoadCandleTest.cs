using Avalonia;
using BacktestApp.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            chart.Test_LoadIndexFile("data/bin/_index.bin");

            int fileRange = 3;
            int firstFileCursor = 0;

            var fileStep = chart.FilesNext(firstFileCursor, fileRange);

            Assert.True(fileStep.CurrentIdx >= 0, "CurrentIdx fichier doit être valide après le premier FilesNext.");

            int currentFileIdx = fileStep.CurrentIdx;
            Debug.WriteLine($"[TEST] currentFileIdx = {currentFileIdx}");

            chart.Test_CandlesLoadFromCurrentFileIndex(currentFileIdx);

            int candleRange = 3;
            var candleStep = chart.CandlesNext(0, candleRange);

            Assert.True(candleStep.CurrentIdx >= 0, "CurrentIdx candle doit être valide après le premier CandlesNext.");

            Debug.WriteLine($"[TEST] candle currentIdx = {candleStep.CurrentIdx}");
            Debug.WriteLine($"[TEST] candle window count = {candleStep.Window.Count}");
            Debug.WriteLine($"{chart.Test_CandleCount}");

            int totalRead = 0;
            long? lastTs = null;

            while (true)
            {
                foreach (var candle in candleStep.Added)
                {
                    if (candle.Idx == -1)
                        continue;

                    byte symbol = candle.Sym;

                    long sec = candle.Ts / 1_000_000_000L;
                    long ns = candle.Ts % 1_000_000_000L;
                    var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                    Debug.WriteLine(
                        $"[CANDLE] idx={candle.Idx} " +
                        $"ts={dt:yyyy-MM-dd HH:mm:ss}.{ns:D9} " +
                        $"o={candle.O} " +
                        $"h={candle.H} " +
                        $"l={candle.L} " +
                        $"c={candle.C} " +
                        $"v={candle.V} " +
                        $"sym={symbol}");

                    if (lastTs.HasValue)
                        Assert.True(lastTs.Value < candle.Ts, $"Duplication ou désordre candle {dt:yyyy-MM-dd HH:mm:ss}.{ns:D9}");

                    lastTs = candle.Ts;
                    totalRead++;
                }

                if (candleStep.NextCursorIdx == -1)
                    break;

                candleStep = chart.CandlesNext(candleStep.NextCursorIdx, candleRange);
            }

            Assert.True(totalRead > 0, "Au moins une itération de lecture de candles doit avoir eu lieu.");
            Assert.Equal(chart.Test_CandleCount, totalRead);

            // vrai no-op après fin
            var finalNoOp = chart.CandlesNext(candleStep.CurrentIdx, candleRange);

            Assert.Equal(candleStep.CurrentIdx, finalNoOp.CurrentIdx);
            Assert.Equal(-1, finalNoOp.NextCursorIdx);
            Assert.Equal(
                candleStep.Window.Select(x => x.Idx).ToArray(),
                finalNoOp.Window.Select(x => x.Idx).ToArray());
            Assert.Empty(finalNoOp.Added);
            Assert.Empty(finalNoOp.Removed);
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
                            if (candle.Idx == -1)
                                continue;

                            long sec = candle.Ts / 1_000_000_000L;
                            long ns = candle.Ts % 1_000_000_000L;
                            var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                            if (previousTs.HasValue)
                            {
                                long psec = previousTs.Value / 1_000_000_000L;
                                long pns = previousTs.Value % 1_000_000_000L;
                                var pdt = DateTimeOffset.FromUnixTimeSeconds(psec).UtcDateTime;

                                if (!(candle.Ts > previousTs.Value))
                                {
                                    Debug.WriteLine(
                                        $"[ERROR] Timestamp non croissant détecté: " +
                                        $"currentTs={candle.Ts} (dt={dt:yyyy-MM-dd HH:mm:ss}.{ns:D9}) <= " +
                                        $"previousTs={previousTs.Value} (pdt={pdt:yyyy-MM-dd HH:mm:ss}.{pns:D9}) " +
                                        $"(fileIdx={currentFileIdx}, candleIdx={candle.Idx})");
                                }

                                Assert.True(
                                    candle.Ts > previousTs.Value,
                                    $"Les timestamps doivent être strictement croissants. " +
                                    $"currentTs={candle.Ts} ({dt:yyyy-MM-dd HH:mm:ss}.{ns:D9}) <= previousTs={previousTs.Value}");
                            }

                            Assert.True(
                                uniqueTs.Add(candle.Ts),
                                $"Timestamp dupliqué détecté: {candle.Ts} ({dt:yyyy-MM-dd HH:mm:ss}.{ns:D9})");

                            previousTs = candle.Ts;
                            totalCandlesRead++;
                            totalReadForThisFile++;
                        }

                        if (candleStep.NextCursorIdx == -1)
                            break;

                        candleStep = chart.CandlesNext(candleStep.NextCursorIdx, candleRange);
                    }

                    Assert.Equal(chart.Test_CandleCount, totalReadForThisFile);
                    totalFilesRead++;
                }

                if (fileStep.NextCursorIdx == -1)
                    break;

                fileCursor = fileStep.NextCursorIdx;
            }

            Assert.Equal(chart.Test_IndexCount, totalFilesRead);
            Assert.True(totalCandlesRead > 0, "Le nombre total de candles lues doit être > 0.");
            Assert.Equal(totalCandlesRead, uniqueTs.Count);
        }

        [Fact]
        public void Test_CandlesNext_RingBuffer_Should_Shift_And_Append_Correctly_On_Whole_File()
        {
            var chart = new global::BacktestApp.Controls.CandleChartControl();

            chart.Test_LoadIndexFile("data/bin/_index.bin");

            int fileRange = 3;
            int candleRange = 3;
            int candleStepSize = candleRange + 1;

            var fileStep = chart.FilesNext(0, fileRange);
            Assert.True(fileStep.CurrentIdx >= 0);

            int currentFileIdx = fileStep.CurrentIdx;
            chart.Test_CandlesLoadFromCurrentFileIndex(currentFileIdx);

            int count = (int)chart.Test_CandleCount;
            Assert.True(count > 0, "Le fichier courant doit contenir au moins une candle.");

            int cursor = 0;
            int iteration = 0;

            var allSeen = new HashSet<int>();
            global::BacktestApp.Controls.CandleChartControl.CandleIndex.CandleCursorStep? previous = null;

            while (true)
            {
                var current = chart.CandlesNext(cursor, candleRange);
                iteration++;

                Assert.Equal(cursor, current.CurrentIdx);

                var currentWindow = current.Window.Select(x => x.Idx).ToArray();
                var currentAdded = current.Added.Select(x => x.Idx).ToArray();
                var currentRemoved = current.Removed.Select(x => x.Idx).ToArray();

                // fenêtre attendue
                int[] expectedWindow = new int[candleRange * 2 + 1];
                int p = 0;
                for (int i = cursor - candleRange; i <= cursor + candleRange; i++)
                    expectedWindow[p++] = (i < 0 || i >= count) ? -1 : i;

                Assert.Equal(expectedWindow, currentWindow);

                // Added global unique
                foreach (var idx in currentAdded)
                {
                    Assert.True(idx >= 0 && idx < count, $"Added contient un idx invalide: {idx}");
                    Assert.True(allSeen.Add(idx), $"Idx ajouté plusieurs fois dans le parcours global: {idx}");
                }

                if (previous == null)
                {
                    Assert.Equal(expectedWindow.Where(x => x != -1).ToArray(), currentAdded);
                    Assert.Empty(currentRemoved);
                }
                else
                {
                    var prevWindow = previous.Window.Select(x => x.Idx).ToArray();

                    int[] expectedAdded = expectedWindow
                        .Where(x => x != -1 && !prevWindow.Contains(x))
                        .ToArray();

                    int[] expectedRemoved = prevWindow
                        .Where(x => x != -1 && !expectedWindow.Contains(x))
                        .ToArray();

                    Assert.Equal(expectedAdded, currentAdded);
                    Assert.Equal(expectedRemoved, currentRemoved);

                    var prevKept = prevWindow
                        .Where(x => x != -1 && !currentRemoved.Contains(x))
                        .ToArray();

                    var currentValid = currentWindow
                        .Where(x => x != -1)
                        .ToArray();

                    var expectedCurrentValid = prevKept
                        .Concat(currentAdded)
                        .ToArray();

                    Assert.Equal(expectedCurrentValid, currentValid);
                    Assert.Equal(previous.CurrentIdx + candleStepSize, current.CurrentIdx);
                }

                bool hasRightMinusOne = expectedWindow[^1] == -1;
                int expectedNext = hasRightMinusOne ? -1 : cursor + candleStepSize;
                Assert.Equal(expectedNext, current.NextCursorIdx);

                previous = current;

                if (current.NextCursorIdx == -1)
                    break;

                cursor = current.NextCursorIdx;

                Assert.True(iteration <= count + 2, $"Boucle suspecte: iteration={iteration}, count={count}");
            }

            Assert.Equal(count, allSeen.Count);
            Assert.Equal(Enumerable.Range(0, count).ToArray(), allSeen.OrderBy(x => x).ToArray());

            var finalNoOp = chart.CandlesNext(previous!.CurrentIdx, candleRange);

            Assert.Equal(previous.CurrentIdx, finalNoOp.CurrentIdx);
            Assert.Equal(-1, finalNoOp.NextCursorIdx);
            Assert.Equal(
                previous.Window.Select(x => x.Idx).ToArray(),
                finalNoOp.Window.Select(x => x.Idx).ToArray());
            Assert.Empty(finalNoOp.Added);
            Assert.Empty(finalNoOp.Removed);
        }
    }
}