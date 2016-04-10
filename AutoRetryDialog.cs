using System;
using FarNet.Tools;
using FarNet.Forms;
using System.Collections.Generic;
using System.Linq;

namespace FarNet.ACD
{
    class AutoRetryDialog : Form
    {
        internal const int MAX_LINE_COUNT = 10;
        internal const int AUX_WIDTH = 10;
        internal const int AUX_HEIGHT = 6;

        string _Message;
        int _clickedButton = -1;
        string[] _Buttons;
        int _DefaultButton;

        public int ClickedButton
		{
			get { return _clickedButton; }
            private set { _clickedButton = value; }
		}


        public AutoRetryDialog(string Message, string Title, string[] Buttons = null, int DefaultButton = 0) : base()
        {
            if (Buttons == null)
            {
                Buttons = new string[] { "&Retry", "&Abort", "&Ignore" };
            }

            Dialog.IsWarning = true;
            Dialog.KeepWindowTitle = true;
            Dialog.EnableRedraw();
            this.Title = Title;
            _Message = Message;
            _Buttons = Buttons;
            _DefaultButton = DefaultButton;
        }

        public override bool Show()
        {
            Init();
            return base.Show();
        }

        public int Display()
        {
            Show();
            return _clickedButton;
        }

        void Init()
        {
            List<string> lines = Utility.StringToList(_Message);

            var text = Utility.WrapText(lines, Console.WindowWidth - 14, Console.WindowHeight - 8);

            var textWidth = Utility.GetTextWidth(text);
            var textHeight = text.Count();

            IButton[] buttons = new IButton[_Buttons.Length];
            _Buttons[_DefaultButton] += " (00)";
            int buttonsWidth = 0;
            for (int key = 0; key < _Buttons.Length; key++)
            {
                buttonsWidth += _Buttons[key].Replace("&", "").Length + 5;
            }
            buttonsWidth--;

            SetSize(AUX_WIDTH + (textWidth > buttonsWidth ? textWidth : buttonsWidth), AUX_HEIGHT + textHeight);

            foreach (var line in text)
            {
                Dialog.AddText(5, -1, 5 + textWidth - 1, line);
            }

            Dialog.AddText(5, -1, 0, string.Empty).Separator = 1;

            for (int key = 0; key < _Buttons.Length; key++)
            {
                buttons[key] = Dialog.AddButton(0, AUX_HEIGHT + textHeight - 3, _Buttons[key]);
                buttons[key].CenterGroup = true;
                buttons[key].Data = key;
                buttonsWidth += _Buttons[key].Replace("&", "").Length + 5;
            }

            var timer = new System.Timers.Timer(1000);
            bool AutoClose = false;

            Dialog.Default = buttons[_DefaultButton];
            Dialog.Default.Text = _Buttons[_DefaultButton].Replace("(00)", string.Format("({0:00})", 10));
            Dialog.SetFocus(buttons[_DefaultButton].Id);
            Dialog.Closing += (object sender, ClosingEventArgs e) =>
            {
                timer.Enabled = false;
                _clickedButton = (int)Dialog.Focused.Data;
            };

            Dialog.Idled += (object sender, EventArgs e) =>
            {
                if (AutoClose)
                {
                    Dialog.SetFocus(buttons[_DefaultButton].Id);
                    Dialog.Close();
                }
            };

            Dialog.KeyPressed += (object sender, KeyPressedEventArgs e) =>
            {
                timer.Enabled = false;
            };

            var i = 9;
            timer.Elapsed += (object sender, System.Timers.ElapsedEventArgs e) =>
            {
                if (i < 0)
                {
                    AutoClose = true;
                    timer.Enabled = false;
                    return;
                }

                // potentially dangerous, but seems working fine
                Dialog.Default.Text = _Buttons[_DefaultButton].Replace("(00)", string.Format("({0:00})", i--));
            };
            timer.Enabled = true;
        }
    }
}
