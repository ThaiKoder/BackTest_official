using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorNext
{
    //Teste minimal pour que loadNext() avance correctement le step des candles et que les timestamps chargés changent en conséquence.
    [Fact]
    public void LoadNext_NewSequence_Should_Advance_UiCandleStep_And_Change_LoadedCandles()
    {
        // Arrange
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_InitializeFilesAndCandlesMode();

        int beforeCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int beforeNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var beforeTs = chart.Test_GetLoadedTimestamps();

        Assert.True(beforeCurrentIdx >= 0, $"CurrentIdx initial invalide: {beforeCurrentIdx}");
        Assert.True(beforeNextCursorIdx != -1, "Le curseur suivant est déjà à la fin du fichier.");
        Assert.NotNull(beforeTs);
        Assert.NotEmpty(beforeTs);

        // Act
        chart.loadNext();   // même chemin que le bouton UI

        int afterCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int afterNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var afterTs = chart.Test_GetLoadedTimestamps();

        // Assert
        Assert.True(afterCurrentIdx >= 0, $"CurrentIdx après avance invalide: {afterCurrentIdx}");
        Assert.NotNull(afterTs);
        Assert.NotEmpty(afterTs);

        Assert.NotEqual(
            beforeCurrentIdx,
            afterCurrentIdx);

        Assert.False(
            beforeTs.SequenceEqual(afterTs),
            $"Les candles chargées doivent changer. " +
            $"beforeCurrentIdx={beforeCurrentIdx}, afterCurrentIdx={afterCurrentIdx}, " +
            $"beforeFirstTs={beforeTs[0]}, beforeLastTs={beforeTs[^1]}, " +
            $"afterFirstTs={afterTs[0]}, afterLastTs={afterTs[^1]}");

        Assert.True(
            beforeTs[0] != afterTs[0] || beforeTs[^1] != afterTs[^1],
            $"La fenêtre doit changer au minimum en début ou en fin. " +
            $"beforeFirstTs={beforeTs[0]}, beforeLastTs={beforeTs[^1]}, " +
            $"afterFirstTs={afterTs[0]}, afterLastTs={afterTs[^1]}");

        // contrôle bonus : cohérence du curseur suivant
        Assert.True(
            afterNextCursorIdx == -1 ||
            afterNextCursorIdx != beforeNextCursorIdx ||
            afterCurrentIdx != beforeCurrentIdx,
            $"Le step ne semble pas avoir avancé correctement. " +
            $"beforeNext={beforeNextCursorIdx}, afterNext={afterNextCursorIdx}, " +
            $"beforeCurrent={beforeCurrentIdx}, afterCurrent={afterCurrentIdx}");
    }

    [Fact]
    public void LoadNext_Should_Traverse_All_Candles_Of_All_Index_Files()
    {
        // Arrange
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        // Référence métier hors chemin UI :
        // somme exacte de toutes les candles de tous les fichiers
        chart.Test_LoadIndexFile("data/bin/_index.bin");

        long expectedTotalFiles = chart.Test_IndexCount;
        long expectedTotalCandles = 0;

        for (int fileIdx = 0; fileIdx < expectedTotalFiles; fileIdx++)
        {
            chart.Test_CandlesLoadFromCurrentFileIndex(fileIdx);
            expectedTotalCandles += chart.Test_CandleCount;
        }

        Assert.True(expectedTotalFiles > 0, "L'index doit contenir au moins un fichier.");
        Assert.True(expectedTotalCandles > 0, "Le nombre total de candles attendu doit être > 0.");

        // Vrai chemin UI
        chart.Test_InitializeFilesAndCandlesMode();

        int initialFileIdx = chart.Test_GetUiLoadedFileIdx();
        int initialCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int initialNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var initialWindow = chart.Test_GetLoadedTimestamps().ToArray();

        Assert.True(initialFileIdx >= 0, $"FileIdx initial invalide: {initialFileIdx}");
        Assert.True(initialCurrentIdx >= 0, $"CurrentIdx initial invalide: {initialCurrentIdx}");
        Assert.NotNull(initialWindow);
        Assert.NotEmpty(initialWindow);

        // Vérifie que la fenêtre courante est strictement croissante
        static void AssertStrictlyIncreasing(IReadOnlyList<long> ts, string messagePrefix)
        {
            for (int i = 1; i < ts.Count; i++)
            {
                Assert.True(
                    ts[i] > ts[i - 1],
                    $"{messagePrefix} timestamps non strictement croissants à i={i}. prev={ts[i - 1]}, cur={ts[i]}");
            }
        }

        AssertStrictlyIncreasing(initialWindow, "[INITIAL]");

        var seenFiles = new HashSet<int> { initialFileIdx };
        var allSeenTs = new HashSet<long>();

        long? previousGlobalTs = null;
        long totalCandlesRead = 0;

        // La fenêtre initiale représente déjà des candles vues
        foreach (var ts in initialWindow)
        {
            if (previousGlobalTs.HasValue)
            {
                Assert.True(
                    ts > previousGlobalTs.Value,
                    $"[INITIAL] timestamp non croissant. current={ts} <= previous={previousGlobalTs.Value}");
            }

            Assert.True(
                allSeenTs.Add(ts),
                $"[INITIAL] timestamp dupliqué détecté: {ts}");

            previousGlobalTs = ts;
            totalCandlesRead++;
        }

        int iterations = 0;

        int previousFileIdx = initialFileIdx;
        int previousCurrentIdx = initialCurrentIdx;
        var previousWindow = initialWindow;

        // Act
        while (chart.Test_AdvanceCandlesNext())
        {
            iterations++;

            int currentFileIdx = chart.Test_GetUiLoadedFileIdx();
            int currentIdx = chart.Test_GetUiCandleCurrentIdx();
            int nextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();

            var currentWindow = chart.Test_GetLoadedTimestamps().ToArray();
            var addedTs = chart.Test_GetLastAddedTimestamps().ToArray();
            var removedTs = chart.Test_GetLastRemovedTimestamps().ToArray();

            int removedCount = chart.Test_GetLastRemovedCount();
            int addedCount = chart.Test_GetLastAddedCount();

            Assert.True(currentFileIdx >= 0, $"FileIdx invalide après loadNext: {currentFileIdx}");
            Assert.True(currentIdx >= 0, $"CurrentIdx invalide après loadNext: {currentIdx}");
            Assert.NotNull(currentWindow);
            Assert.NotEmpty(currentWindow);

            seenFiles.Add(currentFileIdx);

            // 1) La fenêtre courante doit être strictement croissante
            AssertStrictlyIncreasing(currentWindow, $"[STEP {iterations}] fileIdx={currentFileIdx}");

            // 2) Cohérence counts <-> timestamps exposés par Added/Removed
            Assert.Equal(removedCount, removedTs.Length);
            Assert.Equal(addedCount, addedTs.Length);

            // 3) Si on reste dans le même fichier :
            //    - le currentIdx doit avancer
            //    - la fenêtre doit shifter à gauche
            //    - seule la partie de droite doit être append
            if (currentFileIdx == previousFileIdx)
            {
                Assert.True(
                    currentIdx > previousCurrentIdx,
                    $"Le curseur candle doit avancer dans le même fichier. " +
                    $"fileIdx={currentFileIdx}, before={previousCurrentIdx}, after={currentIdx}");

                Assert.True(
                    removedCount >= 0,
                    $"removedCount invalide: {removedCount}");

                Assert.True(
                    addedCount >= 0,
                    $"addedCount invalide: {addedCount}");

                int remain = previousWindow.Length - removedCount;
                Assert.True(
                    remain >= 0,
                    $"remain invalide. previousWindow.Length={previousWindow.Length}, removedCount={removedCount}");

                // Les removed doivent correspondre au préfixe qui sort
                for (int i = 0; i < removedTs.Length; i++)
                {
                    Assert.Equal(
                        previousWindow[i],
                        removedTs[i]);
                }

                // La partie restante doit être conservée à gauche
                for (int i = 0; i < remain; i++)
                {
                    Assert.Equal(
                        previousWindow[i + removedCount],
                        currentWindow[i]);
                }

                // La partie appendée à droite doit correspondre exactement à Added
                var appendedPart = currentWindow.Skip(remain).ToArray();

                Assert.Equal(addedTs.Length, appendedPart.Length);

                for (int i = 0; i < addedTs.Length; i++)
                {
                    Assert.Equal(
                        addedTs[i],
                        appendedPart[i]);
                }
            }
            else
            {
                // 4) Si on change de fichier :
                //    - le fileIdx doit avancer
                //    - on repart sur un currentIdx valide
                Assert.True(
                    currentFileIdx > previousFileIdx,
                    $"Le fileIdx doit avancer strictement. before={previousFileIdx}, after={currentFileIdx}");

                Assert.True(
                    currentIdx >= 0,
                    $"Le curseur du nouveau fichier doit être valide. fileIdx={currentFileIdx}, currentIdx={currentIdx}");

                // Sur changement de fichier, on accepte un rechargement complet de fenêtre.
                // On impose juste que Added représente bien toutes les nouvelles candles visibles.
                Assert.True(
                    addedTs.Length > 0,
                    $"Le changement de fichier doit charger au moins une candle. fileIdx={currentFileIdx}");
            }

            // 5) Vérifie que CHAQUE nouvelle candle ajoutée :
            //    - est strictement > à la précédente globalement
            //    - n'est pas un doublon
            foreach (var ts in addedTs)
            {
                if (previousGlobalTs.HasValue)
                {
                    Assert.True(
                        ts > previousGlobalTs.Value,
                        $"Timestamp non croissant sur les nouvelles candles ajoutées. " +
                        $"current={ts} <= previous={previousGlobalTs.Value}, fileIdx={currentFileIdx}, currentIdx={currentIdx}");
                }

                Assert.True(
                    allSeenTs.Add(ts),
                    $"Timestamp dupliqué détecté dans les candles ajoutées: {ts}, fileIdx={currentFileIdx}, currentIdx={currentIdx}");

                previousGlobalTs = ts;
                totalCandlesRead++;
            }

            previousFileIdx = currentFileIdx;
            previousCurrentIdx = currentIdx;
            previousWindow = currentWindow;

            Assert.True(
                iterations <= expectedTotalCandles,
                $"Boucle suspecte: trop d'itérations. iterations={iterations}, expectedTotalCandles={expectedTotalCandles}, next={nextCursorIdx}");
        }

        // Assert final
        Assert.True(iterations > 0, "Le test doit effectuer au moins un loadNext().");

        int finalLoadedFileIdx = chart.Test_GetUiLoadedFileIdx();
        int finalCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int finalNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var finalWindow = chart.Test_GetLoadedTimestamps().ToArray();

        Assert.True(finalLoadedFileIdx >= 0, $"FileIdx final invalide: {finalLoadedFileIdx}");
        Assert.True(finalCurrentIdx >= 0, $"CurrentIdx final invalide: {finalCurrentIdx}");
        Assert.Equal(-1, finalNextCursorIdx);

        Assert.NotNull(finalWindow);
        Assert.NotEmpty(finalWindow);
        AssertStrictlyIncreasing(finalWindow, "[FINAL]");

        // Tous les fichiers de l'index doivent avoir été vus
        Assert.Equal(expectedTotalFiles, seenFiles.Count);

        // Toutes les candles doivent avoir été lues exactement une fois
        Assert.Equal(expectedTotalCandles, totalCandlesRead);
        Assert.Equal(expectedTotalCandles, allSeenTs.Count);

        // Appel supplémentaire après fin => no-op
        chart.loadNext();

        Assert.Equal(finalLoadedFileIdx, chart.Test_GetUiLoadedFileIdx());
        Assert.Equal(finalCurrentIdx, chart.Test_GetUiCandleCurrentIdx());
        Assert.Equal(-1, chart.Test_GetUiCandleNextCursorIdx());
        Assert.True(
            finalWindow.SequenceEqual(chart.Test_GetLoadedTimestamps()),
            "Après la fin du dernier fichier, loadNext() ne doit plus modifier la fenêtre.");
    }
    [Fact]
    public void LoadNext_Should_Shift_Left_And_Append_Only_New_Candles()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        chart.Test_InitializeFilesAndCandlesMode();

        var before = chart.Test_GetLoadedTimestamps().ToArray();
        Assert.NotEmpty(before);

        chart.loadNext();

        var after = chart.Test_GetLoadedTimestamps().ToArray();
        Assert.NotEmpty(after);

        // Dans ton cas range=3 => step=4, mais au début du fichier
        // Removed peut être plus petit que Added.
        int removed = chart.Test_GetLastRemovedCount();
        int added = chart.Test_GetLastAddedCount();

        Assert.True(removed >= 0);
        Assert.True(added >= 0);

        int remain = before.Length - removed;
        Assert.True(remain >= 0);

        for (int i = 0; i < remain; i++)
        {
            Assert.Equal(before[i + removed], after[i]);
        }
    }
}
