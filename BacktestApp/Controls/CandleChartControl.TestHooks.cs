namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    // Indique le contrat courant (index dans _starts/_ends)
    internal int Test_CurrentIdx => _currentIdx;

    // Pour pouvoir forcer le state "fin de fichier" en test
    internal long Test_FileCount => _fileCount;
    internal long Test_WindowStart => _windowStart;
    internal int Test_WindowCount => WindowCount;

    // Permet au test de charger un contrat + voisins 
    internal void Test_LoadByIndexWithNeighbors(int idx) => LoadByIndexWithNeighbors(idx);

    // Permet au test de forcer le windowStart
    internal void Test_ReloadWindow(long newStart) => ReloadWindow(newStart);

    // Permet au test de charger l'index réel
    internal void Test_LoadIndex() => loadIndex();

    // Optionnel : vérifier le preload
    internal (int m2, int m1, int cur, int p1, int p2) Test_Neighbors(int idx) => GetNeighborIndexes(idx);

    internal int Test_ContractsCount => _starts?.Length ?? 0;

    internal int Test_CursorStep => CursorStep;

    // Met le centre exactement au milieu de la fenêtre actuelle
    internal void Test_SetCenterToWindowMiddle()
    {
        if (_windowLoaded <= 0) return;

        int mid = _windowLoaded / 2;
        _centerTimeSec = TsNsToEpochSeconds(_ts[mid]);
    }
}