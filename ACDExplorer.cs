
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
            CanDeleteFiles = true;
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

        /// <summary>
        /// Progress Form builder
        /// </summary>
        /// <param name="activity">Current activity description (form text)</param>
        /// <param name="title">Form title</param>
        /// <returns></returns>
        private Tools.ProgressForm GetProgressForm(string activity, string title)
        {
            var form = new Tools.ProgressForm();
            form.Activity = activity;
            form.Title = title;
            form.CanCancel = true;
            form.Canceled += (object sender, EventArgs e) =>
            {
                form.Close();
                // we cannot throw and exception here, since it is another thread
                //throw new OperationCanceledException();
            };

            var _task = Task.Factory.StartNew(() =>
            {
                form.Show();
            });

            return form;
        }

        /// <inheritdoc/>
        public override void ImportFiles(ImportFilesEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            //Log.Source.TraceInformation("Progress: {0}", args.Files[0].Name);

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

            args.Result = JobResult.Incomplete;

            var form = GetProgressForm("Uploading...", "Amazon Cloud Drive - File Upload Progress");
            form.SetProgressValue(0, task.Result.Length);

            foreach (var file in args.Files)
            {
                Task uploadTask = Client.UploadFile(task.Result, Path.Combine(Far.Api.Panel.CurrentDirectory, file.Name), form);

                try
                {
                    uploadTask.Wait();
                }
                catch (AggregateException ae)
                {
                    ae.Handle((x) =>
                    {
                        if (x is TaskCanceledException)
                        {
                            form.Complete();
                            return true; // processed
                        }
                        return false; // unprocessed
                    });
                    break;
                }
                /*
                catch
                {
                    // what to do in case of any other exception?
                }
                */
            }
            form.Complete();

            // TODO: handle somehow incomplete state
            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override void ExportFiles(ExportFilesEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            if (Far.Api.Panel2.IsPlugin)
            {
                // How to copy files to plugins??
                return;
            }

            if (Far.Api.Panel2.CurrentDirectory == null)
            {
                // how can it be?
                return;
            }

            args.Result = JobResult.Incomplete;
            var form = GetProgressForm("Downloading...", "Amazon Cloud Drive - File Download Progress");

            foreach (var file in args.Files)
            {
                var item = ((file.Data as Hashtable)["fsitem"] as FSItem);
                var path = Path.Combine(Far.Api.Panel2.CurrentDirectory, item.Name);

                form.SetProgressValue(0, item.Length);

                Task downloadTask = Client.DownloadFile(item, path, form);
                try
                {
                    downloadTask.Wait();
                }
                catch (AggregateException ae)
                {
                    ae.Handle((x) =>
                    {
                        if (x is TaskCanceledException)
                        {
                            form.Complete();
                            return true; // processed
                        }
                        return false; // unprocessed
                    });
                    break;
                }
                /*
                catch
                {
                    // what to do in case of any other exception?
                }
                */
            }
            form.Complete();

            // TODO: handle somehow incomplete state
            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override void DeleteFiles(DeleteFilesEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            args.Result = JobResult.Incomplete;
            var form = GetProgressForm("Deleting...", "Amazon Cloud Drive - File Deletion Progress");

            foreach (var file in args.Files)
            {
                var item = ((file.Data as Hashtable)["fsitem"] as FSItem);
                var path = Path.Combine(Far.Api.Panel2.CurrentDirectory, item.Name);

                Task downloadTask = Client.DeleteFile(item, form);
                try
                {
                    downloadTask.Wait();
                }
                catch (AggregateException ae)
                {
                    ae.Handle((x) =>
                    {
                        if (x is TaskCanceledException)
                        {
                            form.Complete();
                            return true; // processed
                        }
                        return false; // unprocessed
                    });
                    break;
                }
                /*
                catch
                {
                    // what to do in case of any other exception?
                }
                */
            }
            form.Complete();

            // TODO: handle somehow incomplete state
            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override Panel CreatePanel()
		{
			Panel = new ACDPanel(this);
            return Panel;
		}
	}
}
