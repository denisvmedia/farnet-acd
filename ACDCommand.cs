
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
namespace FarNet.ACD
{
	/// <summary>
	/// Command invoked from the command line by the "ACD:" prefix.
	/// It prints some data depending on the command text after the prefix.
	/// </summary>
	[ModuleCommand(Name = "FarNet.ACD Command", Prefix = "acd")]
	[Guid("c3b66ce5-5a7b-4375-85cc-b680a1508009")]
	public class ACDCommand : ModuleCommand
	{
		const int kb = 1024;
		/// <summary>
		/// This method implements the command action.
		/// The command text is the Command property value.
		/// </summary>
		public override void Invoke(object sender, ModuleCommandEventArgs e)
		{
			switch (e.Command.Trim().ToUpper())
			{
				case "PROCESS": DoProcess(); break;
				case "ASSEMBLY": DoAssembly(); break;
				case "RESOURCES": DoResources(); break;
				default:
					// Show help in the help viewer
					Far.Api.ShowHelpTopic("ACDCommand");
					break;
			}
		}
		/// <summary>
		/// Prints some useful process information.
		/// </summary>
		void DoProcess()
		{
			var process = Process.GetCurrentProcess();
			Far.Api.UI.Write(string.Format(@"
Total time     : {0}
Working set    : {1,7:n0} kb
Private memory : {2,7:n0} kb
Managed memory : {3,7:n0} kb
",
 process.TotalProcessorTime,
 process.WorkingSet64 / kb,
 process.PrivateMemorySize64 / kb,
 GC.GetTotalMemory(true) / kb));
		}
		/// <summary>
		/// Shows loaded .NET assembly paths in the viewer.
		/// </summary>
		void DoAssembly()
		{
			var list = new List<string>();
			foreach (var it in AppDomain.CurrentDomain.GetAssemblies())
			{
				try { list.Add(it.Location); }
				catch { list.Add(it.FullName); }
			}
			list.Sort();

			var viewer = Far.Api.CreateViewer();
			viewer.Title = "Assemblies";
			viewer.FileName = Far.Api.TempName();
			viewer.Switching = Switching.Enabled;
			viewer.DeleteSource = DeleteSource.File;

			File.WriteAllLines(viewer.FileName, list.ToArray());
			viewer.Open();
		}
		/// <summary>
		/// Opens the current panel file as a .resources file.
		/// </summary>
		void DoResources()
		{
			var file = Far.Api.Panel.CurrentFile;
			if (file == null)
				return;

			var path = Path.Combine(Far.Api.Panel.CurrentDirectory, file.Name);
			if (!File.Exists(path))
				return;

            var Client = new ACDClient();
            (new ACDExplorer(Client)).OpenPanel();
		}
	}
}
