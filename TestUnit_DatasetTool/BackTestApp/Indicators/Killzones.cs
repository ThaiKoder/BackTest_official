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
                    new { Name = "Silver Bullet", Start = new TimeSpan(21, 0, 0), End = new TimeSpan(22, 0, 0) }
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


        private enum SweepSide
        {
            None = 0,
            High = 1,
            Low = 2
        }

        private enum SetupPhase
        {
            WaitingSweep = 0,
            WaitingIfvgCreation = 1,
            WaitingIfvgTouch = 2,
            WaitingOppositeExtremeTouch = 3,
            Done = 4,
            Failed = 5
        }

        private sealed class ActiveSilverBulletSetup
        {
            public DateTime SessionDateUtc { get; init; }

            public long SessionStartTs { get; init; }
            public long SessionEndTs { get; init; }
            public double SessionHigh { get; init; }
            public double SessionLow { get; init; }

            public SetupPhase Phase { get; set; } = SetupPhase.WaitingSweep;
            public SweepSide SweptSide { get; set; } = SweepSide.None;

            public long SweepTs { get; set; }
            public bool? ExpectedIfvgBullish { get; set; }

            public long IfvgAnchorTs { get; set; }
            public double IfvgLow { get; set; }
            public double IfvgHigh { get; set; }
            public long IfvgTouchTs { get; set; }

            // SL = extrême formé entre le sweep et le first IFVG
            public bool HasTrackedExtreme { get; set; }
            public double TrackedExtremePrice { get; set; }
            public long TrackedExtremeTs { get; set; }

            public bool HasStopLoss { get; set; }
            public double StopLossPrice { get; set; }
            public long StopLossTs { get; set; }
            public long StopLossHitTs { get; set; }

            public long OppositeExtremeTouchTs { get; set; }

            public string FailureReason { get; set; } = string.Empty;
        }

        [Fact]
        public void LoadNext_Should_Detect_SilverBullet_Sweep_Then_First_IFVG_Return_Then_Touch_Opposite_Extreme()
        {
            // ==================================================
            // CONFIG
            // ==================================================
            const bool enableVerboseDebug = true;
            const bool failPreviousSetupWhenNewSessionCloses = true;

            // ==================================================
            // CHART + PARCOURS GLOBAL DE TOUS LES FICHIERS
            // ==================================================
            var chart = new CandleChartControl();
            chart.Test_InitializeFilesAndCandlesMode();

            // ==================================================
            // INDICATEURS DE BACKTEST INDEPENDANTS
            // ==================================================
            var silverBullet = new SessionHighLowIndicator(
                "Silver Bullet",
                new TimeSpan(21, 0, 0),
                new TimeSpan(22, 0, 0));

            var fvg = new FvgIndicator("FVG");

            // ==================================================
            // ETAT
            // ==================================================
            var detectedSummaries = new List<string>();

            ActiveSilverBulletSetup? active = null;

            long lastClosedSilverBulletEndTs = 0;
            int lastProcessedFvgCount = 0;

            int totalCandlesProcessed = 0;
            int totalSteps = 0;
            int totalSessionsClosed = 0;

            int sweepDetectedCount = 0;
            int ifvgCreatedCount = 0;
            int ifvgTouchedCount = 0;
            int successCount = 0;
            int failureCount = 0;
            int stopLossFailureCount = 0;
            int unresolvedFailureCount = 0;
            int ambiguousFailureCount = 0;
            int replacedByNextSessionFailureCount = 0;

            int currentWinStreak = 0;
            int currentFailStreak = 0;
            int maxWinStreak = 0;
            int maxFailStreak = 0;

            var weekdayWins = new Dictionary<DayOfWeek, int>();
            var weekdayFails = new Dictionary<DayOfWeek, int>();

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                weekdayWins[day] = 0;
                weekdayFails[day] = 0;
            }

            int loadedFileSwitchCount = 0;
            int lastLoadedFileIdx = -1;

            // ==================================================
            // HELPERS
            // ==================================================
            static DateTime ToUtcDate(long tsNs)
            {
                long sec = tsNs / 1_000_000_000L;
                return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
            }

            static string FmtTs(long tsNs)
            {
                long sec = tsNs / 1_000_000_000L;
                return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }

            static bool CandleTouchesZone(long high, long low, double zoneLow, double zoneHigh)
            {
                double h = high;
                double l = low;
                return h >= zoneLow && l <= zoneHigh;
            }

            static string FormatWeekdayWinRate(int wins, int fails)
            {
                int total = wins + fails;
                double rate = total > 0 ? (double)wins / total : 0.0;
                return $"{wins}/{total} ({rate:P2})";
            }

            void RegisterSuccess(ActiveSilverBulletSetup setup)
            {
                successCount++;
                weekdayWins[setup.SessionDateUtc.DayOfWeek]++;

                currentWinStreak++;
                currentFailStreak = 0;

                if (currentWinStreak > maxWinStreak)
                    maxWinStreak = currentWinStreak;
            }

            void RegisterFailure(ActiveSilverBulletSetup setup, string reason)
            {
                failureCount++;
                weekdayFails[setup.SessionDateUtc.DayOfWeek]++;

                currentFailStreak++;
                currentWinStreak = 0;

                if (currentFailStreak > maxFailStreak)
                    maxFailStreak = currentFailStreak;

                if (reason.StartsWith("SL", StringComparison.OrdinalIgnoreCase))
                    stopLossFailureCount++;
                else if (reason.StartsWith("Fin des données", StringComparison.OrdinalIgnoreCase))
                    unresolvedFailureCount++;
                else if (reason.StartsWith("Même candle", StringComparison.OrdinalIgnoreCase))
                    ambiguousFailureCount++;
                else if (reason.StartsWith("Nouvelle Silver Bullet", StringComparison.OrdinalIgnoreCase))
                    replacedByNextSessionFailureCount++;
            }

            void FinalizeFailure(ActiveSilverBulletSetup setup, string reason)
            {
                if (setup.Phase == SetupPhase.Done || setup.Phase == SetupPhase.Failed)
                    return;

                setup.Phase = SetupPhase.Failed;
                setup.FailureReason = reason;

                RegisterFailure(setup, reason);

                string summary =
                    $"[FAIL] day={setup.SessionDateUtc:yyyy-MM-dd} ({setup.SessionDateUtc:dddd}) " +
                    $"SB[{FmtTs(setup.SessionStartTs)} -> {FmtTs(setup.SessionEndTs)}] " +
                    $"high={setup.SessionHigh} low={setup.SessionLow} " +
                    $"swept={setup.SweptSide} " +
                    $"sweepTs={(setup.SweepTs != 0 ? FmtTs(setup.SweepTs) : "n/a")} " +
                    $"ifvgAnchor={(setup.IfvgAnchorTs != 0 ? FmtTs(setup.IfvgAnchorTs) : "n/a")} " +
                    $"sl={(setup.HasStopLoss ? setup.StopLossPrice.ToString() : "n/a")} " +
                    $"reason={reason}";

                detectedSummaries.Add(summary);

                if (enableVerboseDebug)
                    Debug.WriteLine(summary);
            }

            void FinalizeSuccess(ActiveSilverBulletSetup setup)
            {
                if (setup.Phase == SetupPhase.Done || setup.Phase == SetupPhase.Failed)
                    return;

                setup.Phase = SetupPhase.Done;

                RegisterSuccess(setup);

                string summary =
                    $"[OK] day={setup.SessionDateUtc:yyyy-MM-dd} ({setup.SessionDateUtc:dddd}) " +
                    $"SB[{FmtTs(setup.SessionStartTs)} -> {FmtTs(setup.SessionEndTs)}] " +
                    $"high={setup.SessionHigh} low={setup.SessionLow} " +
                    $"swept={setup.SweptSide} sweepTs={FmtTs(setup.SweepTs)} " +
                    $"ifvg=[{setup.IfvgLow} -> {setup.IfvgHigh}] anchor={FmtTs(setup.IfvgAnchorTs)} " +
                    $"ifvgTouchTs={FmtTs(setup.IfvgTouchTs)} " +
                    $"sl={(setup.HasStopLoss ? setup.StopLossPrice.ToString() : "n/a")} " +
                    $"tpTs={FmtTs(setup.OppositeExtremeTouchTs)}";

                detectedSummaries.Add(summary);

                if (enableVerboseDebug)
                    Debug.WriteLine(summary);
            }

            void ProcessCandle(long ts, long open, long high, long low, long close, uint volume, byte sym)
            {
                totalCandlesProcessed++;

                // 1) feed indicateurs
                silverBullet.OnCandle(ts, open, high, low, close, volume, sym, 1.0);
                fvg.OnCandle(ts, open, high, low, close, volume, sym, 1.0);

                // 2) détecter fermeture d'une nouvelle Silver Bullet
                var sb = silverBullet.CurrentOutput;
                if (sb.HasLast &&
                    sb.State == SessionHighLowIndicator.SessionState.Out &&
                    sb.LastEndTs != 0 &&
                    sb.LastEndTs != lastClosedSilverBulletEndTs)
                {
                    totalSessionsClosed++;

                    if (active is not null &&
                        active.Phase != SetupPhase.Done &&
                        active.Phase != SetupPhase.Failed &&
                        failPreviousSetupWhenNewSessionCloses)
                    {
                        FinalizeFailure(active, "Nouvelle Silver Bullet clôturée avant résolution du setup précédent.");
                    }

                    active = new ActiveSilverBulletSetup
                    {
                        SessionDateUtc = ToUtcDate(sb.LastEndTs).Date,
                        SessionStartTs = sb.LastStartTs,
                        SessionEndTs = sb.LastEndTs,
                        SessionHigh = sb.LastHigh,
                        SessionLow = sb.LastLow,
                        Phase = SetupPhase.WaitingSweep
                    };

                    lastClosedSilverBulletEndTs = sb.LastEndTs;

                    if (enableVerboseDebug)
                    {
                        Debug.WriteLine(
                            $"[SB CLOSED] day={active.SessionDateUtc:yyyy-MM-dd} ({active.SessionDateUtc:dddd}) " +
                            $"start={FmtTs(active.SessionStartTs)} end={FmtTs(active.SessionEndTs)} " +
                            $"high={active.SessionHigh} low={active.SessionLow}");
                    }
                }

                // 3) récupérer le premier nouveau FVG compatible si on est en attente de création
                FvgIndicator.FvgZone? matchedNewIfvg = null;
                var zones = fvg.Zones;

                if (lastProcessedFvgCount < zones.Count)
                {
                    for (int i = lastProcessedFvgCount; i < zones.Count; i++)
                    {
                        var z = zones[i];

                        if (active is null)
                            continue;

                        if (active.Phase != SetupPhase.WaitingIfvgCreation)
                            continue;

                        if (z.AnchorTs <= active.SweepTs)
                            continue;

                        bool expectedBullish = active.ExpectedIfvgBullish ?? false;
                        if (z.IsBullish != expectedBullish)
                            continue;

                        matchedNewIfvg = z;
                        break;
                    }

                    lastProcessedFvgCount = zones.Count;
                }

                // 4) state machine du setup actif
                if (active is null)
                    return;

                // on ne traite que les candles après la fin de la session SB
                if (ts <= active.SessionEndTs)
                    return;

                // ---- Phase 1 : attendre sweep high ou low
                if (active.Phase == SetupPhase.WaitingSweep)
                {
                    bool breaksHigh = high > active.SessionHigh;
                    bool breaksLow = low < active.SessionLow;

                    if (breaksHigh && breaksLow)
                    {
                        FinalizeFailure(active, "Même candle casse le high et le low de la Silver Bullet, séquence ambiguë.");
                        return;
                    }

                    if (breaksHigh)
                    {
                        active.SweptSide = SweepSide.High;
                        active.SweepTs = ts;
                        active.ExpectedIfvgBullish = false; // après prise du high, on attend un IFVG bearish
                        active.Phase = SetupPhase.WaitingIfvgCreation;

                        active.HasTrackedExtreme = true;
                        active.TrackedExtremePrice = high;
                        active.TrackedExtremeTs = ts;

                        sweepDetectedCount++;

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[SWEEP HIGH] day={active.SessionDateUtc:yyyy-MM-dd} " +
                                $"ts={FmtTs(ts)} SBhigh={active.SessionHigh}");
                        }

                        return;
                    }

                    if (breaksLow)
                    {
                        active.SweptSide = SweepSide.Low;
                        active.SweepTs = ts;
                        active.ExpectedIfvgBullish = true; // après prise du low, on attend un IFVG bullish
                        active.Phase = SetupPhase.WaitingIfvgCreation;

                        active.HasTrackedExtreme = true;
                        active.TrackedExtremePrice = low;
                        active.TrackedExtremeTs = ts;

                        sweepDetectedCount++;

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[SWEEP LOW] day={active.SessionDateUtc:yyyy-MM-dd} " +
                                $"ts={FmtTs(ts)} SBlow={active.SessionLow}");
                        }

                        return;
                    }

                    return;
                }

                // ---- Phase 2 : attendre la création du first IFVG + tracker l'extrême du futur SL
                if (active.Phase == SetupPhase.WaitingIfvgCreation)
                {
                    if (active.SweptSide == SweepSide.High)
                    {
                        if (!active.HasTrackedExtreme || high > active.TrackedExtremePrice)
                        {
                            active.HasTrackedExtreme = true;
                            active.TrackedExtremePrice = high;
                            active.TrackedExtremeTs = ts;
                        }
                    }
                    else if (active.SweptSide == SweepSide.Low)
                    {
                        if (!active.HasTrackedExtreme || low < active.TrackedExtremePrice)
                        {
                            active.HasTrackedExtreme = true;
                            active.TrackedExtremePrice = low;
                            active.TrackedExtremeTs = ts;
                        }
                    }

                    if (matchedNewIfvg is not null)
                    {
                        active.IfvgAnchorTs = matchedNewIfvg.AnchorTs;
                        active.IfvgLow = matchedNewIfvg.Low;
                        active.IfvgHigh = matchedNewIfvg.High;

                        if (active.HasTrackedExtreme)
                        {
                            active.HasStopLoss = true;
                            active.StopLossPrice = active.TrackedExtremePrice;
                            active.StopLossTs = active.TrackedExtremeTs;
                        }

                        active.Phase = SetupPhase.WaitingIfvgTouch;
                        ifvgCreatedCount++;

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[IFVG CREATED] day={active.SessionDateUtc:yyyy-MM-dd} " +
                                $"expectedBullish={active.ExpectedIfvgBullish} " +
                                $"anchor={FmtTs(active.IfvgAnchorTs)} " +
                                $"zone=[{active.IfvgLow} -> {active.IfvgHigh}] " +
                                $"sl={(active.HasStopLoss ? active.StopLossPrice.ToString() : "n/a")}");
                        }
                    }

                    return;
                }

                // ---- Phase 3 : IFVG créé, attendre retour dans la zone
                if (active.Phase == SetupPhase.WaitingIfvgTouch)
                {
                    if (CandleTouchesZone(high, low, active.IfvgLow, active.IfvgHigh))
                    {
                        active.IfvgTouchTs = ts;
                        active.Phase = SetupPhase.WaitingOppositeExtremeTouch;
                        ifvgTouchedCount++;

                        if (enableVerboseDebug)
                        {
                            Debug.WriteLine(
                                $"[IFVG TOUCH] day={active.SessionDateUtc:yyyy-MM-dd} " +
                                $"ts={FmtTs(ts)} zone=[{active.IfvgLow} -> {active.IfvgHigh}]");
                        }
                    }

                    return;
                }

                // ---- Phase 4 : après retour IFVG, SL d'abord = FAIL, TP ensuite = SUCCESS
                if (active.Phase == SetupPhase.WaitingOppositeExtremeTouch)
                {
                    // SL = FAIL (même ordre que l'indicateur)
                    if (active.HasStopLoss)
                    {
                        if (active.SweptSide == SweepSide.High)
                        {
                            // setup vendeur : SL au-dessus
                            if (high >= active.StopLossPrice)
                            {
                                active.StopLossHitTs = ts;
                                FinalizeFailure(active, $"SL touché à {FmtTs(ts)}");
                                return;
                            }
                        }
                        else if (active.SweptSide == SweepSide.Low)
                        {
                            // setup acheteur : SL en-dessous
                            if (low <= active.StopLossPrice)
                            {
                                active.StopLossHitTs = ts;
                                FinalizeFailure(active, $"SL touché à {FmtTs(ts)}");
                                return;
                            }
                        }
                    }

                    // TP = SUCCESS
                    if (active.SweptSide == SweepSide.High)
                    {
                        if (low <= active.SessionLow)
                        {
                            active.OppositeExtremeTouchTs = ts;
                            FinalizeSuccess(active);
                        }

                        return;
                    }

                    if (active.SweptSide == SweepSide.Low)
                    {
                        if (high >= active.SessionHigh)
                        {
                            active.OppositeExtremeTouchTs = ts;
                            FinalizeSuccess(active);
                        }

                        return;
                    }
                }
            }

            // ==================================================
            // 1) TRAITER LE PREMIER BATCH DEJA CHARGE
            // ==================================================
            lastLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
            if (lastLoadedFileIdx != -1)
                loadedFileSwitchCount = 1;

            foreach (var candle in chart.Test_GetLastAddedCandles())
            {
                ProcessCandle(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }

            // ==================================================
            // 2) PARCOURS COMPLET DE TOUS LES FICHIERS / CANDLES
            // ==================================================
            while (chart.Test_AdvanceCandlesNext())
            {
                totalSteps++;

                int currentLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
                if (currentLoadedFileIdx != -1 && currentLoadedFileIdx != lastLoadedFileIdx)
                {
                    loadedFileSwitchCount++;
                    lastLoadedFileIdx = currentLoadedFileIdx;
                }

                var added = chart.Test_GetLastAddedCandles();
                for (int i = 0; i < added.Count; i++)
                {
                    var candle = added[i];

                    ProcessCandle(
                        candle.Ts,
                        candle.O,
                        candle.H,
                        candle.L,
                        candle.C,
                        candle.V,
                        candle.Sym);
                }
            }

            // ==================================================
            // FIN : si un setup reste ouvert, on le clôture en échec
            // ==================================================
            if (active is not null &&
                active.Phase != SetupPhase.Done &&
                active.Phase != SetupPhase.Failed)
            {
                FinalizeFailure(active, "Fin des données avant complétion du setup.");
            }

            // ==================================================
            // DEBUG FINAL
            // ==================================================
            int resolvedCount = successCount + failureCount;
            double globalWinRate = resolvedCount > 0 ? (double)successCount / resolvedCount : 0.0;

            Debug.WriteLine("==================================================");
            Debug.WriteLine("SILVER BULLET -> SWEEP -> FIRST IFVG -> TOUCH IFVG -> TP/SL");
            Debug.WriteLine("==================================================");
            Debug.WriteLine($"Files loaded                 : {loadedFileSwitchCount}");
            Debug.WriteLine($"Steps processed              : {totalSteps}");
            Debug.WriteLine($"Candles processed            : {totalCandlesProcessed}");
            Debug.WriteLine($"SB sessions closed           : {totalSessionsClosed}");
            Debug.WriteLine($"Sweeps detected              : {sweepDetectedCount}");
            Debug.WriteLine($"IFVG created                 : {ifvgCreatedCount}");
            Debug.WriteLine($"IFVG touched                 : {ifvgTouchedCount}");
            Debug.WriteLine($"Success count                : {successCount}");
            Debug.WriteLine($"Failure count                : {failureCount}");
            Debug.WriteLine($"Global win rate              : {globalWinRate:P2}");
            Debug.WriteLine($"SL failure count             : {stopLossFailureCount}");
            Debug.WriteLine($"Unresolved failure count     : {unresolvedFailureCount}");
            Debug.WriteLine($"Ambiguous failure count      : {ambiguousFailureCount}");
            Debug.WriteLine($"Next session failure count   : {replacedByNextSessionFailureCount}");
            Debug.WriteLine($"Max consecutive wins         : {maxWinStreak}");
            Debug.WriteLine($"Max consecutive fails        : {maxFailStreak}");
            Debug.WriteLine("--------------------------------------------------");
            Debug.WriteLine("WIN RATE PAR JOUR");
            Debug.WriteLine($"Monday    : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Monday], weekdayFails[DayOfWeek.Monday])}");
            Debug.WriteLine($"Tuesday   : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Tuesday], weekdayFails[DayOfWeek.Tuesday])}");
            Debug.WriteLine($"Wednesday : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Wednesday], weekdayFails[DayOfWeek.Wednesday])}");
            Debug.WriteLine($"Thursday  : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Thursday], weekdayFails[DayOfWeek.Thursday])}");
            Debug.WriteLine($"Friday    : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Friday], weekdayFails[DayOfWeek.Friday])}");
            Debug.WriteLine($"Saturday  : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Saturday], weekdayFails[DayOfWeek.Saturday])}");
            Debug.WriteLine($"Sunday    : {FormatWeekdayWinRate(weekdayWins[DayOfWeek.Sunday], weekdayFails[DayOfWeek.Sunday])}");
            Debug.WriteLine("==================================================");

            foreach (var line in detectedSummaries)
                Debug.WriteLine(line);

            // ==================================================
            // ASSERTS MINIMUMS
            // ==================================================
            Assert.True(totalCandlesProcessed > 0, "Aucune candle parcourue.");
            Assert.True(totalSessionsClosed > 0, "Aucune session Silver Bullet clôturée détectée.");
            Assert.True(resolvedCount > 0, "Aucun setup Silver Bullet résolu.");
            Assert.Equal(resolvedCount, weekdayWins.Values.Sum() + weekdayFails.Values.Sum());
            Assert.True(maxWinStreak <= resolvedCount, "maxWinStreak invalide.");
            Assert.True(maxFailStreak <= resolvedCount, "maxFailStreak invalide.");
        }
    }
}

