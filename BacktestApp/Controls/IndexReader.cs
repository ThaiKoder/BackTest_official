using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BacktestApp.Controls
{
    //Index Courent Global
    internal class IndexReader
    {
        //Constructor
        public IndexReader()
        {
            Debug.WriteLine("IndexReader Constructor");
        }

        // Charger fichier bin en mmap
        public void Load(string fileNamePath)
        {

        }

        //Charger un fichier par index avec x range precedent et suivant
        //FilesAround(index, range)

        //Append file name of mmap to dynamique list to tile& remove x head from list
        //FilesPrevious(range)

        //Append file name of mmap to dynamique list to head & remove x tile from list
        //FilesNext(range)


    }
}
