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
            int nbMaxWin = 0;
            int nbCurrentWin = 0;
            int nbMaxLoss = 0;
            int nbCurrentLoss = 0;

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

                        bool isOk = a.LastLow < (m.LastHigh - (m.LastHigh - m.LastLow));

                        if (isOk) {
                            compareOk++;
                            nbCurrentWin++;
                            nbCurrentLoss = 0;
                        } else
                        {
                            compareKo++;
                            nbCurrentWin = 0;
                            nbCurrentLoss++;
                        }

                        if (nbCurrentWin > nbMaxWin) nbMaxWin = nbCurrentWin;

                        if (nbCurrentLoss > nbMaxLoss) nbMaxLoss = nbCurrentLoss;




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

            const bool enableExactExpectedCandleCount = false;    // false = plus rapide
            const bool enableStrictUniqueTimestampCheck = false;  // false = encore plus rapide
            const bool enableVerboseDebug = false;

            // ==================================================
            // REFERENCES DYNAMIQUES
            // ==================================================
            // Tu peux mettre 1, 2, 10, 100 références...
            var references = new List<ReferenceConfig>
        {
            new("refAsian", "Asian"),
            new("refLondon", "London"),
            new("refLondon-NY AM", "Between London - NY AM")

            // Exemples si tu as d'autres zones dans ton indicateur :
            // new("ref4", "Asian"),
            // new("ref5", "London"),
            // new("ref6", "NY AM"),
        };

            // ==================================================
            // CONDITIONS IMBRIQUEES
            // ==================================================
            // Elles reçoivent TOUTES les références + la target
            // Donc tu peux faire :
            // refs["ref1"].Low > refs["ref2"].Low && refs["ref3"].Low > target.Low
            var entryConditions = new List<(string Name, Func<ConditionContext, bool> Test)>
        {

            //("C3", ctx => ctx.Refs["refAsian"].Range < ctx.Refs["refLondon"].Range),
            //("C4", ctx => ctx.Refs["refAsian"].Low > ctx.Refs["refLondon-NY AM"].Low)

            // Exemples :
            // ("C2", ctx => ctx.Refs["ref1"].High > ctx.Refs["ref2"].High),
            // ("C3", ctx => ctx.Target.Mid > ctx.Refs["ref1"].Mid),
            // ("C4", ctx => ctx.Target.Range > ctx.Refs["ref2"].Range),
            // ("C5", ctx => ctx.Refs["ref1"].Low > ctx.Refs["ref2"].Low && ctx.Refs["ref3"].Low > ctx.Target.Low),
        };

            // Dernière condition = seule qui compte compareOk / compareKo
            Func<ConditionContext, bool> finalCondition =
                ctx => ((ctx.Refs["refAsian"].Low >= ctx.Target.Low) && ((ctx.Refs["refAsian"].Low - ctx.Refs["refAsian"].Range) >= ctx.Target.Low));

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

            // Petit helper pour extraire les refs utilisées dans les conditions
            static IEnumerable<string> ExtractReferenceKeysFromConditions(
                IEnumerable<(string Name, Func<ConditionContext, bool> Test)> conditions,
                IEnumerable<string> knownKeys)
            {
                // Ici on ne peut pas introspecter le code du lambda.
                // Donc on valide dynamiquement plus tard quand on construit le contexte.
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

            var zoneConfigs = chart.Test_GetSessionZoneConfigs();

            var usedZoneNames = references
                .Select(x => x.ZoneName)
                .Append(targetZoneName)
                .Distinct()
                .ToArray();

            foreach (var zoneName in usedZoneNames)
            {
                Assert.Contains(zoneConfigs, z => z.Name == zoneName);
            }

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

            // Dernière référence fermée par key
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

                // ==================================================
                // CONDITIONS IMBRIQUEES
                // ==================================================
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

                // ==================================================
                // DERNIERE CONDITION = COMPTE OK / KO
                // ==================================================
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
                // ==================================================
                // 1) CONTROLES GLOBAUX
                // ==================================================
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

                // ==================================================
                // 2) FEED DE TOUTES LES ZONES UTILISEES
                // ==================================================
                foreach (var zoneName in usedZoneNames)
                {
                    var indicator = zoneIndicators[zoneName];
                    var output = indicator.OnCandle(candle.Ts, candle.H, candle.L, 1.0);

                    if (output is null || !output.HasLast)
                        continue;

                    long? lastClosedEndTs = lastClosedEndTsByZone[zoneName];

                    if (lastClosedEndTs.HasValue && output.LastEndTs == lastClosedEndTs.Value)
                        continue;

                    lastClosedEndTsByZone[zoneName] = output.LastEndTs;

                    var snapshot = ToSnapshot(output);

                    // Si cette zone sert de référence, on met à jour toutes les clés liées à cette zone
                    foreach (var reference in references)
                    {
                        if (reference.ZoneName == zoneName)
                            OnReferenceClosed(reference.Key, snapshot);
                    }

                    // Si cette zone est la cible, on déclenche l'évaluation
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

