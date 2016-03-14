using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace FarNet.ACD
{
	/// <summary>
	/// User actions.
	/// </summary>
	enum UserAction
	{
		/// <summary>None.</summary>
		None,
		/// <summary>Enter is pressed.</summary>
		Enter
	}

    public sealed class ACDPanel : Panel
    {
        /// <summary>
        /// The last user action.
        /// </summary>
        internal UserAction UserWants { get; set; }

        /// <summary>
        /// Tells to treat the items as not directories even if they have a directory flag.
        /// </summary>
        internal bool IgnoreDirectoryFlag { get; set; }

        /// <summary>Apply command.</summary>
        internal void UIApply()
        {
        }

        /// <summary>Attributes action.</summary>
        internal void UIAttributes()
        {
        }

        /*
        internal bool UICopyMoveCan(bool move) //?????
        {
            return !move && TargetPanel is ObjectPanel;
        }
        */

        /// <summary>
        /// Shows help or the panel menu.
        /// </summary>
        internal void UIHelp()
        {
            //ShowMenu();
        }

        /// <summary>Mode action.</summary>
        internal void UIMode()
        {
        }

        ///
        internal void UIOpenFileMembers()
        {
            FarFile file = CurrentFile;
            if (file != null)
            {
                //OpenFileMembers(file);
            }
        }


        /// <summary>
        /// New panel with the explorer.
        /// </summary>
        /// <param name="explorer">The panel explorer.</param>
        public ACDPanel(Explorer explorer)
            : base(explorer)
        {
            // start mode
            //ViewMode = PanelViewMode.AlternativeFull;

            CurrentLocation = "\\";

            Title = "Amazon Cloud Drive";

            // Set sort and view modes
            SortMode = PanelSortMode.Default;
            ViewMode = PanelViewMode.Full;
            Highlighting = PanelHighlighting.Full;
            UseSortGroups = true;

            // Define the panel columns
            PanelPlan plan = new PanelPlan();
            plan.Columns = new FarColumn[]
            {
                new SetColumn() { Kind = "N", Name = "Name" },
                new SetColumn() { Kind = "Z", Name = "ID" }
            };
            SetPlan(PanelViewMode.AlternativeFull, plan);

            Log.Source.TraceInformation("Entering DoExplored from Constructor");
            DoExplored((ACDExplorer)explorer);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public override bool UIKeyPressed(KeyInfo key)
        {
            if (key == null) throw new ArgumentNullException("key");
            UserWants = UserAction.None;
            try
            {
                switch (key.VirtualKeyCode)
                {
                    case KeyCode.Enter:

                        if (key.Is())
                        {
                            //Far.Api.ShowError("Not implemented", new NotImplementedException("Not implemented: "+ CurrentFile.Name));

                            FarFile file = CurrentFile;
                            if (file == null)
                                break;

                            UserWants = UserAction.Enter;

                            if (file.IsDirectory && !IgnoreDirectoryFlag)
                                break;

                            var cancellation = new CancellationTokenSource();
                            var _cancel = cancellation.Token;

                            //var ptask = new ProgressTask("test", "test-title", _cancel);
                            //var form = ptask.ShowDialog();
                            //Thread.Sleep(3000);
                            //cancellation.Cancel();
                            //form.Complete();

                            UIOpenFile(file);

                            return true;
                        }

                        if (key.IsShift())
                        {
                            UIAttributes();
                            return true;
                        }

                        break;

                    case KeyCode.F1:

                        if (key.Is())
                        {
                            UIHelp();
                            return true;
                        }

                        break;

                    case KeyCode.F3:

                        if (key.Is())
                        {
                            if (CurrentFile == null)
                            {
                                //UIViewAll();
                                return true;
                            }
                            break;
                        }

                        if (key.IsShift())
                        {
                            //ShowMenu();
                            return true;
                        }

                        break;

                    case KeyCode.PageDown:

                        if (key.IsCtrl())
                        {
                            UIOpenFileMembers();
                            return true;
                        }

                        break;

                    case KeyCode.A:

                        if (key.IsCtrl())
                        {
                            UIAttributes();
                            return true;
                        }

                        break;

                    case KeyCode.G:

                        if (key.IsCtrl())
                        {
                            UIApply();
                            return true;
                        }

                        break;

                    case KeyCode.M:

                        if (key.IsCtrlShift())
                        {
                            UIMode();
                            return true;
                        }

                        break;

                    case KeyCode.S:

                        //! Mantis#2635 Ignore if auto-completion menu is opened
                        if (key.IsCtrl() && Far.Api.Window.Kind != WindowKind.Menu)
                        {
                            SaveData();
                            return true;
                        }

                        break;
                }

                // base
                return base.UIKeyPressed(key);
            }
            finally
            {
                UserWants = UserAction.None;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The method updates internal data depending on the current explorer.
        /// </remarks>
        public override void UIExplorerEntered(ExplorerEnteredEventArgs args)
        {
            if (args == null) return;

            base.UIExplorerEntered(args);
            Log.Source.TraceInformation("Entering DoExplored from UIExplorerEntered");
            DoExplored((ACDExplorer)args.Explorer);
        }

        private void DoExplored(ACDExplorer explorer)
        {
            //! path is used for Set-Location on Invoking()
            Title = "ACD: " + Explorer.Location;
            CurrentLocation = Explorer.Location;
            Log.Source.TraceInformation("Title: {0}; CurrentLocation: {1}", Title, CurrentLocation);

            if (CurrentLocation == "\\")
            {
                DotsMode = PanelDotsMode.Off;
            }
            else
            {
                DotsMode = PanelDotsMode.Dots;
            }
        }

        /// <inheritdoc/>
        public override IList<FarFile> UIGetFiles(GetFilesEventArgs args)
        {
            if (args == null) return null;

            //Far.Api.ShowError("Not implemented", new NotImplementedException("Not implemented: " + this.Explorer.Location));

            args.Parameter = this;

            try
            {
                var files = base.UIGetFiles(args);
                return files;
            }
            catch (TaskCanceledException)
            { }

            args.Result = JobResult.Default;
            //UIEscape(false); // crashes far
            //this.Close(); // exception or far crash

            return new List<FarFile>();
        }
    }
}
