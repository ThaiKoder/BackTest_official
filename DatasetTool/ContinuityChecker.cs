

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DatasetTool;

public static class ContinuityChecker
{
    // Ajuste si besoin
    private const int ContinuationThreshold = 6900;
    private const int BreakThreshold = 1440;

    // Symboles cibles (mets ceux que tu veux suivre)
    private static readonly HashSet<int> TargetSymbols = new HashSet<int> { 1, 2, 3, 4 };

    private sealed class SymbolState
    {
        public long ContinuationCount;   // cumul d'occurrences en "continuation"
        public long BreakCount;          // cumul d'occurrences en "coupure"
        public bool ContinuationLogged;  // pour log une seule fois quand atteint
    }

    public static void Run(string[] binFiles, int limitFiles = -1, int limitCandles = -1)
    {
        // Etat par symbole (cibles)
        var stateBySymbol = new Dictionary<int, SymbolState>();
        foreach (var s in TargetSymbols)
            stateBySymbol[s] = new SymbolState();

        int globalCount = 0;
        int localCount = 0;
        long totalCandles = 0;

        // Buffers pour le groupement par jour
        DateOnly? currentDay = null;

        // nb d'occurrences par symbole dans le jour courant
        var dayCounts = new Dictionary<int, int>();

        // symboles présents dans le jour courant
        var daySymbols = new HashSet<int>();

        // symboles présents dans le jour précédent (précédent groupement rencontré)
        var prevDaySymbols = new HashSet<int>();

        // date du jour précédent
        DateOnly? prevDay = null;

        void FinalizeDay(DateOnly day)
        {
            if (prevDay is null)
            {
                // Premier jour rencontré : pas de "hier" => tout ce qui est présent aujourd'hui est "coupure"
                foreach (var s in daySymbols)
                {
                    if (!TargetSymbols.Contains(s)) continue;

                    var st = stateBySymbol[s];
                    int occ = dayCounts.TryGetValue(s, out var c) ? c : 0;

                    // Pas de continuité possible le 1er jour => coupure
                    st.BreakCount += occ;

                    if (st.BreakCount >= BreakThreshold)
                    {
                        Debug.WriteLine($"[BREAK] Symbol={s} atteint {BreakThreshold} (1er jour: {day}) -> reset");
                        ResetSymbol(st);
                    }
                }

                prevDay = day;
                prevDaySymbols = new HashSet<int>(daySymbols);
                return;
            }

            // On a un jour précédent => on applique la règle
            // "si présent aujourd'hui ET présent hier => continuation, sinon => coupure"
            foreach (var s in daySymbols)
            {
                if (!TargetSymbols.Contains(s)) continue;

                var st = stateBySymbol[s];
                int occ = dayCounts.TryGetValue(s, out var c) ? c : 0;

                bool wasInPrevGroup = prevDaySymbols.Contains(s);

                if (wasInPrevGroup)
                {
                    st.ContinuationCount += occ;

                    if (!st.ContinuationLogged && st.ContinuationCount >= ContinuationThreshold)
                    {
                        st.ContinuationLogged = true;
                        Debug.WriteLine($"[CONT] Symbol={s} atteint {ContinuationThreshold} à la date {day}");
                    }
                }
                else
                {
                    // Pas dans le groupement précédent => coupure "en plus"
                    st.BreakCount += occ;

                    if (st.BreakCount >= BreakThreshold)
                    {
                        Debug.WriteLine($"[BREAK] Symbol={s} atteint {BreakThreshold} à la date {day} -> reset");
                        ResetSymbol(st);
                    }
                }
            }

            // IMPORTANT : si tu veux aussi compter comme "coupure"
            // les symboles qui ÉTAIENT hier mais NE SONT PAS aujourd'hui :
            // (ton message peut se lire comme ça aussi)
            //
            // foreach (var s in prevDaySymbols)
            // {
            //     if (!TargetSymbols.Contains(s)) continue;
            //     if (daySymbols.Contains(s)) continue;
            //
            //     var st = stateBySymbol[s];
            //     // On ne connaît pas le nb exact d'occurrences manquantes (car absent),
            //     // mais si tu considères "absence d'un jour" = 1440 :
            //     st.BreakCount += 1440;
            //
            //     if (st.BreakCount >= BreakThreshold)
            //     {
            //         Debug.WriteLine($"[BREAK] Symbol={s} absent le {day} (+=1440) -> reset");
            //         ResetSymbol(st);
            //     }
            // }

            prevDay = day;
            prevDaySymbols = new HashSet<int>(daySymbols);
        }

        foreach (var binPath in binFiles)
        {
            if (globalCount >= limitFiles && limitFiles != -1) break;

            using var bin = new Binary(binPath);

            bin.ReadAllFast((ts, o, h, l, c, v, symbol) =>
            {
                if (localCount >= limitCandles && limitCandles != -1) return;

                long ms = ts / 1_000_000L;
                var dto = DateTimeOffset.FromUnixTimeMilliseconds(ms);

                // Regroupement par "jour"
                // Choisis UTC ou local selon tes données :
                // - si tes ts sont UTC => dto.UtcDateTime.Date
                // - sinon dto.Date
                var day = DateOnly.FromDateTime(dto.UtcDateTime);

                if (currentDay is null)
                {
                    currentDay = day;
                }
                else if (day != currentDay.Value)
                {
                    // On clôture le jour précédent
                    FinalizeDay(currentDay.Value);

                    // On reset les buffers pour le nouveau jour
                    currentDay = day;
                    dayCounts.Clear();
                    daySymbols.Clear();
                }

                int s = symbol; // byte->int

                // On ne bufferise que les symboles cibles (sinon enlève ce if)
                if (TargetSymbols.Contains(s))
                {
                    daySymbols.Add(s);
                    dayCounts.TryGetValue(s, out int count);
                    dayCounts[s] = count + 1;
                }

                localCount++;
                totalCandles++;
            });

            // fin de fichier : on continue, on ne finalize pas forcément ici
            // car un jour peut théoriquement être coupé entre fichiers.
            localCount = 0;
            globalCount++;
        }

        // Finalize du dernier jour restant
        if (currentDay is not null)
            FinalizeDay(currentDay.Value);

        Debug.WriteLine($"Terminé. Fichiers traités={globalCount}, totalCandles={totalCandles}");
    }

    private static void ResetSymbol(SymbolState st)
    {
        st.ContinuationCount = 0;
        st.BreakCount = 0;
        st.ContinuationLogged = false;
    }
}