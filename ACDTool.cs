
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using FarNet;
using FarNet.Forms;

namespace FarNet.ACD
{
	/// <summary>
	/// Provides a menu item in all plugin menus.
	/// It sets the level of tracing used by the core and turn tracing to the file on/off.
	/// The file trace listener is added to the <see cref="Log.Source"/> (FarNet) and <see cref="Trace"/> (.NET).
	/// </summary>
	/// <remarks>
	/// The plugin menu item is shown in English or Russian, according to the current UI settings.
	/// The attribute property <c>Name</c> is treated as a resource (due to the <c>Resources</c> flag).
	/// </remarks>
	[ModuleTool(Name = "MenuTitle", Options = ModuleToolOptions.Disk, Resources = true)]
	[Guid("80e3e2d1-b0b1-4329-89dc-b3a51309bccb")]
	public class ACDTool : ModuleTool
	{
        internal void DisplayError(string Error)
        {
            var size = Far.Api.UI.WindowSize;
            IDialog Dialog = Far.Api.CreateDialog(-1, -1, 100, 10);
            Dialog.IsWarning = true;
            Dialog.AddBox(3, 1, 0, 0, "Amazon Cloud Drive Error");
            Dialog.AddText(5, 2, 0, Error);
            Dialog.Default = Dialog.AddButton(0, 7, "Ok");
            ((IButton)Dialog.Default).CenterGroup = true;
            //Dialog.Cancel = Dialog.AddButton(0, 7, "Cancel");
            //Dialog.Cancel.CenterGroup = true;
            Dialog.Show();
        }

        /// <summary>
        /// This method implements the menu tool action.
        /// </summary>
        public override void Invoke(object sender, ModuleToolEventArgs e)
		{
            if (string.IsNullOrWhiteSpace(ACDSettings.Default.ClientId) || string.IsNullOrWhiteSpace(ACDSettings.Default.ClientSecret))
            {
                DisplayError("ClientId or ClientSecret is not set!");
            } else {
                DoPanel();
            }
		}

        void DoPanel()
        {
            var Client = new ACDClient();
            (new ACDExplorer(Client)).OpenPanel();
        }
    }
}
