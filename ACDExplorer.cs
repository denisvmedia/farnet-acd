
using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using System.Threading;
using FarNet.ACD.Exceptions;

namespace FarNet.ACD
{
    /// <summary>
    /// Panel file explorer to view .resources file data.
    /// </summary>
    class ACDExplorer : Explorer
    {
        const int MAX_UPLOAD_TRIES = 3;
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
            CanCreateFile = true;
            CanRenameFile = true;
            CanGetContent = true;
            CanExploreLocation = true;
        }

        Explorer Explore(string location)
        {
            //! propagate the provider, or performance sucks
            var newExplorer = new ACDExplorer(Client, Panel, location);

            return newExplorer;
        }

        public override Explorer ExploreLocation(ExploreLocationEventArgs args)
        {
            if (args == null) return null;

            return Explore(Path.Combine(Panel.CurrentDirectory, args.Location));
        }

        /// <inheritdoc/>
        public override Explorer ExploreDirectory(ExploreDirectoryEventArgs args)
        {
            if (args == null) return null;

            //var path = ((Panel.CurrentFile.Data as Hashtable)["fsitem"] as FSItem).Path;
            //var item = CacheStorage.GetItemById(Panel.CurrentFile.Description); // description === Id
            var item = Client.FetchNode(Path.Combine(Panel.CurrentDirectory, Panel.CurrentFile.Name)).Result;

            return Explore(item.Path);
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

            if (args.Mode != ExplorerModes.Find)
            {
                Panel.ShowRetryDialogInGetFiles = true;
            }

            do
            {
                Exception failure = null;

                try
                {
                    return Client.GetFiles(path);
                }
                catch (AggregateException ae)
                {
                    ae.Handle((x) =>
                    {
                        if (x is TaskCanceledException)
                        {
                            return true;
                        }

                        failure = x;

                        return true;
                    });

                    if (Panel.ShowRetryDialogInGetFiles)
                    {
                        var dlg = new AutoRetryDialog("Exception during execution: " + UserFriendlyException(failure), "Get files error");
                        var res = dlg.Display();
                        if (res == 0)
                        {
                            continue;
                        }
                        if (args.Mode == ExplorerModes.Find && res == 1)
                        {
                            Panel.ShowRetryDialogInGetFiles = false;
                        }
                    }

                    break;
                }
            } while (true);

            return new List<FarFile>();
        }

        /// <inheritdoc/>
        public override void ImportFiles(ImportFilesEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            //Log.Source.TraceInformation("Progress: {0}", args.Files[0].Name);

            // Current directory cannot be empty
            if (string.IsNullOrWhiteSpace(Far.Api.Panel2.CurrentDirectory))
            {
                return;
            }

            // We cannot copy ".." (parent directory)
            if (args.Files.Count == 1 && args.Files[0].Name == "..")
            {
                return;
            }

            // set job result to incomplete (should be used if operation is cancelled in the middle)
            args.Result = JobResult.Incomplete;

            var form = GetProgressForm("Uploading...", "Amazon Cloud Drive - File Upload Progress");
            form.LineCount = 3;

            // we use this event to pause upload thread
            var pauseThreadEvent = new ManualResetEvent(false);

            // ACD file to be replaced
            FarFile fileToReplace = null;

            // indicates to the Form.Idled handler that we have a file that can be replaced
            int replaceUserChoice = -1;

            // File name to use with ACD
            string acdFileName = "";

            // Exception that indicates failure in the thread
            Exception failure = null;

            var jobThread = new Thread(() =>
            {
                // Progress Interrupt Event
                var wh = GetResetEvent("Do you wish to interrupt upload?", "Upload", form);

                // recursively searche for all the files and directories that can be uploaded
                List<FarFile> files = GetFarFilesRecursive(args.Files);

                // list of directories that already exist on remote (for caching purposes)
                Dictionary<string, FSItem> dirs = new Dictionary<string, FSItem>();
                long progress = 0;
                long maxprogress = 0;

                foreach (var file in files)
                {
                    maxprogress += file.Length;
                }
                form.SetProgressValue(0, maxprogress);

                foreach (var file in files)
                {
                    // Get file name to use with ACD
                    acdFileName = Far.Api.Panel2.CurrentDirectory.TrimEnd('\\') + file.Name.Replace(Panel.CurrentDirectory, "");
                    var parentAcdFileName = Path.GetDirectoryName(acdFileName);

                    // If a file is a directory, then we should try to create it.
                    // If the directory already exists, ClientCreateDirectory(...) internally will fetch that node.
                    // So, basically it's "Get OR Create Directory".
                    if (file.IsDirectory)
                    {
                        form.Activity = "Creating directory " + Utility.ShortenString(acdFileName, 20);
                        FSItem parent = dirs.ContainsKey(parentAcdFileName) ? dirs[parentAcdFileName] : null;
                        Task <FSItem> mdTask = Client.CreateDirectory(acdFileName, parent);
                        mdTask.Wait();
                        // let's cache it
                        dirs.Add(acdFileName, mdTask.Result);
                    }
                    else
                    {
                        FSItem parent;
                        // if we have file's parent directory in our cache, then no need to fetch it again
                        // this also helps in cases when we create a directory on the remote, but it doesn't appear immediately
                        if (dirs.ContainsKey(parentAcdFileName))
                        {
                            parent = dirs[parentAcdFileName];
                        }
                        else
                        {
                            // Fetch Node item for the current directory
                            form.Activity = "Getting directory information " + Utility.ShortenString(parentAcdFileName, 20);
                            Task<FSItem> task = Client.FetchNode(parentAcdFileName);
                            task.Wait();
                            if (!task.IsCompleted || task.Result == null)
                            {
                                // TODO: dialog is not closed here!
                                return;
                            }
                            parent = task.Result;
                            // cache it
                            dirs.Add(parentAcdFileName, parent);
                        }

                        try
                        {
                            // trying to upload the file
                            form.Activity = "Uploading " + Utility.ShortenString(file.Name, 20);
                            progress = DoUpload(file, parent, form, wh, progress, maxprogress);
                        }
                        catch (RemoteFileExistsException) // thrown manually
                        {
                            // it might happen so that the file already exists
                            fileToReplace = file;
                            
                            // pause the thread until the user makes a decision
                            pauseThreadEvent.Reset();
                            pauseThreadEvent.WaitOne();
                        }

                        // Did the user decide to cancel upload?
                        if (form.IsCompleted)
                        {
                            break;
                        }

                        // Do we have a file to replace?
                        if (fileToReplace == null)
                        {
                            continue;
                        }

                        // reset var
                        fileToReplace = null;

                        // Check user decision on how to deal with file replacements
                        switch (replaceUserChoice)
                        {
                            case -1: // escape
                            case 1: // No
                            case 3: // No to all
                                progress += file.Length; // set progress to go futher if the file is skipped
                                form.SetProgressValue(progress, maxprogress);
                                continue;
                        }

                        // finally replace the file
                        try
                        {
                            progress = DoReplace(file, parent, form, wh, progress, maxprogress);
                        }
                        catch (Exception ex)
                        {
                            failure = ex;
                            pauseThreadEvent.Reset();
                            pauseThreadEvent.WaitOne();
                            break;
                        }

                        // Did the user decide to cancel upload (replace)?
                        if (form.IsCompleted)
                        {
                            break;
                        }
                    }
                }

                // finally complete (and close) the form
                form.Complete();
            });

            // here we check if there is a file that can be replaced
            form.Idled += (object sender, EventArgs e) =>
            {
                if (failure != null) {
                    var errorMsg = failure is AggregateException ? (failure as AggregateException).Flatten().ToString() : failure.ToString();
                    Far.Api.Message("Unhandled exception during execution: " + errorMsg, "Upload error", MessageOptions.Ok | MessageOptions.Warning);
                    pauseThreadEvent.Set();
                    return;
                }

                // no file to replace => nothing to do
                if (fileToReplace == null)
                {
                    return;
                }

                string[] buttons = new string[] { "&Yes", "&No", "Yes for &all", "No for all" };
                if (replaceUserChoice != 2 && replaceUserChoice != 3)
                {
                    replaceUserChoice = Far.Api.Message(
                        string.Format("Do you want to replace {0}?", acdFileName),
                        "Upload",
                        MessageOptions.Warning,
                        buttons
                    );
                }

                // unpause the thread
                pauseThreadEvent.Set();
            };
            jobThread.Start();
            // wait a little bit
            Thread.Sleep(500);
            form.Show();

            // TODO: handle somehow incomplete state
            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override void ExportFiles(ExportFilesEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            args.Result = JobResult.Incomplete;
            var form = GetProgressForm("Downloading...", "Amazon Cloud Drive - File Download Progress");
            var wh = GetResetEvent("Do you wish to interrupt download?", "Download", form);

            var jobThread = new Thread(() =>
            {
                foreach (var file in args.Files)
                {
                    //var item = ((file.Data as Hashtable)["fsitem"] as FSItem);
                    //var item = CacheStorage.GetItemById(file.Description); // description === Id
                    var item = Client.FetchNode(Path.Combine(Panel.CurrentDirectory, file.Name)).Result;
                    var path = Path.Combine(Far.Api.Panel2.CurrentDirectory, item.Name);

                    form.SetProgressValue(0, item.Length);

                    Task downloadTask = Client.DownloadFile(item, path, form, wh);

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
                }
                form.Complete();
            });

            jobThread.Start();
            Thread.Sleep(500);
            form.Show();

            // TODO: handle somehow incomplete state
            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override void CreateFile(CreateFileEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            args.Result = JobResult.Incomplete;

            var list = new List<string>(1);
            list.Add("Folder name:");
            var dlg = new InputDialog()
            {
                Prompt = list,
                Caption = "Create folder",
                //Text = "",
            };

            if (!dlg.Show())
            {
                args.Result = JobResult.Ignore;
                return;
            }

            var path = Path.Combine(Panel.CurrentDirectory, dlg.Text);
            Task<FSItem> mdTask = Client.CreateDirectory(path);

            var form = GetProgressForm("Creating the directory...", "Amazon Cloud Drive - Create Directory");
            form.CanCancel = false;
            var jobThread = new Thread(() =>
            {
                try
                {
                    mdTask.Wait();
                    if (mdTask.Result != null)
                    {
                        args.Result = JobResult.Done;
                        args.PostFile = Client.GetFarFileFromFSItem(mdTask.Result);
                        using (var wh = new ManualResetEvent(false))
                        {
                            wh.WaitOne(3000);
                        }
                        form.Complete();
                    }
                    else
                    {
                        form.Close();
                    }
                }
                catch
                {
                    form.Close();
                }
            });
            jobThread.Start();
            form.Show();
            if (form.IsCompleted)
            {
                Panel.NeedsNewFiles = true;
                Panel.Redraw();
                return;
            }
            Far.Api.Message(new MessageArgs()
            {
                Text = "Cannot create a folder",
                Caption = "Error",
                Options = MessageOptions.Warning,
            });
            args.Result = JobResult.Ignore;
        }

        /// <inheritdoc/>
        public override void GetContent(GetContentEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            args.Result = JobResult.Incomplete;
            args.CanSet = true;

            Tools.ProgressForm form = null;
            int res = -1000;

            do
            {
                form = GetProgressForm("Downloading...", "Amazon Cloud Drive - File Download Progress");
                var wh = GetResetEvent("Do you wish to interrupt download?", "Download", form);

                Exception failure = null;

                var jobThread = new Thread(() =>
                {
                    var file = args.File;
                    //var item = ((file.Data as Hashtable)["fsitem"] as FSItem);
                    //var item = CacheStorage.GetItemById(file.Description); // description === Id
                    var item = Client.FetchNode(Path.Combine(Panel.CurrentDirectory, args.File.Name)).Result;
                    var path = args.FileName;

                    form.SetProgressValue(0, item.Length);

                    Task downloadTask = Client.DownloadFile(item, path, form, wh);

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
   
                            failure = x;
                            form.Complete();

                            return true; // we catch any aggregated exception and let the user retry the operation
                        });
                        form.Close();
                        return;
                    }
                    form.Complete();
                });

                jobThread.Start();
                Thread.Sleep(500);
                form.Show();

                if (failure != null)
                {
                    var dlg = new AutoRetryDialog("Error during execution: " + UserFriendlyException(failure), "Download error");
                    res = dlg.Display();
                    if (res == 0)
                    {
                        continue;
                    }
                }

                break;
            } while (true);

            if (form.IsCompleted)
            {
                if (res == 0 || res == -1)
                {
                    args.Result = JobResult.Ignore;
                }
                else
                {
                    args.Result = JobResult.Done;
                }
            }
            else
            {
                args.Result = JobResult.Ignore;
            }
        }

        /// <inheritdoc/>
        public override void DeleteFiles(DeleteFilesEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            // we cannot delete ".."
            if (args.Files.Count == 1 && args.Files[0].Name == "..")
            {
                return;
            }

            args.Result = JobResult.Incomplete;

            { // Delete confirmation
                int res;
                if (args.Files.Count == 1)
                {
                    res = Far.Api.Message(string.Format("Do you wish to delete {0}?", args.Files[0].Name), "Delete", MessageOptions.YesNo | MessageOptions.Warning);
                }
                else
                {
                    res = Far.Api.Message(string.Format("Do you wish to delete {0} files?", args.Files.Count), "Delete", MessageOptions.YesNo | MessageOptions.Warning);
                }

                // Esc = -1 (cancel?), Yes = 0, No = 1
                if (res != 0)
                {
                    return;
                }
            }

            var form = GetProgressForm("Deleting...", "Amazon Cloud Drive - File Deletion Progress");

            var jobThread = new Thread(() =>
            {
                foreach (var file in args.Files)
                {
                    //var item = ((file.Data as Hashtable)["fsitem"] as FSItem);
                    var item = Client.FetchNode(file.Name).Result;
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
                }
                form.Complete();
            });

            jobThread.Start();
            // wait a little bit
            Thread.Sleep(500);
            form.Show();

            // TODO: handle somehow incomplete state
            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override void RenameFile(RenameFileEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            //var item = ((args.File.Data as Hashtable)["fsitem"] as FSItem);
            var item = Client.FetchNode(Path.Combine(Panel.CurrentDirectory, args.File.Name)).Result;
            IInputBox input = Far.Api.CreateInputBox();
            input.EmptyEnabled = true;
            input.Title = "Rename";
            input.Prompt = "New name";
            input.History = "Copy";
            input.Text = args.File.Name;
            input.ButtonsAreVisible = true;
            if (!input.Show() || input.Text == args.File.Name || string.IsNullOrEmpty(input.Text))
            {
                return;
            }
            // set new name and post it
            args.Parameter = input.Text;
            args.PostName = input.Text;

            Task<bool> task = Client.RenameFile(item, input.Text);
            task.Wait();

            if (!task.Result)
            {
                Far.Api.Message(new MessageArgs()
                {
                    Text = "Cannot rename the file",
                    Caption = "Error",
                    Options = MessageOptions.Warning,
                });
            }

            args.Result = JobResult.Done;
        }

        /// <inheritdoc/>
        public override Panel CreatePanel()
		{
			Panel = new ACDPanel(this);
            return Panel;
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

            return form;
        }

        /// <summary>
        /// Reset Event that is used to pause threads
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private ManualResetEvent GetResetEvent(string message, string title, Tools.ProgressForm form)
        {
            var wh = new ManualResetEvent(true); // start in a signaled way, i.e. resumed state
            form.Canceling += (object sender, Forms.ClosingEventArgs cancelArgs) =>
            {
                wh.Reset(); // pause Upload

                if (Far.Api.Message(message, title, MessageOptions.YesNo | MessageOptions.Warning) != 0)
                {
                    cancelArgs.Ignore = true;
                }

                wh.Set(); // resume Upload
            };

            return wh;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="file"></param>
        /// <param name="parentItem"></param>
        /// <param name="form"></param>
        private long DoUpload(FarFile file, FSItem parentItem, Tools.ProgressForm form, ManualResetEvent wh, long progress, long maxprogress)
        {
            Exception exists = null;
            var ACDFilePath = Path.Combine(parentItem.Path, Path.GetFileName(file.Name));

            Task<FSItem> item = Client.FetchNode(ACDFilePath);
            item.Wait();
            if (item.Result != null)
            {
                throw new RemoteFileExistsException("File exists " + ACDFilePath);
            }

            for (var i = 0; i < MAX_UPLOAD_TRIES; i++)
            {
                Task<long> uploadNewTask = Client.UploadNewFile(parentItem, file.Name, form, wh, progress, maxprogress);
                Azi.Tools.HttpWebException webEx = null;

                form.Activity += ".";

                try
                {
                    uploadNewTask.Wait();
                    return uploadNewTask.Result;
                }
                catch (AggregateException ae)
                {
                    ae.Handle((x) =>
                    {
                        if (x is TaskCanceledException)
                        {
                            return true; // processed
                        }

                        if (x is Azi.Tools.HttpWebException && (x as Azi.Tools.HttpWebException).StatusCode == System.Net.HttpStatusCode.Conflict)
                        {
                            exists = x;
                            return true; // processed
                        }

                        if (x is Azi.Tools.HttpWebException && (x as Azi.Tools.HttpWebException).StatusCode == System.Net.HttpStatusCode.InternalServerError)
                        {
                            webEx = (Azi.Tools.HttpWebException)x;
                            return true;
                        }

                        if (x is Azi.Tools.HttpWebException && (x as Azi.Tools.HttpWebException).StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                        {
                            webEx = (Azi.Tools.HttpWebException)x;
                            return true;
                        }

                        if (x is Azi.Tools.HttpWebException && (x as Azi.Tools.HttpWebException).StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                        {
                            webEx = (Azi.Tools.HttpWebException)x;
                            return true;
                        }

                        return false; // unprocessed
                    });
                }

                if (webEx == null)
                {
                    break;
                }
            }

            if (exists != null)
            {
                throw new RemoteFileExistsException("File exists " + ACDFilePath, exists);
            }

            return progress;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="file"></param>
        /// <param name="parentItem"></param>
        /// <param name="form"></param>
        private long DoReplace(FarFile file, FSItem parentItem, Tools.ProgressForm form, ManualResetEvent wh, long progress, long maxprogress)
        {
            Task<long> replaceTask = Client.ReplaceFile(parentItem, file.Name, form, wh, progress, maxprogress);

            try
            {
                replaceTask.Wait();
                return replaceTask.Result;
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
            }

            return progress;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="Files"></param>
        /// <returns></returns>
        private List<FarFile> GetFarFilesRecursive(IList<FarFile> Files)
        {
            List<FarFile> files = new List<FarFile>();

            foreach (var file in Files)
            {
                var path = Path.Combine(Panel.CurrentDirectory, file.Name);
                if (file.IsDirectory)
                {
                    foreach (string dfile in Utility.GetFiles(path))
                    {
                        SetFile farfile;
                        if (Directory.Exists(dfile))
                        {
                            farfile = new SetFile(new DirectoryInfo(dfile), true);
                        }
                        else
                        {
                            farfile = new SetFile(new FileInfo(dfile), true);
                        }
                        files.Add(farfile);
                    }
                }
                else
                {
                    file.Name = path;
                    files.Add(file);
                }
            }

            return files;
        }

        private string UserFriendlyException(Exception ex)
        {
            string errorMsg = "";
            if (ex is Azi.Tools.HttpWebException)
            {
                errorMsg = string.Format(
                    "{0}: {1}", (ex as Azi.Tools.HttpWebException).StatusCode, (ex as Azi.Tools.HttpWebException).Message);
            }
            else if (ex is System.IO.IOException)
            {
                errorMsg = ex.Message;
            }
            else
            {
                // unknown exceptions will be tracked in full
                errorMsg = ex.ToString();
            }

            return errorMsg;
        }
    }
}
