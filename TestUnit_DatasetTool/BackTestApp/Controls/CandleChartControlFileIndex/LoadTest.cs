using Avalonia;
using BacktestApp.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;


namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlFileIndex;


public class LoadTest
{
    [Fact]
    public void Test1_indexReaderConstructor()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        var reader = chart.Test_indexReader();
        Assert.NotNull(reader);
    }

    [Fact]
    public void Test2_loadValidFile()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        try
        {
            chart.Test_LoadIndexFile("data/bin/_index.bin");
            Assert.True(true, "Le fichier index doit contenir au moins un record.");
        }
        catch (Exception ex)
        {
            Assert.True(false, $"Le chargement du fichier index a échoué: {ex.Message}");
        }
    }

    [Fact]
    public void Test3_IndexFiles_AreIncreasing()
    {
        int nbElement = 817;
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndexFile("data/bin/_index.bin");

        long count = chart.Test_IndexCount;
        Assert.True(count > 0, "Le fichier index doit contenir au moins un record.");

        Assert.True(count == nbElement, $"La liste doit contenir {nbElement} element");

        uint lastDate = 0;
        int nbDate = 0;

        for (int i = 0; i < count; i++)
        {
            var (start, end) = chart.getFileIndex.Read(i);

            // Vérifie que les dates sont dans l'ordre croissant
            if (!(lastDate <= start)) Assert.True(false, $"Le fichier index n'est pas dans l'ordre croissant à l'index {i}: {start} <= {lastDate}");

            // Vérifie qu'il n'y a pas de doublons (start == lastDate)
            if (lastDate == start) Assert.True(false, $"Le fichier index contient des doublons à l'index {i}: {start} == {lastDate}");

            lastDate = start;
            nbDate++;
        }

        Assert.True(true, "Tous les fichiers index sont dans l'ordre croissant.");
        Assert.True(nbDate == nbElement, $"La liste doit contenir 187 {nbElement} à la fin");

    }



   // Parcourir liste fichiers index et vérifier x premiers et x derniers records en fonction de l'index courant pour seulement 15 ? records
    //[Fact]
    //public void Test4_FilesNext_Range3_Iterates_AsExpected()
    //{
    //    var chart = new global::BacktestApp.Controls.CandleChartControl();
    //    chart.Test_LoadIndexFile("data/bin/_index.bin");

    //    int range = 3; // Modifier dynamiquement x dernier et x premiers en fonction de l'idx courant
    //    int minNeeded = 15;

    //    Assert.True(chart.Test_IndexCount >= minNeeded, $"Le test demande au moins {minNeeded} records.");

    //    // STEP 1 : -1 -1 -1 [0] 1 2 3
    //    var s1 = chart.FilesNext(0, range);

    //    Assert.Equal(0, s1.CurrentIdx);
    //    Assert.Equal(4, s1.NextCursorIdx);
    //    Assert.Equal(new[] { -1, -1, -1, 0, 1, 2, 3 }, s1.Window.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 0, 1, 2, 3 }, s1.Added.Select(x => x.Idx).ToArray());
    //    Assert.Empty(s1.Removed);

    //    // STEP 2 : 1 2 3 [4] 5 6 7
    //    var s2 = chart.FilesNext(s1.NextCursorIdx, range);

    //    Assert.Equal(4, s2.CurrentIdx);
    //    Assert.Equal(8, s2.NextCursorIdx);
    //    Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, s2.Window.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 4, 5, 6, 7 }, s2.Added.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 0 }, s2.Removed.Select(x => x.Idx).ToArray());

    //    // STEP 3 : 5 6 7 [8] 9 10 11
    //    var s3 = chart.FilesNext(s2.NextCursorIdx, range);

    //    Assert.Equal(8, s3.CurrentIdx);
    //    Assert.Equal(12, s3.NextCursorIdx);
    //    Assert.Equal(new[] { 5, 6, 7, 8, 9, 10, 11 }, s3.Window.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 8, 9, 10, 11 }, s3.Added.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 1, 2, 3, 4 }, s3.Removed.Select(x => x.Idx).ToArray());

    //    // STEP 4 : 9 10 11 [12] 13 14 -1
    //    var s4 = chart.FilesNext(s3.NextCursorIdx, range);

    //    Assert.Equal(12, s4.CurrentIdx);
    //    Assert.Equal(-1, s4.NextCursorIdx);
    //    Assert.Equal(new[] { 9, 10, 11, 12, 13, 14, -1 }, s4.Window.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 12, 13, 14 }, s4.Added.Select(x => x.Idx).ToArray());
    //    Assert.Equal(new[] { 5, 6, 7, 8 }, s4.Removed.Select(x => x.Idx).ToArray());


    //    // STEP 5 : appel après fin → rien ne change
    //    var s5 = chart.FilesNext(s4.CurrentIdx, range);

    //    Assert.Equal(12, s5.CurrentIdx);
    //    Assert.Equal(-1, s5.NextCursorIdx);
    //    Assert.Equal(new[] { 9, 10, 11, 12, 13, 14, -1 }, s5.Window.Select(x => x.Idx).ToArray());
    //    Assert.Empty(s5.Added);
    //    Assert.Empty(s5.Removed);
    //}


    //FULL TEST Parcourir tout les fichiers avec Next File
    private uint lastDate = 0;
    HashSet<uint> listUniqueDate = new HashSet<uint>();
    private bool FileExistsForIndex(CandleChartControl.FileIndex.FileItem fileRecord)
    {
        string folder = "data/bin";
        string file = $"glbx-mdp3-{fileRecord.StartYmd}-{fileRecord.EndYmd}.ohlcv-1m.bin";
        string path = Path.Combine(folder, file);

        Assert.True(fileRecord.StartYmd > lastDate, $"Vérification du fichier {file} avec date {fileRecord.StartYmd} > lastDate {lastDate}");
        lastDate = fileRecord.StartYmd;

        if (!listUniqueDate.Add(fileRecord.StartYmd))
        {
            // Existait déjà donc doublon
            Assert.True(false, $"Le fichier {file} avec date {fileRecord.StartYmd} est un doublon (déjà vu).");
        }


        return File.Exists(path);
    }

    [Fact]
    public void Test5_FilesNext_Range3_Iterates_Whole_List()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        chart.Test_LoadIndexFile("data/bin/_index.bin");

        int range = 3;
        int count = (int)chart.Test_IndexCount;
        int stepSize = range + 1;

        Assert.True(count > 0, "Le fichier index doit contenir au moins un record.");

        int cursor = 0;
        int previousCursor = -1;
        int stepNumber = 0;
        int totalFile = 0;

        global::BacktestApp.Controls.CandleChartControl.FileIndex.FileCursorStep lastStep = null!;

        while (true)
        {
            var step = chart.FilesNext(cursor, range);
            stepNumber++;

            if (previousCursor != -1)
            {
                Assert.Equal(previousCursor + stepSize, cursor);
            }

            // Vérifie CurrentIdx
            Assert.Equal(cursor, step.CurrentIdx);

            // Vérifie la fenêtre attendue
            int[] expectedWindow = new int[range * 2 + 1];
            int p = 0;
            for (int i = cursor - range; i <= cursor + range; i++)
                expectedWindow[p++] = (i < 0 || i >= count) ? -1 : i;

            Assert.Equal(expectedWindow, step.Window.Select(x => x.Idx).ToArray());

            // Vérifie le contenu start/end pour tous les idx valides
            foreach (var item in step.Window)
            {
                if (item.Idx == -1)
                {
                    Assert.Equal(0u, item.StartYmd);
                    Assert.Equal(0u, item.EndYmd);
                }
                else
                {
                    var (start, end) = chart.getFileIndex.Read(item.Idx);
                    Assert.Equal(start, item.StartYmd);
                    Assert.Equal(end, item.EndYmd);
                }
            }

            // Vérifie Added / Removed
            int[] expectedAdded;
            int[] expectedRemoved;

            if (stepNumber == 1)
            {
                expectedAdded = expectedWindow.Where(x => x != -1).ToArray();
                expectedRemoved = Array.Empty<int>();
                foreach (var aFile in step.Added)
                {
                    //Verifie si le fichier existe
                    bool exists = FileExistsForIndex(aFile);
                    if (exists) totalFile++;
                }
            }
            else
            {
                int[] prevWindow = new int[range * 2 + 1];
                p = 0;
                for (int i = previousCursor - range; i <= previousCursor + range; i++)
                    prevWindow[p++] = (i < 0 || i >= count) ? -1 : i;

                expectedAdded = expectedWindow
                    .Where(x => x != -1 && !prevWindow.Contains(x))
                    .ToArray();

                expectedRemoved = prevWindow
                    .Where(x => x != -1 && !expectedWindow.Contains(x))
                    .ToArray();


                foreach (var aFile in step.Added)
                {
                    //Verifie si le fichier existe
                    bool exists = FileExistsForIndex(aFile);
                    if (exists) totalFile++;
                }

            }

            Assert.Equal(expectedAdded, step.Added.Select(x => x.Idx).ToArray());
            Assert.Equal(expectedRemoved, step.Removed.Select(x => x.Idx).ToArray());

            // Vérifie NextCursorIdx
            bool hasRightMinusOne = expectedWindow[^1] == -1;
            int expectedNext = hasRightMinusOne ? -1 : cursor + stepSize;

            Assert.Equal(expectedNext, step.NextCursorIdx);

            lastStep = step;

            if (step.NextCursorIdx == -1)
                break;

            previousCursor = cursor;
            cursor = step.NextCursorIdx;
        }

        // Appel après fin : rien ne change
        var finalNoOp = chart.FilesNext(lastStep.CurrentIdx, range);

        Assert.Equal(lastStep.CurrentIdx, finalNoOp.CurrentIdx);
        Assert.Equal(-1, finalNoOp.NextCursorIdx);
        Assert.Equal(
            lastStep.Window.Select(x => x.Idx).ToArray(),
            finalNoOp.Window.Select(x => x.Idx).ToArray());

        Assert.Empty(finalNoOp.Added);
        Assert.Empty(finalNoOp.Removed);
        Assert.True(cursor+1==totalFile, $"Parcours complet de la liste avec {cursor + 1} étapes et {totalFile} fichiers vérifiés.");
        Assert.True(totalFile == 817, $"Parcours complet de la liste pour 817 fichiers et {totalFile} fichiers vérifiés.");
    }

    [Fact]
    public void Test6_FilesNext_RingBuffer_Should_Shift_And_Append_Correctly_On_Whole_Traversal()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        chart.Test_LoadIndexFile("data/bin/_index.bin");

        int range = 3;
        int count = (int)chart.Test_IndexCount;
        int expectedFileCount = 817;
        int stepSize = range + 1;

        Assert.True(count > 0, "Le fichier index doit contenir au moins un record.");
        Assert.Equal(expectedFileCount, count);

        int cursor = 0;
        int iteration = 0;

        var allSeen = new HashSet<int>();
        global::BacktestApp.Controls.CandleChartControl.FileIndex.FileCursorStep? previous = null;


        while (true)
        {
            var current = chart.FilesNext(cursor, range);
            iteration++;

            Assert.Equal(cursor, current.CurrentIdx);

            var currentWindow = current.Window.Select(x => x.Idx).ToArray();
            var currentAdded = current.Added.Select(x => x.Idx).ToArray();
            var currentRemoved = current.Removed.Select(x => x.Idx).ToArray();

            // 1) Fenêtre logique attendue
            int[] expectedWindow = new int[range * 2 + 1];
            int p = 0;
            for (int i = cursor - range; i <= cursor + range; i++)
                expectedWindow[p++] = (i < 0 || i >= count) ? -1 : i;

            Assert.Equal(expectedWindow, currentWindow);

            // 2) Tous les index valides vus une seule fois globalement via Added
            foreach (var idx in currentAdded)
            {
                Assert.True(idx >= 0 && idx < count, $"Added contient un idx invalide: {idx}");
                Assert.True(allSeen.Add(idx), $"Idx ajouté plusieurs fois dans le parcours global: {idx}");
            }

            if (previous == null)
            {
                // Premier step : Added = tous les valides de la fenêtre, Removed vide
                Assert.Equal(expectedWindow.Where(x => x != -1).ToArray(), currentAdded);
                Assert.Empty(currentRemoved);
            }
            else
            {
                var prevWindow = previous.Window.Select(x => x.Idx).ToArray();

                // 3) Vérifie Added / Removed attendus par différence
                int[] expectedAdded = expectedWindow
                    .Where(x => x != -1 && !prevWindow.Contains(x))
                    .ToArray();

                int[] expectedRemoved = prevWindow
                    .Where(x => x != -1 && !expectedWindow.Contains(x))
                    .ToArray();

                Assert.Equal(expectedAdded, currentAdded);
                Assert.Equal(expectedRemoved, currentRemoved);

                // 4) Vérifie la propriété ring buffer sur les éléments valides uniquement
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

                // 5) Le curseur doit avancer du step exact tant qu'on n'est pas en fin
                Assert.Equal(previous.CurrentIdx + stepSize, current.CurrentIdx);
            }

            // 6) Vérifie NextCursorIdx
            bool hasRightMinusOne = expectedWindow[^1] == -1;
            int expectedNext = hasRightMinusOne ? -1 : cursor + stepSize;
            Assert.Equal(expectedNext, current.NextCursorIdx);

            previous = current;

            if (current.NextCursorIdx == -1)
                break;

            cursor = current.NextCursorIdx;

            // sécurité anti boucle infinie
            Assert.True(iteration <= count + 2, $"Boucle suspecte: iteration={iteration}, count={count}");
        }

        // 7) On doit avoir vu tous les fichiers exactement une fois via Added
        Assert.Equal(expectedFileCount, allSeen.Count);
        Assert.Equal(count, allSeen.Count);
        Assert.Equal(Enumerable.Range(0, count).ToArray(), allSeen.OrderBy(x => x).ToArray());

        // 8) Appel après fin = no-op
        var finalStep = chart.FilesNext(previous!.CurrentIdx, range);

        Assert.Equal(previous.CurrentIdx, finalStep.CurrentIdx);
        Assert.Equal(-1, finalStep.NextCursorIdx);
        Assert.Equal(
            previous.Window.Select(x => x.Idx).ToArray(),
            finalStep.Window.Select(x => x.Idx).ToArray());
        Assert.Empty(finalStep.Added);
        Assert.Empty(finalStep.Removed);
    }

    //Test6 : Verifier x premiers et x derniers records en fonction de l'index courant 0 et len -1



    //Test7 : Parcourirs liste fichiers avec recherche binaire et verifier l'idx + nom fichier + x derniers et x premiers en fonction de la position idx






    //Test3 : charger un fichier index invalide (ex: non existant)

    //Test4 : charger un fichier index avec un format incorrect (ex: pas de records)

    //Test5 : charger un fichier index avec des records partiels (ex: taille du fichier pas multiple de 8)

    //Test6 : charger un fichier index valide et vérifier le count

    //Test7 : charger un fichier index valide et vérifier la lecture d'un record (ex: lire le premier record et vérifier sa valeur)

    //

}
