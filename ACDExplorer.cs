
using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using System.Threading;
using FarNet.ACD.Exceptions;
using Azi.Amazon.CloudDrive;

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

            return Explore(Path.Combine(Far.Api.Panel.CurrentDirectory, args.Location));
        }

        /// <inheritdoc/>
        public override Explorer ExploreDirectory(ExploreDirectoryEventArgs args)
        {
            if (args == null) return null;

            //var path = ((Panel.CurrentFile.Data as Hashtable)["fsitem"] as FSItem).Path;
            //var item = CacheStorage.GetItemById(Panel.CurrentFile.Description); // description === Id
            var item = Client.FetchNode(Path.Combine(Far.Api.Panel.CurrentDirectory, Far.Api.Panel.CurrentFile.Name)).Result;

            return Explore(item.Path);
        }

        /// <inheritdoc/>
        public override Explorer ExploreParent(ExploreParentEventArgs args)
        {
            var path = Path.GetDirectoryName(Location);

            return Explore(path);
        }

        /// <inheritdoc/>
        public override void SetFile(SetFileEventArgs args)
        {
            if (args == null) return;
            if (args != null) args.Result = JobResult.Ignore;

            // set job result to incomplete (should be used if operation is cancelled in the middle)
            args.Result = JobResult.Incomplete;

            var form = GetProgressForm("Uploading...", "Amazon Cloud Drive - File Upload Progress");
            form.LineCount = 3;

            // we use this event to pause upload thread
            var pauseThreadEvent = new ManualResetEvent(false);

            // indicates user choice in retry (-1: Cancel, 0: Retry, 1: Abort, 2: Ignore)
            int retryUserChoice = -1;

            // File name to use with ACD
            string acdFileName = "";

            // Exception that indicates failure in the thread
            Exception failure = null;

            var jobThread = new Thread(() =>
            {
                // Progress Interrupt Event
                var wh = GetResetEvent("Do you wish to interrupt upload?", "Upload", form);

                long progress = 0;
                long maxprogress = new FileInfo(args.FileName).Length;
                form.SetProgressValue(0, maxprogress);

                // Get file name to use with ACD
                acdFileName = Path.Combine(Panel.CurrentDirectory, args.File.Name);
                var parentAcdFileName = Path.GetDirectoryName(acdFileName);

                do
                {
                    try
                    {
                        FSItem parent;

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

                        progress = DoReplace(args.FileName, parent, form, wh, progress, maxprogress, acdFileName);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;

                        // pause the thread until the user makes a decision
                        pauseThreadEvent.Reset();
                        pauseThreadEvent.WaitOne();

                        if (retryUserChoice == 0) // retry upload
                        {
                            continue;
                        }
                    }

                    break;
                } while (true);

                form.Activity = "Syncing the directory...";
                CacheStorage.RemoveItems(Panel.CurrentDirectory);
                using (var _wh = new ManualResetEvent(false))
                {
                    _wh.WaitOne(3000);
                }

                // finally complete (and close) the form
                form.Complete();
            });

            // here we check if there is a file that can be replaced
            form.Idled += (object sender, EventArgs e) =>
            {
                if (failure != null)
                {
                    var dlg = new AutoRetryDialog(UserFriendlyException(failure), "Upload Error");
                    failure = null;
                    retryUserChoice = dlg.Display();
                    pauseThreadEvent.Set();
                    return;
                }
            };
            jobThread.Start();
            // wait a little bit
            Thread.Sleep(500);
            form.Show();

            Panel.Update(true);

            args.Result = JobResult.Done;
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
                    return GetFiles(path);
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

            // Current directory cannot be empty
            if (string.IsNullOrWhiteSpace(Panel.CurrentDirectory))
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

            // indicates user choice in retry (-1: Cancel, 0: Retry, 1: Abort, 2: Ignore)
            int retryUserChoice = -1;

            // File name to use with ACD
            string acdFileName = "";

            // Exception that indicates failure in the thread
            Exception failure = null;

            var jobThread = new Thread(() =>
            {
                // Progress Interrupt Event
                var wh = GetResetEvent("Do you wish to interrupt upload?", "Upload", form);

                long totalsize;
                // recursively searche for all the files and directories that can be uploaded
                List<FarFile> files = GetLocalFarFilesRecursive(args.Files, args.DirectoryName, out totalsize);

                // list of directories that already exist on remote (for caching purposes)
                Dictionary<string, FSItem> dirs = new Dictionary<string, FSItem>();
                long progress = 0;

                form.SetProgressValue(0, totalsize);

                foreach (var file in files)
                {
                    // Get file name to use with ACD
                    acdFileName = Panel.CurrentDirectory.TrimEnd('\\') + file.Name.Replace(args.DirectoryName, ""); // TODO: better way to cut the directory name
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

                    Upload:
                        try
                        {
                            // trying to upload the file
                            form.Activity = "Uploading " + Utility.ShortenString(file.Name, 20);
                            progress = DoUpload(file, parent, form, wh, progress, totalsize);
                        }
                        catch (RemoteFileExistsException) // thrown manually
                        {
                            // it might happen so that the file already exists
                            fileToReplace = file;

                            // pause the thread until the user makes a decision
                            pauseThreadEvent.Reset();
                            pauseThreadEvent.WaitOne();
                        }
                        catch (Exception ex)
                        {
                            // handle exception: retry, abort or ignore?
                            failure = ex;

                            // pause the thread until the user makes a decision
                            pauseThreadEvent.Reset();
                            pauseThreadEvent.WaitOne();
                            if (retryUserChoice == 0) // retry upload
                            {
                                goto Upload;
                            }

                            if (retryUserChoice == 1) // break, stop upload
                            {
                                break;
                            }

                            if (retryUserChoice == -1 || retryUserChoice == 2) // cancel or ignore
                            {
                                progress += file.Length; // set progress to go futher if the file is skipped
                                form.SetProgressValue(progress, totalsize);
                                continue;
                            }
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
                                form.SetProgressValue(progress, totalsize);
                                continue;
                        }

                    Replace:
                        // finally replace the file
                        try
                        {
                            progress = DoReplace(file.Name, parent, form, wh, progress, totalsize);
                        }
                        catch (Exception ex)
                        {
                            failure = ex;

                            // pause the thread until the user makes a decision
                            pauseThreadEvent.Reset();
                            pauseThreadEvent.WaitOne();

                            if (retryUserChoice == 0) // retry upload
                            {
                                goto Replace;
                            }

                            if (retryUserChoice == 1) // break, stop upload
                            {
                                break;
                            }

                            if (retryUserChoice == -1 || retryUserChoice == 2) // cancel or ignore
                            {
                                progress += file.Length; // set progress to go futher if the file is skipped
                                form.SetProgressValue(progress, totalsize);
                                continue;
                            }
                        }

                        // Did the user decide to cancel upload (replace)?
                        if (form.IsCompleted)
                        {
                            break;
                        }
                    }
                }

                form.Activity = "Syncing the directory...";
                CacheStorage.RemoveItems(Panel.CurrentDirectory);
                using (var _wh = new ManualResetEvent(false))
                {
                    _wh.WaitOne(3000);
                }

                // finally complete (and close) the form
                form.Complete();
            });

            // here we check if there is a file that can be replaced
            form.Idled += (object sender, EventArgs e) =>
            {
                if (failure != null) {
                    var dlg = new AutoRetryDialog(UserFriendlyException(failure), "Upload Error");
                    failure = null;
                    retryUserChoice = dlg.Display();
                    pauseThreadEvent.Set();
                    return;
                }

                // no file to replace => nothing to do
                if (fileToReplace == null)
                {
                    return;
                }

                if (replaceUserChoice != 2 && replaceUserChoice != 3)
                {
                    string[] buttons = new string[] { "&Yes", "&No", "Yes for &all", "No for all" };
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

            Far.Api.Panel2.Update(true);

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
            form.LineCount = 3;

            // we use this event to pause upload thread
            var pauseThreadEvent = new ManualResetEvent(false);

            // indicates user choice in retry (-1: Cancel, 0: Retry, 1: Abort, 2: Ignore)
            int retryUserChoice = -1;

            // indicates to the Form.Idled handler that we have a file that can be replaced
            int replaceUserChoice = -1;

            var wh = GetResetEvent("Do you wish to interrupt download?", "Download", form);
            long totalsize;
            long progress = 0;
            Exception failure = null;
            string fileToReplace = "";

            var files = GetRemoteFarFilesRecursive(args.Files, Far.Api.Panel.CurrentDirectory, out totalsize);

            form.SetProgressValue(0, totalsize);

            var jobThread = new Thread(() =>
            {
                foreach (var filePath in files.Keys)
                {
                    Download:
                    FSItem item = null;
                    string localFilePath = Path.Combine(Far.Api.Panel2.CurrentDirectory, filePath.Substring(Far.Api.Panel.CurrentDirectory.Length));

                    try
                    {
                        var itemData = Client.FetchNode(filePath);
                        itemData.Wait();
                        item = itemData.Result;

                        if (File.Exists(localFilePath))
                        {
                            fileToReplace = localFilePath;
                            // pause the thread until the user makes a decision
                            pauseThreadEvent.Reset();
                            pauseThreadEvent.WaitOne();

                            switch (replaceUserChoice)
                            {
                                case -1: // escape
                                case 1: // No
                                case 3: // No to all
                                    progress += item.Length; // set progress to go futher if the file is skipped
                                    form.SetProgressValue(progress, totalsize);
                                    continue;
                            }
                        }
                        progress = DoDownload(localFilePath, item, form, wh, progress, totalsize);
                    }
                    catch (Exception ex)
                    {
                        if (ex is AggregateException)
                        {
                            Exception innerException = null;
                            (ex as AggregateException).Handle((x) =>
                            {
                                if (x is TaskCanceledException)
                                {
                                    form.Complete();
                                    return true; // processed
                                }

                                innerException = x;
                                return true;
                            });

                            failure = innerException;
                        }
                        else
                        {
                            failure = ex;
                        }

                        // pause the thread until the user makes a decision
                        pauseThreadEvent.Reset();
                        pauseThreadEvent.WaitOne();

                        File.Delete(localFilePath);

                        if (retryUserChoice == 0) // retry upload
                        {
                            goto Download;
                        }

                        if (retryUserChoice == 1) // break, stop upload
                        {
                            break;
                        }

                        if (retryUserChoice == -1 || retryUserChoice == 2) // cancel or ignore
                        {
                            progress += item.Length; // set progress to go futher if the file is skipped
                            form.SetProgressValue(progress, totalsize);
                            continue;
                        }
                    }
                }
                form.Complete();
            });

            // here we check if there is a file that can be replaced
            form.Idled += (object sender, EventArgs e) =>
            {
                if (failure != null)
                {
                    var dlg = new AutoRetryDialog(UserFriendlyException(failure), "Download Error");
                    failure = null;
                    retryUserChoice = dlg.Display();
                    pauseThreadEvent.Set();
                    return;
                }

                if (fileToReplace != "" && replaceUserChoice != 2 && replaceUserChoice != 3)
                {
                    string[] buttons = new string[] { "&Yes", "&No", "Yes for &all", "No for all" };
                    replaceUserChoice = Far.Api.Message(
                        string.Format("Do you want to replace {0}?", fileToReplace),
                        "Upload",
                        MessageOptions.Warning,
                        buttons
                    );
                    fileToReplace = "";
                }

                // unpause the thread
                pauseThreadEvent.Set();
            };

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

            var form = GetProgressForm("Creating the directory...", "Amazon Cloud Drive - Create Directory");
            form.CanCancel = false;
            Exception failure = null;

            var jobThread = new Thread(() =>
            {
                try
                {
                    var path = Path.Combine(Far.Api.Panel.CurrentDirectory, dlg.Text);
                    Task<FSItem> mdTask = Client.CreateDirectory(path, null, false);
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
                        throw new Exception("Cannot create directory (Unknown failure)");
                    }
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
                        form.Close();
                        return true; // we catch any aggregated exception and let the user retry the operation
                    });
                }
                catch (Exception ex)
                {
                    failure = ex;
                    form.Close();
                }
            });
            jobThread.Start();
            form.Show();
            if (form.IsCompleted)
            {
                CacheStorage.RemoveItems(Panel.CurrentDirectory);
                Panel.Update(true);
                return;
            }
            Far.Api.Message(new MessageArgs()
            {
                Text = UserFriendlyException(failure),
                Caption = "Create Directory Error",
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

                    // TODO: catch exception!
                    var item = Client.FetchNode(Path.Combine(Far.Api.Panel.CurrentDirectory, args.File.Name)).Result;
                    var path = args.FileName;

                    form.SetProgressValue(0, item.Length);

                    Task<long> downloadTask = Client.DownloadFile(item, path, form, wh, 0, item.Length);

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
                    var item = Client.FetchNode(Path.Combine(Far.Api.Panel.CurrentDirectory, file.Name)).Result;
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
            var item = Client.FetchNode(Path.Combine(Far.Api.Panel.CurrentDirectory, args.File.Name)).Result;
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

            Exception failure = null;

            try
            {
                Task<bool> task = Client.RenameFile(item, input.Text);
                task.Wait();
                if (!task.Result)
                {
                    throw new Exception("Cannot rename the file (unknow error)");
                }
            }
            catch (AggregateException ae)
            {
                ae.Handle((x) =>
                {
                    if (x is TaskCanceledException)
                    {
                        return true; // processed
                    }

                    failure = x;
                    return true; // we catch any aggregated exception and let the user retry the operation
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }


            if (failure != null)
            {
                Far.Api.Message(new MessageArgs()
                {
                    Text = UserFriendlyException(failure),
                    Caption = "Rename File Error",
                    Options = MessageOptions.Warning,
                });

                args.Result = JobResult.Ignore;
                return;
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
        /// <param name="filePath"></param>
        /// <param name="item"></param>
        /// <param name="form"></param>
        /// <param name="wh"></param>
        /// <param name="progress"></param>
        /// <param name="totalsize"></param>
        /// <returns></returns>
        private long DoDownload(string filePath, FSItem item, Tools.ProgressForm form, ManualResetEvent wh, long progress, long totalsize)
        {
            Exception webEx = null;

            form.SetProgressValue(0, item.Length);
            try
            {
                Task<long> downloadTask = Client.DownloadFile(
                    item,
                    filePath,
                    form,
                    wh,
                    progress,
                    totalsize
                    );
                downloadTask.Wait();
                progress = downloadTask.Result;
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

                    webEx = x;
                    return true;
                });
            }

            if (webEx != null)
            {
                throw webEx;
            }

            return progress;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="file"></param>
        /// <param name="parentItem"></param>
        /// <param name="form"></param>
        private long DoUpload(FarFile file, FSItem parentItem, Tools.ProgressForm form, ManualResetEvent wh, long progress, long totalsize)
        {
            Exception exists = null;
            var ACDFilePath = Path.Combine(parentItem.Path, Path.GetFileName(file.Name));

            Task<FSItem> item = Client.FetchNode(ACDFilePath);
            item.Wait();
            if (item.Result != null)
            {
                throw new RemoteFileExistsException("File exists " + ACDFilePath);
            }

            Task<long> uploadNewTask = Client.UploadNewFile(parentItem, file.Name, form, wh, progress, totalsize);
            Exception webEx = null;

            form.Activity += ".";

            try
            {
                uploadNewTask.Wait();
                return uploadNewTask.Result; // new progress
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

                    webEx = x;
                    return true;
                });
            }

            if (webEx != null)
            {
                throw webEx;
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
        private long DoReplace(string fileName, FSItem parentItem, Tools.ProgressForm form, ManualResetEvent wh, long progress, long maxprogress, string ACDFilePath = null)
        {
            Task<long> replaceTask = Client.ReplaceFile(parentItem, fileName, form, wh, progress, maxprogress, ACDFilePath);

            Exception webEx = null;

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
                        return true; // processed
                    }

                    webEx = x;
                    return true;
                });
            }

            if (webEx != null)
            {
                throw webEx;
            }

            return progress;
        }

        private long FillFilesAndDirectories(IList<FarFile> Files, Dictionary<string, FarFile> FillFiles, Dictionary<string, FarFile> FillDirs, string DirectoryName)
        {
            long result = 0;
            foreach (var file in Files)
            {
                FillFiles.Add(Path.Combine(DirectoryName, file.Name), file);
                if (file.IsDirectory)
                {
                    FillDirs.Add(Path.Combine(DirectoryName, file.Name), file);
                }
                else
                {
                    result += file.Length;
                }
            }

            return result;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="Files"></param>
        /// <returns></returns>
        private Dictionary<string, FarFile> GetRemoteFarFilesRecursive(IList<FarFile> Files, string DirectoryName, out long Size)
        {
            Dictionary<string, FarFile> files = new Dictionary<string, FarFile>();
            Dictionary<string, FarFile> dirs = new Dictionary<string, FarFile>();
            Dictionary<string, FarFile> nextDirs = new Dictionary<string, FarFile>();
            List<FarFile> filesToProcess = (List<FarFile>)Files;

            Size = FillFilesAndDirectories(Files, files, dirs, DirectoryName);

            do
            {
                foreach (var key in dirs.Keys)
                {
                    var currentDir = Path.Combine(DirectoryName, dirs[key].Name);
                    var dirFiles = GetFiles(currentDir);
                    Size += FillFilesAndDirectories(dirFiles, files, nextDirs, currentDir);
                }
                dirs = nextDirs;
                nextDirs = new Dictionary<string, FarFile>();
            } while (dirs.Count > 0);

            return files;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="Files"></param>
        /// <returns></returns>
        private List<FarFile> GetLocalFarFilesRecursive(IList<FarFile> Files, string DirectoryName, out long Size)
        {
            List<FarFile> files = new List<FarFile>();

            Size = 0;

            foreach (var file in Files)
            {
                var path = Path.Combine(DirectoryName, file.Name);
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
                    Size += file.Length;
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

        /// <summary>
        /// TODO
        /// </summary>
        /// <returns></returns>
        public bool UIAuthenticate()
        {
            var cs = new CancellationTokenSource();
            var token = cs.Token;

            // indicates user choice in retry (-1: Cancel, 0: Retry, 1: Abort, 2: Ignore)
            int retryUserChoice = -1;

            // we use this event to pause upload thread
            var pauseThreadEvent = new ManualResetEvent(false);

            var form = GetProgressForm("Authenticating...", "Amazon Cloud Drive: Authentication");
            Exception failure = null;

            var jobThread = new Thread(() =>
            {
                do
                {
                    Task<AmazonDrive> task = Client.Authenticate(token, true);
                    try
                    {
                        task.Wait(token);
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

                            return true; // we catch any aggregated exception and let the user retry the operation
                        });
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }

                    if (failure != null)
                    {
                        // pause the thread until the user makes a decision
                        pauseThreadEvent.Reset();
                        pauseThreadEvent.WaitOne();

                        if (retryUserChoice == 0)
                        {
                            continue;
                        }
                    }

                    break;
                } while (true);

                form.Complete();
            });

            // here we check if there is a file that can be replaced
            form.Idled += (object sender, EventArgs e) =>
            {
                if (failure != null)
                {
                    var dlg = new AutoRetryDialog(UserFriendlyException(failure), "Authentication Error");
                    failure = null;
                    retryUserChoice = dlg.Display();
                    pauseThreadEvent.Set();
                    return;
                }
            };

            jobThread.Start();
            // wait a little bit
            Thread.Sleep(500);
            form.Show();

            return Client.Authenticated;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="Path"></param>
        /// <returns></returns>
        public IList<FarFile> GetFiles(string Path = "\\")
        {
            Log.Source.TraceInformation("ACDClient::GetFiles, Path = {0}", Path);
            IList<FarFile> Files = new List<FarFile>();

            if (!Client.Authenticated && !UIAuthenticate())
            {
                throw new TaskCanceledException();
            }

            var itemsData = Client.GetDirItems(Path);
            itemsData.Wait();
            var items = itemsData.Result;

            if (items == null)
            {
                return Files;
            }

            foreach (var item in items)
            {
                var file = Client.GetFarFileFromFSItem(item);
                Files.Add(file);
            }

            return Files;
        }

    }
}
