
using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using System.Threading;

namespace FarNet.ACD
{
    /// <summary>
    /// Panel file explorer to view .resources file data.
    /// </summary>
    class ACDExplorer : Explorer
    {
        public readonly ACDClient Client;
        ACDPanel Panel;

        public ACDExplorer(ACDClient client, ACDPanel panel = null, string path = "\\")
            : base(new Guid("dc05c639-5e56-4c0e-b83e-19c63731949a"))
        {
            Client = client;
            Location = path;
            Panel = panel;
            CanImportFiles = true;
            CanExportFiles = true;
        }

        Explorer Explore(string location)
        {
            //! propagate the provider, or performance sucks
            var newExplorer = new ACDExplorer(Client, Panel, location);

            return newExplorer;
        }


        /// <inheritdoc/>
        public override Explorer ExploreDirectory(ExploreDirectoryEventArgs args)
        {
            if (args == null) return null;

            var path = ((Panel.CurrentFile.Data as Hashtable)["fsitem"] as FSItem).Path;

            return Explore(path);
        }

        /// <inheritdoc/>
        public override Explorer ExploreParent(ExploreParentEventArgs args)
        {
            var path = Path.GetDirectoryName(Location);

            return Explore(path);
        }

        /// <inheritdoc/>
        public override IList<FarFile> GetFiles(GetFilesEventArgs args)
        {
            string path = Location;

            return Client.GetFiles(path);
        }

        /// <inheritdoc/>
        public override void ImportFiles(ImportFilesEventArgs args)
        {
            if (args != null) args.Result = JobResult.Ignore;

            //Log.Source.TraceInformation("Progress: {0}", args.Files[0].Name);
            //Log.Source.TraceInformation("Current directory: {0}", Far.Api.Panel.CurrentFile.Name);

            if (string.IsNullOrWhiteSpace(Far.Api.Panel2.CurrentDirectory))
            {
                return;
            }

            Task <FSItem> task = Client.FetchNode(Far.Api.Panel2.CurrentDirectory);
            task.Wait();
            if (!task.IsCompleted || task.Result == null)
            {
                return;
            }

            var item = task.Result;

            foreach (var file in args.Files)
            {
                //var path = Path.Combine(Far.Api.Panel2.CurrentDirectory, item.Name);

                var form = new Tools.ProgressForm();
                form.Activity = "Uploading...";
                form.Title = "Amazon Cloud Drive - File Upload Progress";
                form.CanCancel = true;
                form.SetProgressValue(0, item.Length);
                form.Canceled += (object sender, EventArgs e) =>
                {
                    form.Close();
                };

                Task uploadTask = Client.UploadFile(item, Path.Combine(Far.Api.Panel.CurrentDirectory, file.Name), form);
                var cs = new CancellationTokenSource();
                var token = cs.Token;
                /*
                token.Register(() =>
                {
                    form.Close();
                });*/
                var _task = Task.Factory.StartNew(() =>
                {
                    form.Show();
                }, token);
                uploadTask.Wait();
            }
        }

        /// <inheritdoc/>
        public override void ExportFiles(ExportFilesEventArgs args)
        {
            if (args != null) args.Result = JobResult.Ignore;

            if (Far.Api.Panel2.IsPlugin)
            {
                // How to copy files to plugins??
                args.Result = JobResult.Ignore;
                return;
            }

            if (Far.Api.Panel2.CurrentDirectory == null)
            {
                // how can it be?
                args.Result = JobResult.Ignore;
                return;
            }

            foreach (var file in args.Files)
            {
                var item = ((file.Data as Hashtable)["fsitem"] as FSItem);
                var path = Path.Combine(Far.Api.Panel2.CurrentDirectory, item.Name);

                var form = new Tools.ProgressForm();
                form.Activity = "Downloading...";
                form.Title = "Amazon Cloud Drive - File Download Progress";
                form.CanCancel = true;
                form.SetProgressValue(0, item.Length);
                form.Canceled += (object sender, EventArgs e) =>
                {
                    form.Close();
                };

                Task task = Client.DownloadFile(item, path, form);
                var cs = new CancellationTokenSource();
                var token = cs.Token;
                /*
                token.Register(() =>
                {
                    form.Close();
                });*/
                var _task = Task.Factory.StartNew(() =>
                {
                    form.Show();
                }, token);
                task.Wait();
            }
        }

        /// <inheritdoc/>
        public override Panel CreatePanel()
		{
			Panel = new ACDPanel(this);
            return Panel;
		}
	}
}
