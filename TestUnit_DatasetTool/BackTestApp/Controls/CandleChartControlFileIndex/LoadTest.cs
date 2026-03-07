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


    //Test5 : Parcourir liste fichiers index et vérifier x premiers et x derniers records en fonction de l'index courant

    //Test6 : Verifier x premiers et x derniers records en fonction de l'index courant 0 et len -1

    //Parcourir tout les fichiers avec Next File

    //Test7 : Parcourirs liste fichiers avec recherche binaire et verifier l'idx + nom fichier + x derniers et x premiers en fonction de la position idx

    //Test8 : Modifier dynamiquement x dernier et x premiers en fonction de l'idx courant




    //Test3 : charger un fichier index invalide (ex: non existant)

    //Test4 : charger un fichier index avec un format incorrect (ex: pas de records)

    //Test5 : charger un fichier index avec des records partiels (ex: taille du fichier pas multiple de 8)

    //Test6 : charger un fichier index valide et vérifier le count

    //Test7 : charger un fichier index valide et vérifier la lecture d'un record (ex: lire le premier record et vérifier sa valeur)

    //

}
