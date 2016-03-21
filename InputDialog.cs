
/*
Based on PowerShellFar.UI.InputDialog, Copyright (c) 2006-2016 Roman Kuzmin
*/

using System;
using System.Collections.Generic;
using FarNet;
using FarNet.Forms;

namespace FarNet.ACD
{
    class InputDialog
    {
        const int DLG_WIDTH = 76;

        public static readonly Guid TypeId = new Guid("2a3ccad7-265f-4091-9516-7245e76c8e4b");

        public string Caption { get; set; }
        public IList<string> Prompt { get; set; }
        public string Text { get; set; }
        public string History { get; set; }
        public bool UseLastHistory { get; set; } = false;

        public bool Show()
        {
            if (Prompt == null)
                Prompt = new string[] { };

            int w = DLG_WIDTH;//Far.Api.UI.WindowSize.X - 7;
            int h = 7 + Prompt.Count;

            var uiDialog = Far.Api.CreateDialog(-1, -1, w, h);
            uiDialog.TypeId = TypeId;
            uiDialog.AddBox(3, 1, w - 4, h - 2, Caption);

            var uiPrompt = new List<IText>(Prompt.Count);
            foreach (var s in Prompt)
                uiPrompt.Add(uiDialog.AddText(5, -1, w - 6, s));

            var uiEdit = uiDialog.AddEdit(5, -1, w - 6, string.Empty);
            uiEdit.IsPath = true;
            uiEdit.Text = Text ?? string.Empty;
            uiEdit.History = History;
            uiEdit.UseLastHistory = UseLastHistory;
            uiEdit.NoAutoComplete = true;

            // hotkeys
            uiEdit.KeyPressed += (sender, e) =>
            {
                switch (e.Key.VirtualKeyCode)
                {
                    case KeyCode.Enter:
                        if (uiEdit.Text.Length == 0)
                        {
                            Far.Api.Message(new MessageArgs()
                            {
                                Text = "Input cannot be empty",
                                Options = MessageOptions.Warning,
                            });
                            e.Ignore = true;
                        }
                        break;
                    case KeyCode.Tab:
                        //e.Ignore = true;
                        //EditorKit.ExpandCode(uiEdit.Line, null);
                        break;
                    case KeyCode.F1:
                        e.Ignore = true;
                        //Help.ShowHelpForContext("InvokeCommandsDialog");
                        break;
                }
            };

            IButton okButton = uiDialog.AddButton(0, h - 3, "OK");
            okButton.CenterGroup = true;
            IButton cancelButton = uiDialog.AddButton(0, h - 3, "Cancel");
            cancelButton.CenterGroup = true;
            uiDialog.Default = okButton;
            uiDialog.Cancel = cancelButton;

            if (!uiDialog.Show())
                return false;

            Text = uiEdit.Text;
            return true;
        }
    }
}
