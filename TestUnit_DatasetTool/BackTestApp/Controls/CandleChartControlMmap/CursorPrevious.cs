using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorPrevious
{
    [Fact]
    public void Test_CandlesPrevious_1_file()
    {
        // ==================================================
        // ARRANGE
        // ==================================================
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        string filePath = Path.Combine(
            "data",
            "bin",
            "glbx-mdp3-20100606-20100612.ohlcv-1m.bin");

        var candleIndex = chart.Test_candleReader();
        candleIndex.Load(filePath);

        long expectedTotalCandles = candleIndex.Count;

        Assert.True(
            expectedTotalCandles > 0,
            $"Le fichier doit contenir au moins une candle: {filePath}");

        int range = 10;
        int stepSize = range + 1;

        int startCursorIdx = (int)expectedTotalCandles - 1;

        var initialStep = candleIndex.CandlesPrevious(startCursorIdx, range);

        Assert.Equal(startCursorIdx, initialStep.CurrentIdx);
        Assert.NotNull(initialStep.Window);
        Assert.NotEmpty(initialStep.Window);

        // Fenêtre logique = toujours triée du plus ancien au plus récent
        static void AssertStrictlyIncreasing(IReadOnlyList<long> ts, string prefix)
        {
            for (int i = 1; i < ts.Count; i++)
            {
                Assert.True(
                    ts[i] > ts[i - 1],
                    $"{prefix} timestamps non strictement croissants à i={i}. prev={ts[i - 1]}, cur={ts[i]}");
            }
        }

        var initialWindowTs = initialStep.Window
            .Where(c => c.Idx != -1)
            .Select(c => c.Ts)
            .ToArray();

        Assert.NotEmpty(initialWindowTs);
        AssertStrictlyIncreasing(initialWindowTs, "[INITIAL WINDOW]");

        // La dernière candle réelle doit être visible au départ
        Assert.Contains(initialStep.Window, c => c.Idx == startCursorIdx);

        // Toutes les candles de la fenêtre initiale sont "vues"
        var seenTs = new HashSet<long>();
        foreach (var ts in initialWindowTs)
        {
            Assert.True(
                seenTs.Add(ts),
                $"[INITIAL] doublon détecté dans la fenêtre initiale: {ts}");
        }

        long totalCandlesRead = initialWindowTs.Length;

        // En parcours reverse, chaque nouveau timestamp ajouté doit être strictement plus petit
        // que le plus petit déjà vu jusque-là.
        long currentSmallestSeenTs = initialWindowTs[0];

        int previousCursorIdx = initialStep.CurrentIdx;
        var previousWindow = initialStep.Window
            .Where(c => c.Idx != -1)
            .Select(c => c.Ts)
            .ToArray();

        int iterations = 0;
        var step = initialStep;

        // ==================================================
        // ACT
        // ==================================================
        while (step.PreviousCursorIdx != -1)
        {
            iterations++;

            step = candleIndex.CandlesPrevious(step.PreviousCursorIdx, range);

            Assert.True(step.CurrentIdx >= 0, $"CurrentIdx invalide à l'itération {iterations}");
            Assert.NotNull(step.Window);
            Assert.NotEmpty(step.Window);

            var currentWindow = step.Window
                .Where(c => c.Idx != -1)
                .Select(c => c.Ts)
                .ToArray();

            var addedTs = step.Added
                .Where(c => c.Idx != -1)
                .Select(c => c.Ts)
                .ToArray();

            var removedTs = step.Removed
                .Where(c => c.Idx != -1)
                .Select(c => c.Ts)
                .ToArray();

            Assert.NotEmpty(currentWindow);
            AssertStrictlyIncreasing(currentWindow, $"[STEP {iterations}]");

            // ==================================================
            // 1) Le curseur recule bien
            // ==================================================
            Assert.True(
                step.CurrentIdx < previousCursorIdx,
                $"Le curseur previous doit reculer. before={previousCursorIdx}, after={step.CurrentIdx}");

            // ==================================================
            // 2) Pas de doublons dans la fenêtre courante
            // ==================================================
            for (int i = 1; i < currentWindow.Length; i++)
            {
                Assert.True(
                    currentWindow[i] != currentWindow[i - 1],
                    $"Doublon local dans la fenêtre à i={i}. ts={currentWindow[i]}");
            }

            // ==================================================
            // 3) Vérifie le shift RIGHT + prepend ONLY NEW
            //    previous = on enlève à droite, on ajoute à gauche
            // ==================================================
            int removedCount = removedTs.Length;
            int addedCount = addedTs.Length;

            Assert.True(removedCount >= 0);
            Assert.True(addedCount >= 0);

            int remain = previousWindow.Length - removedCount;
            Assert.True(
                remain >= 0,
                $"remain invalide. previousWindow.Length={previousWindow.Length}, removedCount={removedCount}");

            // Les removed doivent correspondre au suffixe qui sort à droite
            for (int i = 0; i < removedCount; i++)
            {
                Assert.Equal(
                    previousWindow[remain + i],
                    removedTs[i]);
            }

            // La partie prependée à gauche doit correspondre exactement à Added
            Assert.True(
                addedCount <= currentWindow.Length,
                $"addedCount invalide. addedCount={addedCount}, currentWindow.Length={currentWindow.Length}");

            for (int i = 0; i < addedCount; i++)
            {
                Assert.Equal(
                    addedTs[i],
                    currentWindow[i]);
            }

            // La partie restante doit être conservée à droite
            for (int i = 0; i < remain; i++)
            {
                Assert.Equal(
                    previousWindow[i],
                    currentWindow[addedCount + i]);
            }

            // ==================================================
            // 4) Chaque nouvelle candle ajoutée est bien plus ancienne
            //    que tout ce qu'on avait déjà vu
            // ==================================================
            foreach (var ts in addedTs)
            {
                Assert.True(
                    ts < currentSmallestSeenTs,
                    $"En parcours previous, chaque nouvelle candle doit être plus ancienne. " +
                    $"addedTs={ts}, currentSmallestSeenTs={currentSmallestSeenTs}");

                Assert.True(
                    seenTs.Add(ts),
                    $"Doublon global détecté pendant le parcours previous: {ts}");

                currentSmallestSeenTs = ts;
                totalCandlesRead++;
            }

            previousCursorIdx = step.CurrentIdx;
            previousWindow = currentWindow;

            Assert.True(
                iterations <= expectedTotalCandles,
                $"Boucle suspecte: trop d'itérations. iterations={iterations}, expectedTotalCandles={expectedTotalCandles}");
        }

        // ==================================================
        // ASSERT FINAL
        // ==================================================
        Assert.True(iterations > 0, "Le test doit effectuer au moins un step previous.");

        var finalWindow = step.Window
            .Where(c => c.Idx != -1)
            .Select(c => c.Ts)
            .ToArray();

        Assert.NotEmpty(finalWindow);
        AssertStrictlyIncreasing(finalWindow, "[FINAL WINDOW]");

        // On doit avoir parcouru exactement toutes les candles du fichier, une seule fois
        Assert.Equal(expectedTotalCandles, totalCandlesRead);
        Assert.Equal(expectedTotalCandles, seenTs.Count);

        // Après le début du fichier, appel supplémentaire => no-op logique attendu
        var noOp = candleIndex.CandlesPrevious(step.CurrentIdx, range);

        Assert.Equal(step.CurrentIdx, noOp.CurrentIdx);
        Assert.Equal(-1, noOp.PreviousCursorIdx);
    }
}