using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;

namespace RemoteEmu1
{
    public partial class RemoteEmu1Form : Form
    {
        ScriptConsole Console;

        public RemoteEmu1Form()
        {
            Console = new ScriptConsole(new IPEndPoint(IPAddress.Loopback, 5000));                        // TODO get port and addr from config file
            InitializeComponent();
        }

        private void RemoteEmu1Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            Console.Close();
        }
    }
}
