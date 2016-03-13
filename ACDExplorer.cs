
using System;
using System.Collections.Generic;
using System.IO;
using System.Resources;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;
using System.Collections;
using Azi.Amazon.CloudDrive;
using Azi.Amazon.CloudDrive.JsonObjects;

namespace FarNet.ACD
{
	/// <summary>
	/// Panel file explorer to view .resources file data.
	/// </summary>
	class ACDExplorer : Explorer
	{
        readonly ACDClient Client;
        ACDPanel Panel;

		public ACDExplorer(ACDClient client, ACDPanel panel = null, string path = "\\")
			: base(new Guid("dc05c639-5e56-4c0e-b83e-19c63731949a"))
		{
            Client = client;
            Location = path;
            Panel = panel;
        }

        /// <inheritdoc/>
        public override Explorer ExploreDirectory(ExploreDirectoryEventArgs args)
        {
            if (args == null) return null;

            var path = ((FSItem)((Hashtable)Panel.CurrentFile.Data)["fsitem"]).Path;

            return Explore(path);
        }

        public override Explorer ExploreParent(ExploreParentEventArgs args)
        {
            var path = Path.GetDirectoryName(Location);

            return Explore(path);
        }

        Explorer Explore(string location)
        {
            //! propagate the provider, or performance sucks
            var newExplorer = new ACDExplorer(Client, Panel, location);

            return newExplorer;
        }

        public override IList<FarFile> GetFiles(GetFilesEventArgs args)
		{
            string path = "\\";
            if (Panel.CurrentFile != null)
            {
                path = ((FSItem)((Hashtable)Panel.CurrentFile.Data)["fsitem"]).Path;
            }
            
            return Client.GetFiles(path);
        }

		public override Panel CreatePanel()
		{
			Panel = new ACDPanel(this);
            return Panel;
		}
	}
}
