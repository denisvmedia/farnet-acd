using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections;

using Azi.Amazon.CloudDrive;
using Azi.Amazon.CloudDrive.JsonObjects;

namespace FarNet.ACD
{
    /// <summary>
    /// TODO
    /// </summary>
    class ACDClient : ITokenUpdateListener
    {
        /// <summary>
        /// TODO
        /// </summary>
        private AmazonDrive amazon;

        /// <summary>
        /// TODO
        /// </summary>
        private bool IsAuthenticated = false;

        /// <summary>
        /// TODO
        /// </summary>
        private static readonly AmazonNodeKind[] FsItemKinds = { AmazonNodeKind.FILE, AmazonNodeKind.FOLDER };

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="cs"></param>
        /// <param name="interactiveAuth"></param>
        /// <returns></returns>
        public async Task<AmazonDrive> Authenticate(CancellationToken cs, bool interactiveAuth = true)
        {
            var Settings = ACDSettings.Default;

            //var settings = Gui.Properties.Settings.Default;
            amazon = new AmazonDrive(Settings.ClientId, Settings.ClientSecret);
            amazon.OnTokenUpdate = this;

            if (!string.IsNullOrWhiteSpace(Settings.AuthRenewToken))
            {
                if (await amazon.AuthenticationByTokens(
                    Settings.AuthToken,
                    Settings.AuthRenewToken,
                    Settings.AuthTokenExpiration))
                {
                    return amazon;
                }
            }


            if (await amazon.AuthenticationByExternalBrowser(CloudDriveScopes.ReadAll | CloudDriveScopes.Write, TimeSpan.FromMinutes(2), cs, "http://localhost:{0}/signin/"))
            {
                return amazon;
            }

            //cs.ThrowIfCancellationRequested();
            return null;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="itemPath"></param>
        /// <returns></returns>
        public async Task<FSItem> FetchNode(string itemPath)
        {
            //Far.Api.ShowError("Not implemented", new NotImplementedException("Not implemented: " + itemPath));
            if (itemPath == "\\" || itemPath == string.Empty)
            {
                return FSItem.FromRoot(await amazon.Nodes.GetRoot());
            }

            var folders = new LinkedList<string>();
            var curpath = itemPath;
            FSItem item = null;

            do
            {
                folders.AddFirst(Path.GetFileName(curpath));
                curpath = Path.GetDirectoryName(curpath);
                if (curpath == "\\" || string.IsNullOrEmpty(curpath))
                {
                    break;
                }
            } while (true);

            if (item == null)
            {
                item = FSItem.FromRoot(await amazon.Nodes.GetRoot());
            }

            foreach (var name in folders)
            {
                if (curpath == "\\")
                {
                    curpath = string.Empty;
                }

                curpath = curpath + "\\" + name;

                var newnode = await amazon.Nodes.GetChild(item.Id, name);
                if (newnode == null || newnode.status != AmazonNodeStatus.AVAILABLE)
                {
                    // Log.Error("NonExisting path from server: " + itemPath);
                    return null;
                }

                item = FSItem.FromNode(curpath, newnode);
            }

            return item;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="folderPath"></param>
        /// <returns></returns>
        public async Task<IList<FSItem>> GetDirItems(string folderPath)
        {
            var folderNode = FetchNode(folderPath).Result;
            var nodes = await amazon.Nodes.GetChildren(folderNode?.Id);
            var items = new List<FSItem>(nodes.Count);
            var curdir = folderPath;
            if (curdir == "\\")
            {
                curdir = string.Empty;
            }

            foreach (var node in nodes.Where(n => FsItemKinds.Contains(n.kind)))
            {
                if (node.status != AmazonNodeStatus.AVAILABLE)
                {
                    continue;
                }

                var path = curdir + "\\" + node.name;
                items.Add(FSItem.FromNode(path, node));
            }

            // Log.Warn("Got real dir:\r\n  " + string.Join("\r\n  ", items.Select(i => i.Path)));
            return items;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="dest"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task DownloadFile(FSItem item, string dest, Tools.ProgressForm form)
        {
            using (var fs = new FileStream(dest, FileMode.OpenOrCreate))
            {
                var totalBytes = Utility.BytesToString(item.Length);
                await amazon.Files.Download(item.Id, fs, null, null, 4096, (long position) =>
                {
                    if (form.IsClosed)
                    {
                        throw new OperationCanceledException();
                    }
                    //Log.Source.TraceInformation("Progress: {0}", progress);
                    form.Activity = string.Format("{0} ({1}/{2})", item.Name, Utility.BytesToString(position), totalBytes);
                    form.SetProgressValue(position, item.Length);
                    return position;
                });
            }
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="dest"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task DeleteFile(FSItem item, Tools.ProgressForm form)
        {
            form.Activity = item.Name;
            await amazon.Nodes.Trash(item.Id);
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="src"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task UploadFile(FSItem item, string src, Tools.ProgressForm form)
        {
            var itemLength = new FileInfo(src).Length;
            var totalBytes = Utility.BytesToString(itemLength);
            var filename = Path.GetFileName(src);
            var fileUpload = new FileUpload();
            fileUpload.AllowDuplicate = true;
            fileUpload.ParentId = item.Id;
            fileUpload.FileName = filename;
            fileUpload.StreamOpener = () => new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            fileUpload.Progress = (long position) =>
            {
                //Log.Source.TraceInformation("Progress: {0}", progress);
                form.Activity = string.Format("{0} ({1}/{2})", filename, Utility.BytesToString(position), totalBytes);
                form.SetProgressValue(position, itemLength);

                return position;
            };
            var cs = new CancellationTokenSource();
            var token = cs.Token;
            fileUpload.CancellationToken = token;
            form.Canceled += (object sender, EventArgs e) =>
            {
                cs.Cancel(true);
            };

            await amazon.Files.UploadNew(fileUpload);
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="access_token"></param>
        /// <param name="refresh_token"></param>
        /// <param name="expires_in"></param>
        public void OnTokenUpdated(string access_token, string refresh_token, DateTime expires_in)
        {
            var settings = ACDSettings.Default;
            settings.AuthToken = access_token;
            settings.AuthRenewToken = refresh_token;
            settings.AuthTokenExpiration = expires_in;
            settings.Save();
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <returns></returns>
        public bool UIAuthenticate()
        {
            var cs = new CancellationTokenSource();
            var token = cs.Token;

            var form = new Tools.ProgressForm();
            form.Canceled += delegate
            {
                cs.Cancel(false);
            };
            form.Title = "Amazon Cloud Drive: Authentication";
            form.Activity = "Authenticating...";
            form.CanCancel = true;

            Task<AmazonDrive> task = Authenticate(token, true);

            var _task = Task.Factory.StartNew(() =>
            {
                form.Show();
            });

            try
            {
                task.Wait(token);
            }
            catch (AggregateException) // some exception in event (most likely timeout)
            {
                form.Complete();
            }
            catch (OperationCanceledException) // cancellation was requested from the dialog
            {
            }

            if (!token.IsCancellationRequested && task.Status == TaskStatus.RanToCompletion)
            {
                form.Complete();
                return IsAuthenticated = true;
            }

            return IsAuthenticated = false;
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

            if (!IsAuthenticated && !UIAuthenticate())
            {
                throw new TaskCanceledException();
            }

            var itemsData = GetDirItems(Path);
            var items = itemsData.Result;
            foreach (var item in items)
            {
                SetFile file = new SetFile();
                file.Name = item.Name;
                file.Description = item.Id;
                file.IsDirectory = item.IsDir;
                file.LastAccessTime = item.LastAccessTime;
                file.LastWriteTime = item.LastWriteTime;
                file.Length = item.Length;
                file.CreationTime = item.CreationTime;
                file.Data = new Hashtable();
                ((Hashtable)file.Data).Add("fsitem", item);
                Files.Add(file);
            }

            return Files;
        }
    }
}
