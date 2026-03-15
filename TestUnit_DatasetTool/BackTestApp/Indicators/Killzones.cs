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
        public void LoadNext_Should_Compare_AfternoonHigh_Vs_MorningHigh_After_Each_Completed_Reentry_And_Exit()
        {
            // Arrange
            var chart = new global::BacktestApp.Controls.CandleChartControl();

            // Référence hors chemin UI : nombre total attendu de fichiers et de candles
            chart.Test_LoadIndexFile("data/bin/_index.bin");

            long expectedTotalFiles = chart.Test_IndexCount;
            long expectedTotalCandles = 0;

            for (int fileIdx = 0; fileIdx < expectedTotalFiles; fileIdx++)
            {
                chart.Test_CandlesLoadFromCurrentFileIndex(fileIdx);
                expectedTotalCandles += chart.Test_CandleCount;
            }

            Assert.True(expectedTotalFiles > 0, "L'index doit contenir au moins un fichier.");
            Assert.True(expectedTotalCandles > 0, "Le nombre total de candles doit être > 0.");

            // Vrai chemin UI
            chart.Test_InitializeFilesAndCandlesMode();

            int initialFileIdx = chart.Test_GetUiLoadedFileIdx();
            int initialCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
            var initialCandles = chart.Test_GetUiWindowCandles().ToArray();

            Assert.True(initialFileIdx >= 0, $"FileIdx initial invalide: {initialFileIdx}");
            Assert.True(initialCurrentIdx >= 0, $"CurrentIdx initial invalide: {initialCurrentIdx}");
            Assert.NotNull(initialCandles);
            Assert.NotEmpty(initialCandles);

            var seenFiles = new HashSet<int> { initialFileIdx };
            var seenTs = new HashSet<long>();

            long totalCandlesRead = 0;
            long? previousGlobalTs = null;

            // Kill zones
            var zones = chart.Test_GetSessionZoneConfigs();

            var killZoneName1 = zones.First(z => z.Name == "Asian");
            var killZoneName2 = zones.First(z => z.Name == "London");

            var morning = new SessionHighLowIndicator(
                killZoneName1.Name,
                killZoneName1.Start,
                killZoneName1.End);

            var afternoon = new SessionHighLowIndicator(
                killZoneName2.Name,
                killZoneName2.Start,
                killZoneName2.End);


            long? lastSeenMorningEndTs = null;
            long? lastSeenAfternoonEndTs = null;

            long? pendingMorningEndTs = null;
            double? pendingMorningHigh = null;

            int compareOk = 0;
            int compareKo = 0;
            int skippedNoMorning = 0;
            int skippedDateMismatch = 0;

            static DateTime UtcFromNs(long tsNs)
            {
                long sec = tsNs / 1_000_000_000L;
                return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
            }

            void ProcessCandle(global::BacktestApp.Controls.CandleChartControl.CandleIndex.CandleItem candle)
            {
                // 1) Contrôle parcours global
                if (previousGlobalTs.HasValue)
                {
                    Assert.True(
                        candle.Ts > previousGlobalTs.Value,
                        $"Timestamp non croissant détecté. currentTs={candle.Ts} <= previousTs={previousGlobalTs.Value}");
                }

                Assert.True(
                    seenTs.Add(candle.Ts),
                    $"Timestamp dupliqué détecté: {candle.Ts}");

                previousGlobalTs = candle.Ts;
                totalCandlesRead++;

                // 2) Feed des kill zones
                var m = morning.OnCandle(candle.Ts, candle.H, candle.L, 1.0);
                var a = afternoon.OnCandle(candle.Ts, candle.H, candle.L, 1.0);

                // 3) Détection d'une nouvelle Morning terminée
                if (m is not null && m.HasLast)
                {
                    if (!lastSeenMorningEndTs.HasValue || m.LastEndTs != lastSeenMorningEndTs.Value)
                    {
                        lastSeenMorningEndTs = m.LastEndTs;
                        pendingMorningEndTs = m.LastEndTs;
                        pendingMorningHigh = m.LastHigh;

                        //Debug.WriteLine(
                        //    $"[MORNING CLOSED] " +
                        //    $"date={UtcFromNs(m.LastEndTs):yyyy-MM-dd} " +
                        //    $"end={UtcFromNs(m.LastEndTs):yyyy-MM-dd HH:mm:ss} " +
                        //    $"high={m.LastHigh}");
                    }
                }

                // 4) Détection d'une nouvelle Afternoon terminée
                if (a is not null && a.HasLast)
                {
                    if (!lastSeenAfternoonEndTs.HasValue || a.LastEndTs != lastSeenAfternoonEndTs.Value)
                    {
                        lastSeenAfternoonEndTs = a.LastEndTs;

                        if (!pendingMorningEndTs.HasValue || !pendingMorningHigh.HasValue)
                        {
                            skippedNoMorning++;

                            //Debug.WriteLine(
                            //    $"[AFTERNOON CLOSED - SKIP NO MORNING] " +
                            //    $"date={UtcFromNs(a.LastEndTs):yyyy-MM-dd} " +
                            //    $"end={UtcFromNs(a.LastEndTs):yyyy-MM-dd HH:mm:ss} " +
                            //    $"high={a.LastHigh}");
                            return;
                        }

                        var morningDate = UtcFromNs(pendingMorningEndTs.Value).Date;
                        var afternoonDate = UtcFromNs(a.LastEndTs).Date;

                        if (morningDate != afternoonDate)
                        {
                            skippedDateMismatch++;

                            //Debug.WriteLine(
                            //    $"[AFTERNOON CLOSED - SKIP DATE MISMATCH] " +
                            //    $"morningDate={morningDate:yyyy-MM-dd} " +
                            //    $"afternoonDate={afternoonDate:yyyy-MM-dd} " +
                            //    $"morningHigh={pendingMorningHigh.Value} " +
                            //    $"afternoonHigh={a.LastHigh}");

                            pendingMorningEndTs = null;
                            pendingMorningHigh = null;
                            return;
                        }

                        bool isOk = a.LastLow < (m.LastHigh-(m.LastHigh- m.LastLow));

                        if (isOk)
                            compareOk++;
                        else
                            compareKo++;

                        //Debug.WriteLine(
                        //    $"[COMPARE] date={afternoonDate:yyyy-MM-dd} " +
                        //    $"morningHigh={pendingMorningHigh.Value} " +
                        //    $"afternoonHigh={a.LastHigh} " +
                        //    $"result={(isOk ? "OK" : "KO")}");

                        // On consomme ce cycle.
                        // Il faudra rerentrer puis resortir d'une nouvelle Morning
                        // avant de pouvoir refaire une comparaison.
                        pendingMorningEndTs = null;
                        pendingMorningHigh = null;
                    }
                }
            }

            // Fenêtre initiale : traitée une seule fois
            foreach (var candle in initialCandles)
            {
                ProcessCandle(candle);
            }

            int iterations = 0;
            int previousFileIdx = initialFileIdx;
            int previousCurrentIdx = initialCurrentIdx;

            // Act
            while (chart.Test_AdvanceCandlesNext())
            {
                iterations++;

                int currentFileIdx = chart.Test_GetUiLoadedFileIdx();
                int currentIdx = chart.Test_GetUiCandleCurrentIdx();
                int nextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();

                var addedCandles = chart.Test_GetLastAddedCandles().ToArray();
                var loadedTs = chart.Test_GetLoadedTimestamps();

                Assert.True(currentFileIdx >= 0, $"FileIdx invalide après loadNext: {currentFileIdx}");
                Assert.True(currentIdx >= 0, $"CurrentIdx invalide après loadNext: {currentIdx}");
                Assert.NotNull(loadedTs);
                Assert.NotEmpty(loadedTs);

                seenFiles.Add(currentFileIdx);

                // Cohérence progression fichier / candle
                if (currentFileIdx == previousFileIdx)
                {
                    Assert.True(
                        currentIdx > previousCurrentIdx,
                        $"Le curseur candle doit avancer dans le même fichier. " +
                        $"fileIdx={currentFileIdx}, before={previousCurrentIdx}, after={currentIdx}");
                }
                else
                {
                    Assert.True(
                        currentFileIdx > previousFileIdx,
                        $"Le fileIdx doit avancer strictement. before={previousFileIdx}, after={currentFileIdx}");
                }

                // Fenêtre courante toujours croissante
                for (int i = 1; i < loadedTs.Count; i++)
                {
                    Assert.True(
                        loadedTs[i] > loadedTs[i - 1],
                        $"Les timestamps chargés doivent être strictement croissants. " +
                        $"fileIdx={currentFileIdx}, i={i}, prev={loadedTs[i - 1]}, cur={loadedTs[i]}");
                }

                // Seules les nouvelles candles Added sont traitées
                foreach (var candle in addedCandles)
                {
                    ProcessCandle(candle);
                }

                previousFileIdx = currentFileIdx;
                previousCurrentIdx = currentIdx;

                Assert.True(
                    iterations <= expectedTotalCandles,
                    $"Boucle suspecte: trop d'itérations. iterations={iterations}, expectedTotalCandles={expectedTotalCandles}, next={nextCursorIdx}");
            }

            // Assert final
            Assert.True(iterations > 0, "Le test doit effectuer au moins un loadNext().");

            var finalLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
            var finalCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
            var finalNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
            var finalTs = chart.Test_GetLoadedTimestamps();

            Assert.True(finalLoadedFileIdx >= 0, $"FileIdx final invalide: {finalLoadedFileIdx}");
            Assert.True(finalCurrentIdx >= 0, $"CurrentIdx final invalide: {finalCurrentIdx}");
            Assert.Equal(-1, finalNextCursorIdx);

            Assert.NotNull(finalTs);
            Assert.NotEmpty(finalTs);

            // Le test doit bien avoir parcouru tout l'index
            Assert.Equal(expectedTotalFiles, seenFiles.Count);
            Assert.Equal(expectedTotalCandles, totalCandlesRead);
            Assert.Equal(expectedTotalCandles, seenTs.Count);

            int totalComparisons = compareOk + compareKo;

            Debug.WriteLine("==================================================");
            Debug.WriteLine("[KILLZONE SUMMARY]");
            Debug.WriteLine($"filesRead={seenFiles.Count}/{expectedTotalFiles}");
            Debug.WriteLine($"candlesRead={totalCandlesRead}/{expectedTotalCandles}");
            Debug.WriteLine($"compareOk={compareOk}");
            Debug.WriteLine($"compareKo={compareKo}");
            Debug.WriteLine($"compareOkRate={(totalComparisons > 0 ? (double)compareOk / totalComparisons : 0):P2}");
            Debug.WriteLine($"totalComparisons={totalComparisons}");
            Debug.WriteLine($"skippedNoMorning={skippedNoMorning}");
            Debug.WriteLine($"skippedDateMismatch={skippedDateMismatch}");
            Debug.WriteLine("==================================================");

            Assert.True(
                totalComparisons > 0,
                "Aucune comparaison Morning -> Afternoon n'a été produite.");
        }

        private enum ZoneMetric
        {
            High,
            Low
        }

        [Fact]
        public void LoadNext_Should_Compare_SelectedMetric_Between_Two_Zones_After_Each_Completed_Cycle()
        {
            // ==================================================
            // CONFIG TEST
            // ==================================================
            const string referenceZoneName = "Asian";
            const string targetZoneName = "London";

            const ZoneMetric referenceMetric = ZoneMetric.High;
            const ZoneMetric targetMetric = ZoneMetric.Low;

            // Exemples :
            // target > reference   => (targetValue, referenceValue) => targetValue > referenceValue
            // target < reference   => (targetValue, referenceValue) => targetValue < referenceValue
            // target >= reference  => (targetValue, referenceValue) => targetValue >= referenceValue
            // target <= reference  => (targetValue, referenceValue) => targetValue <= referenceValue
            static bool Comparison(double targetValue, double referenceValue) => targetValue < referenceValue;

            const bool enableExactExpectedCandleCount = false;   // false = ultra rapide
            const bool enableStrictUniqueTimestampCheck = true;  // peut être mis à false pour encore plus de vitesse
            const bool enableVerboseDebug = false;

            // ==================================================
            // ARRANGE
            // ==================================================
            var chart = new global::BacktestApp.Controls.CandleChartControl();

            chart.Test_LoadIndexFile("data/bin/_index.bin");

            long expectedTotalFiles = chart.Test_IndexCount;
            long? expectedTotalCandles = null;

            if (enableExactExpectedCandleCount)
            {
                long exactCount = 0;
                for (int fileIdx = 0; fileIdx < expectedTotalFiles; fileIdx++)
                {
                    chart.Test_CandlesLoadFromCurrentFileIndex(fileIdx);
                    exactCount += chart.Test_CandleCount;
                }

                expectedTotalCandles = exactCount;
            }

            Assert.True(expectedTotalFiles > 0, "L'index doit contenir au moins un fichier.");

            chart.Test_InitializeFilesAndCandlesMode();

            int initialLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
            int initialCandleIdx = chart.Test_GetUiCandleCurrentIdx();
            var initialWindowCandles = chart.Test_GetUiWindowCandles();

            Assert.True(initialLoadedFileIdx >= 0, $"FileIdx initial invalide: {initialLoadedFileIdx}");
            Assert.True(initialCandleIdx >= 0, $"CurrentIdx initial invalide: {initialCandleIdx}");
            Assert.NotNull(initialWindowCandles);
            Assert.NotEmpty(initialWindowCandles);

            var zoneConfigs = chart.Test_GetSessionZoneConfigs();

            var referenceZoneConfig = zoneConfigs.First(z => z.Name == referenceZoneName);
            var targetZoneConfig = zoneConfigs.First(z => z.Name == targetZoneName);

            var referenceZone = new SessionHighLowIndicator(
                referenceZoneConfig.Name,
                referenceZoneConfig.Start,
                referenceZoneConfig.End);

            var targetZone = new SessionHighLowIndicator(
                targetZoneConfig.Name,
                targetZoneConfig.Start,
                targetZoneConfig.End);

            var visitedFileIndexes = new HashSet<int> { initialLoadedFileIdx };
            HashSet<long>? visitedTimestamps = enableStrictUniqueTimestampCheck ? new HashSet<long>() : null;

            long totalCandlesRead = 0;
            long? previousGlobalTimestamp = null;

            long? lastReferenceClosedEndTs = null;
            long? lastTargetClosedEndTs = null;

            long? pendingReferenceEndTs = null;
            double? pendingReferenceValue = null;

            int compareOk = 0;
            int compareKo = 0;
            int skippedMissingReference = 0;
            int skippedDateMismatch = 0;

            static double SelectMetric(SessionHighLowIndicator.Output output, ZoneMetric metric)
            {
                return metric switch
                {
                    ZoneMetric.High => output.LastHigh,
                    ZoneMetric.Low => output.LastLow,
                    _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
                };
            }

            static DateTime UtcFromNs(long tsNs)
            {
                long sec = tsNs / 1_000_000_000L;
                return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
            }

            void ProcessCandle(global::BacktestApp.Controls.CandleChartControl.CandleIndex.CandleItem candle)
            {
                // 1) Contrôle de progression globale
                if (previousGlobalTimestamp.HasValue)
                {
                    Assert.True(
                        candle.Ts > previousGlobalTimestamp.Value,
                        $"Timestamp non croissant détecté. currentTs={candle.Ts} <= previousTs={previousGlobalTimestamp.Value}");
                }

                if (visitedTimestamps is not null)
                {
                    Assert.True(
                        visitedTimestamps.Add(candle.Ts),
                        $"Timestamp dupliqué détecté: {candle.Ts}");
                }

                previousGlobalTimestamp = candle.Ts;
                totalCandlesRead++;

                // 2) Feed des 2 zones
                var referenceOutput = referenceZone.OnCandle(candle.Ts, candle.H, candle.L, 1.0);
                var targetOutput = targetZone.OnCandle(candle.Ts, candle.H, candle.L, 1.0);

                // 3) Détection fermeture nouvelle zone de référence
                if (referenceOutput is not null && referenceOutput.HasLast)
                {
                    if (!lastReferenceClosedEndTs.HasValue || referenceOutput.LastEndTs != lastReferenceClosedEndTs.Value)
                    {
                        lastReferenceClosedEndTs = referenceOutput.LastEndTs;
                        pendingReferenceEndTs = referenceOutput.LastEndTs;
                        pendingReferenceValue = SelectMetric(referenceOutput, referenceMetric);

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[REFERENCE CLOSED] " +
                                $"zone={referenceZoneName} " +
                                $"date={UtcFromNs(referenceOutput.LastEndTs):yyyy-MM-dd} " +
                                $"value={pendingReferenceValue}");
                        }
                    }
                }

                // 4) Détection fermeture nouvelle zone cible
                if (targetOutput is not null && targetOutput.HasLast)
                {
                    if (!lastTargetClosedEndTs.HasValue || targetOutput.LastEndTs != lastTargetClosedEndTs.Value)
                    {
                        lastTargetClosedEndTs = targetOutput.LastEndTs;

                        if (!pendingReferenceEndTs.HasValue || !pendingReferenceValue.HasValue)
                        {
                            skippedMissingReference++;

                            if (enableVerboseDebug)
                            {
                                Debug.WriteLine(
                                    $"[TARGET CLOSED - SKIP NO REFERENCE] " +
                                    $"zone={targetZoneName} " +
                                    $"date={UtcFromNs(targetOutput.LastEndTs):yyyy-MM-dd}");
                            }

                            return;
                        }

                        var referenceDate = UtcFromNs(pendingReferenceEndTs.Value).Date;
                        var targetDate = UtcFromNs(targetOutput.LastEndTs).Date;

                        if (referenceDate != targetDate)
                        {
                            skippedDateMismatch++;

                            if (enableVerboseDebug)
                            {
                                Debug.WriteLine(
                                    $"[TARGET CLOSED - SKIP DATE MISMATCH] " +
                                    $"referenceDate={referenceDate:yyyy-MM-dd} " +
                                    $"targetDate={targetDate:yyyy-MM-dd}");
                            }

                            pendingReferenceEndTs = null;
                            pendingReferenceValue = null;
                            return;
                        }

                        double targetValue = SelectMetric(targetOutput, targetMetric);
                        bool isOk = Comparison(targetValue, pendingReferenceValue.Value);

                        if (isOk)
                            compareOk++;
                        else
                            compareKo++;

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[COMPARE] " +
                                $"date={targetDate:yyyy-MM-dd} " +
                                $"referenceZone={referenceZoneName} referenceMetric={referenceMetric} referenceValue={pendingReferenceValue.Value} " +
                                $"targetZone={targetZoneName} targetMetric={targetMetric} targetValue={targetValue} " +
                                $"result={(isOk ? "OK" : "KO")}");
                        }

                        // cycle consommé
                        pendingReferenceEndTs = null;
                        pendingReferenceValue = null;
                    }
                }
            }

            // ==================================================
            // FENÊTRE INITIALE
            // ==================================================
            foreach (var candle in initialWindowCandles)
            {
                ProcessCandle(candle);
            }

            int iterationCount = 0;
            int previousLoadedFileIdx = initialLoadedFileIdx;
            int previousCandleIdx = initialCandleIdx;

            // ==================================================
            // ACT
            // ==================================================
            while (chart.Test_AdvanceCandlesNext())
            {
                iterationCount++;

                int currentLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
                int currentCandleIdx = chart.Test_GetUiCandleCurrentIdx();
                int nextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();

                var addedCandles = chart.Test_GetLastAddedCandles();

                Assert.True(currentLoadedFileIdx >= 0, $"FileIdx invalide après loadNext: {currentLoadedFileIdx}");
                Assert.True(currentCandleIdx >= 0, $"CurrentIdx invalide après loadNext: {currentCandleIdx}");
                Assert.NotNull(addedCandles);

                visitedFileIndexes.Add(currentLoadedFileIdx);

                if (currentLoadedFileIdx == previousLoadedFileIdx)
                {
                    Assert.True(
                        currentCandleIdx > previousCandleIdx,
                        $"Le curseur candle doit avancer dans le même fichier. fileIdx={currentLoadedFileIdx}, before={previousCandleIdx}, after={currentCandleIdx}");
                }
                else
                {
                    Assert.True(
                        currentLoadedFileIdx > previousLoadedFileIdx,
                        $"Le fileIdx doit avancer strictement. before={previousLoadedFileIdx}, after={currentLoadedFileIdx}");
                }

                foreach (var candle in addedCandles)
                {
                    ProcessCandle(candle);
                }

                previousLoadedFileIdx = currentLoadedFileIdx;
                previousCandleIdx = currentCandleIdx;

                if (expectedTotalCandles.HasValue)
                {
                    Assert.True(
                        iterationCount <= expectedTotalCandles.Value,
                        $"Boucle suspecte: trop d'itérations. iterations={iterationCount}, expectedTotalCandles={expectedTotalCandles.Value}, next={nextCursorIdx}");
                }
            }

            // ==================================================
            // ASSERT FINAL
            // ==================================================
            Assert.True(iterationCount > 0, "Le test doit effectuer au moins un loadNext().");

            int finalLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
            int finalCandleIdx = chart.Test_GetUiCandleCurrentIdx();
            int finalNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();

            Assert.True(finalLoadedFileIdx >= 0, $"FileIdx final invalide: {finalLoadedFileIdx}");
            Assert.True(finalCandleIdx >= 0, $"CurrentIdx final invalide: {finalCandleIdx}");
            Assert.Equal(-1, finalNextCursorIdx);

            Assert.Equal(expectedTotalFiles, visitedFileIndexes.Count);

            if (expectedTotalCandles.HasValue)
                Assert.Equal(expectedTotalCandles.Value, totalCandlesRead);

            if (visitedTimestamps is not null)
                Assert.Equal(totalCandlesRead, visitedTimestamps.Count);

            int totalComparisons = compareOk + compareKo;

            Debug.WriteLine("==================================================");
            Debug.WriteLine("[ZONE COMPARISON SUMMARY]");
            Debug.WriteLine($"referenceZone={referenceZoneName}");
            Debug.WriteLine($"referenceMetric={referenceMetric}");
            Debug.WriteLine($"targetZone={targetZoneName}");
            Debug.WriteLine($"targetMetric={targetMetric}");
            Debug.WriteLine($"filesRead={visitedFileIndexes.Count}/{expectedTotalFiles}");
            Debug.WriteLine($"candlesRead={totalCandlesRead}{(expectedTotalCandles.HasValue ? $"/{expectedTotalCandles.Value}" : "")}");
            Debug.WriteLine($"compareOk={compareOk}");
            Debug.WriteLine($"compareKo={compareKo}");
            Debug.WriteLine($"compareOkRate={(totalComparisons > 0 ? (double)compareOk / totalComparisons : 0):P2}");
            Debug.WriteLine($"totalComparisons={totalComparisons}");
            Debug.WriteLine($"skippedMissingReference={skippedMissingReference}");
            Debug.WriteLine($"skippedDateMismatch={skippedDateMismatch}");
            Debug.WriteLine("==================================================");

            Assert.True(
                totalComparisons > 0,
                "Aucune comparaison entre les deux zones n'a été produite.");
        }
    }
}