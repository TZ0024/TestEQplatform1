using ASCOM.Astrometry.NOVASCOM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TEQ1Helper
{
    internal static class HelperClass1
    {
        /// <summary>
        /// Get substring until first occurrence of given character has been found. Returns the whole string if character has not been found.
        /// </summary>
        public static string GetUntil(this string str, char @char)
        {
            int index = str.IndexOf(@char);
            if (index > 0)
            {
                return str.Substring(0, index);
            }
            else
            {
                return str;
            }
        }

    }
}

