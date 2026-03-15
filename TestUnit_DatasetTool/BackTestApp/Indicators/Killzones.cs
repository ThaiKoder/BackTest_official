using Avalonia;
using BacktestApp.Controls;
using BacktestApp.Indicators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Indicators.Killzones
{
    public class Killzones
    {
        [Fact]
        public void Test_CandlesNext_RingBuffer_And_KillZones_BlackBox_Should_Work_On_Whole_File()
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

            // Killzones black-box
            var killzones = new List<SessionHighLowIndicator>
    {
        new SessionHighLowIndicator("Morning",   new TimeSpan(10, 0, 0), new TimeSpan(12, 0, 0)),
        new SessionHighLowIndicator("Afternoon", new TimeSpan(14, 0, 0), new TimeSpan(16, 0, 0)),
    };

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

                // --- TEST BOITE NOIRE KILLZONES SUR CHAQUE CANDLE AJOUTEE ---
                foreach (var candle in current.Added)
                {
                    if (candle.Idx == -1)
                        continue;

                    long sec = candle.Ts / 1_000_000_000L;
                    long ns = candle.Ts % 1_000_000_000L;
                    var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                    Debug.WriteLine(
                        $"[CANDLE] idx={candle.Idx} ts={dt:yyyy-MM-dd HH:mm:ss}.{ns:D9} " +
                        $"H={candle.H} L={candle.L}");

                    foreach (var kz in killzones)
                    {
                        var output = kz.OnCandle(
                            candle.Ts,
                            candle.H,
                            candle.L,
                            1.0);

                        Debug.WriteLine(
                            $"    [KILLZONE {output.Name}] " +
                            $"State={output.State} | " +
                            $"Last=({output.LastHigh}, {output.LastLow}) " +
                            $"Prev=({output.PreviousHigh}, {output.PreviousLow}) " +
                            $"LastTs=[{output.LastStartTs} -> {output.LastEndTs}] " +
                            $"PrevTs=[{output.PreviousStartTs} -> {output.PreviousEndTs}]");

                        // Assertions minimales boîte noire
                        Assert.Equal(kz.Name, output.Name);

                        if (output.HasLast)
                        {
                            Assert.True(output.LastHigh >= output.LastLow,
                                $"Killzone {output.Name}: LastHigh doit être >= LastLow");
                            Assert.True(output.LastStartTs >= 0);
                            Assert.True(output.LastEndTs >= 0);
                        }

                        if (output.HasPrevious)
                        {
                            Assert.True(output.PreviousHigh >= output.PreviousLow,
                                $"Killzone {output.Name}: PreviousHigh doit être >= PreviousLow");
                            Assert.True(output.PreviousStartTs >= 0);
                            Assert.True(output.PreviousEndTs >= 0);
                        }
                    }
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