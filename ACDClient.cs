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
using Newtonsoft.Json;

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

        private bool _authenticated = false;

        /// <summary>
        /// TODO
        /// </summary>
        public bool Authenticated {
            get { return _authenticated; }
            set { _authenticated = value; }
        }

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
                    Authenticated = true;
                    return amazon;
                }
            }


            if (await amazon.AuthenticationByExternalBrowser(CloudDriveScopes.ReadAll | CloudDriveScopes.Write, TimeSpan.FromMinutes(2), cs, "http://localhost:{0}/signin/"))
            {
                Authenticated = true;
                return amazon;
            }

            return null;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="itemPath"></param>
        /// <returns></returns>
        public async Task<FSItem> FetchNode(string itemPath, bool CacheBypass = false)
        {
            FSItem item = null;
            if (!CacheBypass)
            {
                item = CacheStorage.GetItemByPath(itemPath);
            }

            if (item != null)
            {
                return item;
            }

            if (itemPath == "\\" || itemPath == string.Empty)
            {
                item = FSItem.FromRoot(await amazon.Nodes.GetRoot());
                CacheStorage.AddItem(item);
                return item;
            }

            var folders = new LinkedList<string>();
            var curpath = itemPath;

            do
            {
                folders.AddFirst(Path.GetFileName(curpath));
                curpath = Path.GetDirectoryName(curpath);
                if (curpath == "\\" || string.IsNullOrEmpty(curpath))
                {
                    break;
                }
            } while (true);

            item = FSItem.FromRoot(await amazon.Nodes.GetRoot());

            foreach (var name in folders)
            {
                if (curpath == "\\")
                {
                    curpath = string.Empty;
                }

                curpath = curpath + "\\" + name;

                FSItem nextitem = null;
                if (!CacheBypass)
                {
                    nextitem = CacheStorage.GetItemByPath(curpath);
                }

                if (nextitem == null)
                {

                    var newnode = await amazon.Nodes.GetChild(item.Id, name);
                    if (newnode == null || newnode.status != AmazonNodeStatus.AVAILABLE)
                    {
                        // Log.Error("NonExisting path from server: " + itemPath);
                        return null;
                    }

                    nextitem = FSItem.FromNode(curpath, newnode);
                    CacheStorage.AddItem(nextitem);
                }

                item = nextitem;
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
            var items = CacheStorage.GetItemsByParentPath(folderPath);

            if (items != null)
            {
                return items;
            }

            var folderNode = await FetchNode(folderPath);

            var nodes = await amazon.Nodes.GetChildren(folderNode?.Id);
            items = new List<FSItem>(nodes.Count);
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

            CacheStorage.AddItems(folderPath, items);

            return items;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="dest"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task<long> DownloadFile(FSItem item, string dest, Tools.ProgressForm form, EventWaitHandle wh, long progress, long totalsize)
        {
            if (item.IsDir)
            {
                form.Activity = string.Format("{0} {1})", "Creating directory", Utility.ShortenString(dest, 20));
                Directory.CreateDirectory(dest);
                return progress;
            }

            using (var fs = new FileStream(dest, FileMode.Create))
            {
                var totalBytes = Utility.BytesToString(item.Length);
                await amazon.Files.Download(item.Id, fs, null, null, 4096, (long position) =>
                {
                    wh.WaitOne();

                    if (form.IsClosed)
                    {
                        fs.Dispose();
                        try
                        {
                            File.Delete(dest);
                        }
                        catch { }
                        throw new TaskCanceledException();
                    }

                    form.Activity = string.Format("{0} ({1}/{2})", Utility.ShortenString(dest, 20), Utility.BytesToString(position), totalBytes) + Environment.NewLine;
                    form.Activity += Progress.FormatProgress(position, item.Length) + Environment.NewLine;
                    form.Activity += "Total:";

                    form.SetProgressValue(progress + position, totalsize);

                    return position;
                });

                return progress + item.Length;
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
            form.Activity = Utility.ShortenString(item.Path, 20);
            await amazon.Nodes.Trash(item.Id);

            CacheStorage.RemoveItem(item);
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="dest"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task<FSItem> CreateDirectory(string filePath, FSItem parent = null, bool allowExisting = true)
        {
            if (filePath == "\\" || filePath == ".." || filePath == ".")
            {
                return null;
            }
            var dir = Path.GetDirectoryName(filePath);
            if (dir == ".." || dir == ".")
            {
                return null;
            }

            if (parent == null)
            {
                parent = await FetchNode(dir);
                if (parent == null)
                {
                    return null;
                }
            }

            var name = Path.GetFileName(filePath);
            AmazonNode node = null;
            string nodeId = null;

            try
            {
                node = await amazon.Nodes.CreateFolder(parent.Id, name);
            }
            catch (Azi.Tools.HttpWebException x)
            {
                if (x.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    if (!allowExisting)
                    {
                        throw x;
                    }
                    var resp = new StreamReader((x.InnerException as System.Net.WebException).Response.GetResponseStream()).ReadToEnd();

                    dynamic obj = JsonConvert.DeserializeObject(resp);
                    nodeId = obj.info.nodeId.Value;
                }
                else
                {
                    throw x;
                }
            }

            if (nodeId != null) // in case of duplicate
            {
                node = await amazon.Nodes.GetNode(nodeId);
            }

            var item = FSItem.FromNode(filePath, node);

            CacheStorage.AddItem(item);

            return item;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="parentItem"></param>
        /// <param name="src"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task<long> UploadNewFile(FSItem parentItem, string src, Tools.ProgressForm form, EventWaitHandle wh, long progress, long totalsize)
        {
            var itemLength = new FileInfo(src).Length;
            var totalBytes = Utility.BytesToString(itemLength);
            var filename = Path.GetFileName(src);
            var fileUpload = new FileUpload();
            fileUpload.AllowDuplicate = true;
            fileUpload.ParentId = parentItem.Id;
            fileUpload.FileName = filename;
            fileUpload.StreamOpener = () => new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            fileUpload.Progress = (long position) =>
            {
                wh.WaitOne();

                form.Activity = string.Format("{0} ({1}/{2})", Utility.ShortenString(src, 20), Utility.BytesToString(position), totalBytes) + Environment.NewLine;
                form.Activity += Progress.FormatProgress(position, itemLength) + Environment.NewLine;
                form.Activity += "Total:";

                form.SetProgressValue(progress + position, totalsize);

                return position;
            };
            var cs = new CancellationTokenSource();
            var token = cs.Token;
            fileUpload.CancellationToken = token;
            form.Canceled += (object sender, EventArgs e) =>
            {
                cs.Cancel(true);
            };

            var node = await amazon.Files.UploadNew(fileUpload);

            CacheStorage.AddItem(FSItem.FromNode(Path.Combine(parentItem.Path, filename), node));

            return progress + node.Length;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="parentItem"></param>
        /// <param name="src"></param>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task<long> ReplaceFile(FSItem parentItem, string src, Tools.ProgressForm form, EventWaitHandle wh, long progress, long maxprogress, string ACDFilePath = null)
        {
            var itemLength = new FileInfo(src).Length;
            var totalBytes = Utility.BytesToString(itemLength);
            var filename = Path.GetFileName(src);
            var fileUpload = new FileUpload();
            var cs = new CancellationTokenSource();
            var token = cs.Token;
            fileUpload.CancellationToken = token;
            fileUpload.AllowDuplicate = true;
            fileUpload.ParentId = parentItem.Id;
            // for upload we need a node id to replace
            if (ACDFilePath == null)
            {
                ACDFilePath = Path.Combine(parentItem.Path, filename);
            }
            var node = await FetchNode(ACDFilePath);
            if (node == null)
            {
                throw new FileNotFoundException("Remote file " + ACDFilePath + " not found");
            }
            fileUpload.FileName = node.Id;

            fileUpload.StreamOpener = () => new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            fileUpload.Progress = (long position) =>
            {
                wh.WaitOne();
                //Log.Source.TraceInformation("Progress: {0}", progress);
                form.Activity = string.Format("{0} ({1}/{2})", Utility.ShortenString(src, 20), Utility.BytesToString(position), totalBytes) + Environment.NewLine;
                form.Activity += Progress.FormatProgress(position, itemLength) + Environment.NewLine;
                form.Activity += "Total:";

                form.SetProgressValue(progress + position, maxprogress);

                return position;
            };
            form.Canceled += (object sender, EventArgs e) =>
            {
                cs.Cancel(true);
            };

            var resultNode = await amazon.Files.Overwrite(fileUpload);

            CacheStorage.AddItem(FSItem.FromNode(ACDFilePath, resultNode));

            return progress + resultNode.Length;
        }
        
        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="newParent"></param>
        /// <returns></returns>
        public async Task<bool> MoveFile(FSItem item, string newParent)
        {
            if (!Utility.IsValidPathname(newParent))
            {
                return false;
            }

            var newParentNode = await FetchNode(newParent);

            return await MoveFile(item, newParentNode);
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="newParentNode"></param>
        /// <returns></returns>
        public async Task<bool> MoveFile(FSItem item, FSItem newParentNode)
        {
            if (newParentNode == null)
            {
                return false; // TODO: throw an exception
            }

            if (!newParentNode.IsDir)
            {
                return false; // TODO: throw an exception
            }

            await amazon.Nodes.Move(item.Id, item.ParentIds.First(), newParentNode.Id);

            CacheStorage.RemoveItem(item);
            CacheStorage.RemoveItems(newParentNode.Path);

            return true;
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item"></param>
        /// <param name="newName"></param>
        /// <returns></returns>
        public async Task<bool> RenameFile(FSItem item, string newName)
        {
            if (!Utility.IsValidPathname(newName))
            {
                return false; // TODO: throw an exception
            }

            // 1. Is newName a folder?
            var newParentNode = await FetchNode(newName);
            if (newParentNode != null)
            {
                return await MoveFile(item, newParentNode);
            }

            // 2. Is newName a file in the same dir?
            var destination = Path.GetDirectoryName(newName);
            // 2.1 Is destination empty? (means that the file is in the current folder)
            if (destination == "")
            {
                CacheStorage.RemoveItems(item.Dir); // invalidate parent path
                await amazon.Nodes.Rename(item.Id, newName);
                return true;
            }

            // 2.1.1 If destination (destination) node does not exist or is not a directory, we should fail
            var destinationNode = await FetchNode(destination);
            if (destinationNode == null || !destinationNode.IsDir)
            {
                return false; // TODO: throw exception
            }

            // 2.2 Is destination the same as the directory name of the item?
            var filename = Path.GetFileName(newName);
            if (destination == item.Dir)
            {
                CacheStorage.RemoveItems(item.Dir); // invalidate parent path
                await amazon.Nodes.Rename(item.Id, filename);
                return true;
            }

            // 3. Is newName is another folder AND filename? (the only remaining option)
            //    Here we have 2 problems (because of no way to move and rename atomically):
            //    1) If we first rename and then move, then it might happen so that there is a file with the same name in the current folder
            //    2) Similar problem can be if first move and then rename
            //    Solution? We have it.
            //    1) We should first rename to something unique (say, filename.randomstr.ext) and most likely we will not get a conflict
            //    2) We move the file with this unique name
            //    3) We _try_ to rename back to the original name
            //    4) If we fail, we add (2), (3), (n) to the filename (i.e.: filename (n).ext)
            //    5) If we still fail, we at least have the same file with the randomstr in the filename
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            var extension = Path.GetExtension(filename);
            var randomString = Utility.RandomString(8);
            var tmpFilename = string.Format("{0}.{1}.{2}", filenameWithoutExtension, randomString, extension);

            // 3.1 Rename to a temporary name
            await amazon.Nodes.Rename(item.Id, tmpFilename);
            // 3.2 Move to the new destination
            await amazon.Nodes.Move(item.Id, item.ParentIds.First(), destinationNode.Id);
            // 3.3 Rename back to the original name
            await amazon.Nodes.Rename(item.Id, filename); // TODO: catch exceptions and try to rename again

            CacheStorage.RemoveItems(item.Dir); // invalidate parent path
            CacheStorage.RemoveItems(destinationNode.Path); // invalidate new parent path

            return true;
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
        /// Get FarFile wrapper for FSItem
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public FarFile GetFarFileFromFSItem(FSItem item)
        {
            SetFile file = new SetFile()
            {
                Name = item.Name,
                Description = item.Id,
                IsDirectory = item.IsDir,
                LastAccessTime = item.LastAccessTime,
                LastWriteTime = item.LastWriteTime,
                Length = item.Length,
                CreationTime = item.CreationTime,
                Data = new Hashtable(),
            };
            //((Hashtable)file.Data).Add("fsitem", item);
            //CacheStorage.AddItem(item);

            return file;
        }
    }
}
