using System;
using System.Windows.Forms;

namespace EQServerStatus.Forms
{
    public partial class AboutBox : Form
    {
        public AboutBox()
        {
            InitializeComponent();
        }

        private void AboutBoxOkButton_Click(object sender, EventArgs e) => this.Close();

        private void LaunchEQResourceURL(object sender, LinkLabelLinkClickedEventArgs e) => System.Diagnostics.Process.Start(MainForm.eqResource);
    }
}
