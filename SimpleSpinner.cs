using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern {
    internal class SimpleSpinner {
        ulong counter;
        public SimpleSpinner() {
            counter = 0;
        }

        public char Turn() {
            counter++;
            char spinner = ' ';
            
            switch (counter % 4) {
                case 0: spinner = '/'; break;
                case 1: spinner = '-'; break;
                case 2: spinner = '\\'; break;
                case 3: spinner = '|'; break;
            }

            return spinner;
        }
    }
}
