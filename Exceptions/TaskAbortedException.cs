using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FarNet.ACD.Exceptions
{
    class TaskAbortedException: TaskCanceledException
    {
        public TaskAbortedException()
        : base()
        {
        }

        public TaskAbortedException(string message) 
        : base(message)
        {
        }

        public TaskAbortedException(string message, Exception e)
        : base(message, e) 
        {
        }
    }
}
