using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FarNet.ACD
{
    class ProgressTask
    {
        private Tools.ProgressForm form;
        private CancellationToken Token;

        public ProgressTask(string activity, string title, CancellationToken token)
        {
            form = new Tools.ProgressForm();
            form.Activity = activity;
            form.Title = title;
            form.CanCancel = true;
            Token = token;
        }

        public Tools.ProgressForm ShowDialog(CancellationTokenSource cs = null)
        {
            Task task = null;

            task = Task.Factory.StartNew(() =>
            {
                form.Show();
                if (form.IsClosed && form.Title != "done" && cs != null)
                {
                    cs.Cancel();
                }
            });

            Token.Register(() =>
            {
                form.Title = "done";
                form.Close();
            });

            return form;
        }
    }
}
