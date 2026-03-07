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



    //Parcourir tout les fichiers avec Next File
   // Parcourir liste fichiers index et vérifier x premiers et x derniers records en fonction de l'index courant
    [Fact]
    public void Test4_FilesNext_Range3_Iterates_AsExpected()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        chart.Test_LoadIndexFile("data/bin/_index.bin");

        int range = 3; // Modifier dynamiquement x dernier et x premiers en fonction de l'idx courant
        int minNeeded = 15;

        Assert.True(chart.Test_IndexCount >= minNeeded, $"Le test demande au moins {minNeeded} records.");

        // STEP 1 : -1 -1 -1 [0] 1 2 3
        var s1 = chart.FilesNext(0, range);

        Assert.Equal(0, s1.CurrentIdx);
        Assert.Equal(4, s1.NextCursorIdx);
        Assert.Equal(new[] { -1, -1, -1, 0, 1, 2, 3 }, s1.Window.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, s1.Added.Select(x => x.Idx).ToArray());
        Assert.Empty(s1.Removed);

        // STEP 2 : 1 2 3 [4] 5 6 7
        var s2 = chart.FilesNext(s1.NextCursorIdx, range);

        Assert.Equal(4, s2.CurrentIdx);
        Assert.Equal(8, s2.NextCursorIdx);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, s2.Window.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 4, 5, 6, 7 }, s2.Added.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 0 }, s2.Removed.Select(x => x.Idx).ToArray());

        // STEP 3 : 5 6 7 [8] 9 10 11
        var s3 = chart.FilesNext(s2.NextCursorIdx, range);

        Assert.Equal(8, s3.CurrentIdx);
        Assert.Equal(12, s3.NextCursorIdx);
        Assert.Equal(new[] { 5, 6, 7, 8, 9, 10, 11 }, s3.Window.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 8, 9, 10, 11 }, s3.Added.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, s3.Removed.Select(x => x.Idx).ToArray());

        // STEP 4 : 9 10 11 [12] 13 14 -1
        var s4 = chart.FilesNext(s3.NextCursorIdx, range);

        Assert.Equal(12, s4.CurrentIdx);
        Assert.Equal(-1, s4.NextCursorIdx);
        Assert.Equal(new[] { 9, 10, 11, 12, 13, 14, -1 }, s4.Window.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 12, 13, 14 }, s4.Added.Select(x => x.Idx).ToArray());
        Assert.Equal(new[] { 5, 6, 7, 8 }, s4.Removed.Select(x => x.Idx).ToArray());


        // STEP 5 : appel après fin → rien ne change
        var s5 = chart.FilesNext(s4.CurrentIdx, range);

        Assert.Equal(12, s5.CurrentIdx);
        Assert.Equal(-1, s5.NextCursorIdx);
        Assert.Equal(new[] { 9, 10, 11, 12, 13, 14, -1 }, s5.Window.Select(x => x.Idx).ToArray());
        Assert.Empty(s5.Added);
        Assert.Empty(s5.Removed);
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
