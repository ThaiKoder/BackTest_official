using System;
using System.Collections.Generic;
using Xunit;
using BacktestApp.Controls;


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

    //Test3 : Parcourir liste fichiers index et qu'ils sont dans l'ordre croissant

    //Test4 : Parcourir liste fichiers index et vérifier pas de doublons

    //Test5 : Parcourir liste fichiers index et vérifier x premiers et x derniers records en fonction de l'index courant

    //Test6 : Verifier x premiers et x derniers records en fonction de l'index courant 0 et len -1

    //Test7 : Parcourirs liste fichiers avec recherche binaire et verifier l'idx + nom fichier + x derniers et x premiers en fonction de la position idx

    //Test8 : Modifier dynamiquement x dernier et x premiers en fonction de l'idx courant

    //Parcourir tout les fichiers avec Next File



    //Test3 : charger un fichier index invalide (ex: non existant)

    //Test4 : charger un fichier index avec un format incorrect (ex: pas de records)

    //Test5 : charger un fichier index avec des records partiels (ex: taille du fichier pas multiple de 8)

    //Test6 : charger un fichier index valide et vérifier le count

    //Test7 : charger un fichier index valide et vérifier la lecture d'un record (ex: lire le premier record et vérifier sa valeur)

    //

}
