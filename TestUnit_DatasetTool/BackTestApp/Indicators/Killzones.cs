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


        private sealed record ZoneSnapshot(
              string Name,
              DateTime DateUtc,
              long StartTs,
              long EndTs,
              double High,
              double Low)
        {
            public double Mid => (High + Low) / 2.0;
            public double Range => High - Low;
        }

        private sealed record ReferenceConfig(
            string Key,
            string ZoneName);

        private sealed record ConditionContext(
            IReadOnlyDictionary<string, ZoneSnapshot> Refs,
            ZoneSnapshot Target);

        [Fact]
        public void LoadNext_Should_Compare_Zones_With_Multiple_Dynamic_References_And_MultiReference_Conditions()
        {
            // ==================================================
            // CONFIG TEST
            // ==================================================
            const string targetZoneName = "NY AM";

            const bool enableExactExpectedCandleCount = false;
            const bool enableStrictUniqueTimestampCheck = false;
            const bool enableVerboseDebug = false;

            var zoneConfigs = new[]
{
                    new { Name = "Asian", Start = new TimeSpan(1, 0, 0), End = new TimeSpan(5, 0, 0) },
                    new { Name = "London", Start = new TimeSpan(7, 0, 0), End = new TimeSpan(10, 0, 0) },
                    new { Name = "Between London - NY AM", Start = new TimeSpan(10, 0, 0), End = new TimeSpan(13, 30, 0) },
                    new { Name = "NY AM", Start = new TimeSpan(13, 30, 0), End = new TimeSpan(16, 0, 0) },
                };

            // ==================================================
            // REFERENCES DYNAMIQUES
            // ==================================================
            var references = new List<ReferenceConfig>
        {
            new("refAsian", "Asian"),
            new("refLondon", "London"),
            new("refLondon-NY AM", "Between London - NY AM")
        };

            // ==================================================
            // CONDITIONS IMBRIQUEES
            // ==================================================
            var entryConditions = new List<(string Name, Func<ConditionContext, bool> Test)>
            {
                ("C1", ctx => ctx.Refs["refAsian"].Range < (ctx.Refs["refLondon"].Range/100*60)),
                ("C2", ctx => ctx.Refs["refAsian"].Range > (ctx.Refs["refLondon"].Range/100*30)),
                // ("C3", ctx => ctx.Refs["refAsian"].Range < ctx.Refs["refLondon"].Range),
                // ("C4", ctx => ctx.Refs["refAsian"].Low > ctx.Refs["refLondon-NY AM"].Low)
            };

            Func<ConditionContext, bool> finalCondition =
               ctx => ((ctx.Refs["refLondon"].Range * 1) < ctx.Target.Range);

            // ==================================================
            // VALIDATION CONFIG
            // ==================================================
            Assert.NotEmpty(references);

            var duplicateReferenceKeys = references
                .GroupBy(x => x.Key)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            Assert.True(
                duplicateReferenceKeys.Length == 0,
                $"Les clés de références doivent être uniques. Doublons: {string.Join(", ", duplicateReferenceKeys)}");

            var referenceByKey = references.ToDictionary(x => x.Key, x => x);

            static IEnumerable<string> ExtractReferenceKeysFromConditions(
                IEnumerable<(string Name, Func<ConditionContext, bool> Test)> conditions,
                IEnumerable<string> knownKeys)
            {
                return knownKeys;
            }

            _ = ExtractReferenceKeysFromConditions(entryConditions, referenceByKey.Keys);

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

            // ==================================================
            // CONFIG ZONES (remplace Test_GetSessionZoneConfigs)
            // ==================================================

                var usedZoneNames = references
                    .Select(x => x.ZoneName)
                    .Append(targetZoneName)
                    .Distinct()
                    .ToArray();

            // Validation
            foreach (var zoneName in usedZoneNames)
            {
                Assert.Contains(zoneConfigs, z => z.Name == zoneName);
            }

            // Création des indicateurs
            var zoneIndicators = new Dictionary<string, SessionHighLowIndicator>();

            foreach (var zoneName in usedZoneNames)
            {
                var config = zoneConfigs.First(z => z.Name == zoneName);

                zoneIndicators[zoneName] = new SessionHighLowIndicator(
                    config.Name,
                    config.Start,
                    config.End);
            }

            var visitedFileIndexes = new HashSet<int> { initialLoadedFileIdx };
            HashSet<long>? visitedTimestamps = enableStrictUniqueTimestampCheck ? new HashSet<long>() : null;

            long totalCandlesRead = 0;
            long? previousGlobalTimestamp = null;

            var lastClosedEndTsByZone = new Dictionary<string, long?>();

            foreach (var zoneName in usedZoneNames)
                lastClosedEndTsByZone[zoneName] = null;

            var pendingReferencesByKey = new Dictionary<string, ZoneSnapshot?>();

            foreach (var reference in references)
                pendingReferencesByKey[reference.Key] = null;

            var entryConditionPassedCounts = new int[entryConditions.Count];
            var entryConditionSkippedCounts = new int[entryConditions.Count];

            int compareOk = 0;
            int compareKo = 0;
            int skippedMissingReference = 0;
            int skippedDateMismatch = 0;
            int nbMaxWin = 0;
            int nbCurrentWin = 0;
            int nbMaxLoss = 0;
            int nbCurrentLoss = 0;

            static DateTime UtcFromNs(long tsNs)
            {
                long sec = tsNs / 1_000_000_000L;
                return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
            }

            static ZoneSnapshot ToSnapshot(SessionHighLowIndicator.Output output)
            {
                return new ZoneSnapshot(
                    output.Name,
                    UtcFromNs(output.LastEndTs).Date,
                    output.LastStartTs,
                    output.LastEndTs,
                    output.LastHigh,
                    output.LastLow);
            }

            void OnReferenceClosed(string referenceKey, ZoneSnapshot snapshot)
            {
                pendingReferencesByKey[referenceKey] = snapshot;

                if (enableVerboseDebug)
                {
                    Debug.WriteLine(
                        $"[REFERENCE CLOSED] " +
                        $"key={referenceKey} " +
                        $"zone={snapshot.Name} " +
                        $"date={snapshot.DateUtc:yyyy-MM-dd} " +
                        $"startTs={snapshot.StartTs} " +
                        $"endTs={snapshot.EndTs} " +
                        $"H={snapshot.High} " +
                        $"L={snapshot.Low} " +
                        $"M={snapshot.Mid} " +
                        $"R={snapshot.Range}");
                }
            }

            bool TryBuildContextForTarget(
                ZoneSnapshot targetSnapshot,
                out ConditionContext? context,
                out string failureReason)
            {
                var available = new Dictionary<string, ZoneSnapshot>();

                foreach (var kvp in pendingReferencesByKey)
                {
                    if (kvp.Value is null)
                    {
                        failureReason = $"MISSING_REFERENCE:{kvp.Key}";
                        context = null;
                        return false;
                    }

                    if (kvp.Value.DateUtc != targetSnapshot.DateUtc)
                    {
                        failureReason =
                            $"DATE_MISMATCH:{kvp.Key}:refDate={kvp.Value.DateUtc:yyyy-MM-dd}:targetDate={targetSnapshot.DateUtc:yyyy-MM-dd}";
                        context = null;
                        return false;
                    }

                    available[kvp.Key] = kvp.Value;
                }

                context = new ConditionContext(available, targetSnapshot);
                failureReason = string.Empty;
                return true;
            }

            void ProcessTargetClose(ZoneSnapshot targetSnapshot)
            {
                if (!TryBuildContextForTarget(targetSnapshot, out var context, out var failureReason))
                {
                    if (failureReason.StartsWith("MISSING_REFERENCE:", StringComparison.Ordinal))
                        skippedMissingReference++;
                    else if (failureReason.StartsWith("DATE_MISMATCH:", StringComparison.Ordinal))
                        skippedDateMismatch++;

                    if (enableVerboseDebug)
                    {
                        Debug.WriteLine(
                            $"[TARGET CLOSED - SKIP CONTEXT] " +
                            $"targetZone={targetSnapshot.Name} " +
                            $"date={targetSnapshot.DateUtc:yyyy-MM-dd} " +
                            $"reason={failureReason}");
                    }

                    return;
                }

                Assert.NotNull(context);

                bool allEntryConditionsPassed = true;

                for (int i = 0; i < entryConditions.Count; i++)
                {
                    var (conditionName, conditionTest) = entryConditions[i];
                    bool passed;

                    try
                    {
                        passed = conditionTest(context);
                    }
                    catch (KeyNotFoundException ex)
                    {
                        entryConditionSkippedCounts[i]++;
                        skippedMissingReference++;

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[SKIP {conditionName}] " +
                                $"date={targetSnapshot.DateUtc:yyyy-MM-dd} " +
                                $"reason=MISSING_REFERENCE_IN_CONDITION " +
                                $"error={ex.Message}");
                        }

                        allEntryConditionsPassed = false;
                        break;
                    }

                    if (!passed)
                    {
                        entryConditionSkippedCounts[i]++;

                        if (enableVerboseDebug)
                        {
                            string refsDump = string.Join(
                                " | ",
                                context.Refs.Select(r =>
                                    $"{r.Key}:{r.Value.Name}(H={r.Value.High},L={r.Value.Low},M={r.Value.Mid},R={r.Value.Range})"));

                            Debug.WriteLine(
                                $"[SKIP {conditionName}] " +
                                $"date={targetSnapshot.DateUtc:yyyy-MM-dd} " +
                                $"refs={refsDump} " +
                                $"target(H={context.Target.High},L={context.Target.Low},M={context.Target.Mid},R={context.Target.Range})");
                        }

                        allEntryConditionsPassed = false;
                        break;
                    }

                    entryConditionPassedCounts[i]++;
                }

                if (!allEntryConditionsPassed)
                    return;

                bool isOk;

                try
                {
                    isOk = finalCondition(context);
                }
                catch (KeyNotFoundException ex)
                {
                    skippedMissingReference++;

                    if (enableVerboseDebug)
                    {
                        Debug.WriteLine(
                            $"[FINAL SKIP] " +
                            $"date={targetSnapshot.DateUtc:yyyy-MM-dd} " +
                            $"reason=MISSING_REFERENCE_IN_FINAL_CONDITION " +
                            $"error={ex.Message}");
                    }

                    return;
                }

                if (isOk)
                {
                    compareOk++;
                    nbCurrentWin++;
                    nbCurrentLoss = 0;
                }
                else
                {
                    compareKo++;
                    nbCurrentWin = 0;
                    nbCurrentLoss++;
                }

                if (nbCurrentWin > nbMaxWin) nbMaxWin = nbCurrentWin;
                if (nbCurrentLoss > nbMaxLoss) nbMaxLoss = nbCurrentLoss;

                if (enableVerboseDebug)
                {
                    string passedConditions =
                        entryConditions.Count == 0
                            ? "NO ENTRY CONDITIONS"
                            : string.Join(" ", entryConditions.Select(c => $"{c.Name}=TRUE"));

                    string refsDump = string.Join(
                        " | ",
                        context.Refs.Select(r =>
                            $"{r.Key}:{r.Value.Name}(H={r.Value.High},L={r.Value.Low},M={r.Value.Mid},R={r.Value.Range})"));

                    Debug.WriteLine(
                        $"[FINAL COMPARE] " +
                        $"date={targetSnapshot.DateUtc:yyyy-MM-dd} " +
                        $"refs={refsDump} | " +
                        $"targetZone={context.Target.Name} H={context.Target.High} L={context.Target.Low} M={context.Target.Mid} R={context.Target.Range} | " +
                        $"{passedConditions} FINAL={(isOk ? "OK" : "KO")}");
                }
            }

            void ProcessCandle(global::BacktestApp.Controls.CandleChartControl.CandleIndex.CandleItem candle)
            {
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

                foreach (var zoneName in usedZoneNames)
                {
                    var indicator = zoneIndicators[zoneName];

                    indicator.OnCandle(
                        candle.Ts,
                        candle.O,
                        candle.H,
                        candle.L,
                        candle.C,
                        candle.V,
                        candle.Sym,
                        1.0);

                    var output = indicator.CurrentOutput;

                    if (!output.HasLast)
                        continue;

                    long? lastClosedEndTs = lastClosedEndTsByZone[zoneName];

                    if (lastClosedEndTs.HasValue && output.LastEndTs == lastClosedEndTs.Value)
                        continue;

                    lastClosedEndTsByZone[zoneName] = output.LastEndTs;

                    var snapshot = ToSnapshot(output);

                    foreach (var reference in references)
                    {
                        if (reference.ZoneName == zoneName)
                            OnReferenceClosed(reference.Key, snapshot);
                    }

                    if (zoneName == targetZoneName)
                    {
                        ProcessTargetClose(snapshot);
                    }
                }
            }

            // ==================================================
            // FENETRE INITIALE
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
            Debug.WriteLine($"targetZone={targetZoneName}");
            Debug.WriteLine($"references={string.Join(", ", references.Select(r => $"{r.Key}:{r.ZoneName}"))}");
            Debug.WriteLine($"filesRead={visitedFileIndexes.Count}/{expectedTotalFiles}");
            Debug.WriteLine($"candlesRead={totalCandlesRead}{(expectedTotalCandles.HasValue ? $"/{expectedTotalCandles.Value}" : "")}");

            for (int i = 0; i < entryConditions.Count; i++)
            {
                Debug.WriteLine($"{entryConditions[i].Name}Passed={entryConditionPassedCounts[i]}");
                Debug.WriteLine($"{entryConditions[i].Name}Skipped={entryConditionSkippedCounts[i]}");
            }

            Debug.WriteLine($"compareOk={compareOk}");
            Debug.WriteLine($"compareKo={compareKo}");
            Debug.WriteLine($"compareOkRate={(totalComparisons > 0 ? (double)compareOk / totalComparisons : 0):P2}");
            Debug.WriteLine($"maxConsecutiveWins={nbMaxWin}");
            Debug.WriteLine($"maxConsecutiveLosses={nbMaxLoss}");
            Debug.WriteLine($"totalComparisons={totalComparisons}");
            Debug.WriteLine($"skippedMissingReference={skippedMissingReference}");
            Debug.WriteLine($"skippedDateMismatch={skippedDateMismatch}");
            Debug.WriteLine("==================================================");

            Assert.True(
                totalComparisons > 0,
                "Aucune comparaison finale n'a été produite.");
        }
    }
}

