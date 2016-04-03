using System;

namespace FarNet.ACD.Exceptions
{
    public class IOException: Exception
    {
        public IOException()
        : base()
        {
        }

        public IOException(string message) 
        : base(message)
        {
        }

        public IOException(string message, Exception e)
        : base(message, e) 
        {
        }
    }
}
