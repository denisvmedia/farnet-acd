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
    class ACDClient : ITokenUpdateListener
    {
        private AmazonDrive amazon;
        private static readonly AmazonNodeKind[] FsItemKinds = { AmazonNodeKind.FILE, AmazonNodeKind.FOLDER };

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


            if (await amazon.AuthenticationByExternalBrowser(CloudDriveScope.ReadAll | CloudDriveScope.Write, TimeSpan.FromMinutes(10), cs, "http://localhost:{0}/signin/"))
            {
                return amazon;
            }

            cs.ThrowIfCancellationRequested();
            return null;
        }

        private async Task<FSItem> FetchNode(string itemPath)
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

        public void OnTokenUpdated(string access_token, string refresh_token, DateTime expires_in)
        {
            var settings = ACDSettings.Default;
            settings.AuthToken = access_token;
            settings.AuthRenewToken = refresh_token;
            settings.AuthTokenExpiration = expires_in;
            settings.Save();
        }

        public IList<FarFile> GetFiles(string Path = "\\")
        {
            IList<FarFile> Files = new List<FarFile>();
            var cs = new CancellationTokenSource();
            Task<AmazonDrive> task = Authenticate(cs.Token, true);
            task.Wait(60000); //TODO: show some dialog and allow manual cancellation
            if (task.IsCompleted)
            {
                var items = GetDirItems(Path).Result;
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
            }

            return Files;
        }
    }
}
