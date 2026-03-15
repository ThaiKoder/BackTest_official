using Avalonia;
using Avalonia.Media.TextFormatting;
using System.Collections.Generic;
using System;
using System.IO;
using DatasetTool;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    private FileIndex? _testFileIndex;

    // Indique le contrat courant (index dans _starts/_ends)
    internal int Test_CurrentIdx => _currentIdx;

    // Pour pouvoir forcer le state "fin de fichier" en test
    internal long Test_FileCount => _fileCount;
    internal long Test_WindowStart => _windowStart;
    internal int Test_WindowCount => WindowCount;

    // Permet au test de charger un contrat + voisins 
    internal void Test_LoadByIndexWithNeighbors(int idx) => LoadByIndexWithNeighbors(idx);

    // Permet au test de forcer le windowStart
    internal void Test_ReloadWindow(long newStart) => ReloadWindow(newStart, preserveView: false);

    // Permet au test de charger l'index réel
    internal void Test_LoadIndex() => loadIndex();

    // Optionnel : vérifier le preload
    internal (int m2, int m1, int cur, int p1, int p2) Test_Neighbors(int idx) => GetNeighborIndexes(idx);

    internal int Test_ContractsCount => _starts?.Length ?? 0;

    internal int Test_CursorStep => CursorStep;


    internal IReadOnlyList<long> Test_GetWindowCandleIds()
    {
        var ids = new long[_windowLoaded];
        for (int i = 0; i < _windowLoaded; i++)
            ids[i] = _windowStart + i;
        return ids;
    }



    // =========================
    // Hooks mode FilesNext/CandlesNext (UI path)
    // =========================

    internal void Test_InitializeFilesAndCandlesMode()
        => InitializeFilesAndCandlesMode();

    internal int Test_GetUiFileCurrentIdx()
        => _uiFileStep?.CurrentIdx ?? -1;

    internal int Test_GetUiFileNextCursorIdx()
        => _uiFileStep?.NextCursorIdx ?? -1;

    internal int Test_GetUiCandleCurrentIdx()
        => _uiCandleStep?.CurrentIdx ?? -1;

    internal int Test_GetUiCandleNextCursorIdx()
        => _uiCandleStep?.NextCursorIdx ?? -1;

    internal long Test_GetUiFileCount()
        => _uiCandleIndex?.Count ?? 0;

    internal IReadOnlyList<long> Test_GetLoadedTimestamps()
    {
        var result = new long[_windowLoaded];
        for (int i = 0; i < _windowLoaded; i++)
            result[i] = RingTsAtLogical(i);

        return result;
    }
    internal int Test_GetWindowLoaded()
        => _windowLoaded;

    internal long Test_GetWindowStart()
        => _windowStart;

    internal bool Test_AdvanceCandlesNext()
        => AdvanceCandlesNext();









    internal int Test_GetLastRemovedCount()
    => _uiCandleStep?.Removed.Count ?? 0;

    internal int Test_GetLastAddedCount()
        => _uiCandleStep?.Added.Count ?? 0;




    // Met le centre exactement au milieu de la fenêtre actuelle
    internal void Test_SetCenterToWindowMiddle()
    {
        if (_windowLoaded <= 0) return;

        int mid = _windowLoaded / 2;
        _centerTimeSec = TsNsToEpochSeconds(GetTs(mid));
    }


    // ✅ NOUVEAU : simuler une taille de control (Bounds) en test
    internal void Test_SetBoundsForTest(double width, double height)
    {
        // Si Bounds est read-only chez toi, remplace par une variable interne
        // ou un "GetPlotRect" spécial test.
        // Si Bounds est accessible en lecture seule, on contourne en appelant directement EnsureWindowAroundView
        // avec un Rect "plot" cohérent.
        _testBounds = new Rect(0, 0, width, height);
    }

    // ✅ NOUVEAU : tick UI-like (appelle EnsureWindowAroundView)
    internal void Test_TickEdgeTimerOnce()
    {
        if (_file is null) return;
        if (_windowLoaded <= 0) return;

        // plot rect comme UI
        var bounds = GetTestBoundsOrReal();
        var plot = GetPlotRect(new Rect(0, 0, bounds.Width, bounds.Height));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        EnsureWindowAroundView(plot);
    }

    // --- Support test bounds ---
    private Rect? _testBounds;

    private Rect GetTestBoundsOrReal()
    {
        // si tu ne peux pas setter Bounds, on utilise une valeur de test
        if (_testBounds.HasValue) return _testBounds.Value;
        return new Rect(0, 0, Bounds.Width, Bounds.Height);
    }






    //FilesNext
    public FileIndex getFileIndex => _testFileIndex;

    public FileIndex.FileCursorStep FilesNext(int cursorIdx, int range)
        => _testFileIndex!.FilesNext(cursorIdx, range);

    internal long Test_IndexCount => getFileIndex.Count;

    internal FileIndex Test_indexReader() => new FileIndex();

    internal void Test_LoadIndexFile(string path)
    {
        _testFileIndex = Test_indexReader();
        _testFileIndex.Load(path);
    }




    //CandlesNext
    private CandleIndex? _testCandleIndex;

    public CandleIndex getCandleIndex => _testCandleIndex!;

    public CandleIndex.CandleCursorStep CandlesNext(int cursorIdx, int range)
        => _testCandleIndex!.CandlesNext(cursorIdx, range);

    internal long Test_CandleCount => _testCandleIndex?.Count ?? 0;
    internal CandleIndex Test_candleReader() => new CandleIndex();

    internal void Test_CandlesLoadFromCurrentFileIndex(int fileIdx)
    {
        if (_testFileIndex is null)
            throw new InvalidOperationException("L'index fichier doit être chargé avant de charger les candles.");

        var (startYmd, endYmd) = getFileIndex.Read(fileIdx);

        string path = Path.Combine(
            "data",
            "bin",
            $"glbx-mdp3-{startYmd}-{endYmd}.ohlcv-1m.bin");

        _testCandleIndex?.Dispose();
        _testCandleIndex = Test_candleReader();
        _testCandleIndex.Load(path);
    }











}