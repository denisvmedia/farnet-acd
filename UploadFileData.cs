using System.Threading;

namespace FarNet.ACD
{
    class UploadFileData
    {
        public FarFile File;
        public string RemoteFileName;
        public FSItem ParentItem;
        public Tools.ProgressForm Form;
        public ManualResetEvent PauseEvent;
        public long TotalProgress;
        public long TotalSize;
        public int TimestampStartOne;
        public int TimestampStartTotal;
    }
}
