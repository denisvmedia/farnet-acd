using System;

namespace FarNet.ACD.Exceptions
{
    public class RemoteFileExistsException: IOException
    {
        public RemoteFileExistsException()
        : base()
        {
        }

        public RemoteFileExistsException(string message) 
        : base(message)
        {
        }

        public RemoteFileExistsException(string message, Exception e)
        : base(message, e) 
        {
        }
    }
}
