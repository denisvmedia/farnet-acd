using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FarNet.ACD
{
    class Progress
    {
        const char EMPTY_BLOCK = '\x2591';
        const char SOLID_BLOCK = '\x2588';

        public static string FormatProgress(long position, long maxposition, int width = 62)
        {
            int percentage = (int)Math.Round((double)(100 * position) / maxposition);

            return FormatProgressPercentage(percentage, width);
        }

        public static string FormatProgressPercentage(int percentage, int width = 62)
        {
            // number of chars to fill
            int n = width * percentage / 100;

            // do not fill too much
            if (n > width)
            {
                n = width;
            }
            // leave 1 not filled
            else if (n == width)
            {
                if (percentage < 100)
                    --n;
            }
            // fill at least 1
            else if (n == 0)
            {
                if (percentage > 0)
                    n = 1;
            }

            return new string(SOLID_BLOCK, n) + new string(EMPTY_BLOCK, width - n) + string.Format(null, "{0,3}%", percentage);
        }
    }
}
