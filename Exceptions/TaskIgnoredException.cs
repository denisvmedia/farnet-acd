using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FarNet.ACD.Exceptions
{
    class TaskIgnoredException : TaskCanceledException
    {
        public TaskIgnoredException()
        : base()
        {
        }

        public TaskIgnoredException(string message)
        : base(message)
        {
        }

        public TaskIgnoredException(string message, Exception e)
        : base(message, e)
        {
        }
    }
}
