﻿using System;
using System.Windows.Forms;

namespace EQServerStatus.Forms
{
    public partial class ApplicationSettings : Form
    {
        public int returnRefreshTimer;
        public bool returnMinimizeToTray;

        public ApplicationSettings()
        {
            InitializeComponent();
            setRefreshTimerMaskedTextBox.Text = Properties.Settings.Default.refreshTime.ToString();
            minimizeToTrayCheckbox.Checked = Properties.Settings.Default.minimizeToTray;
        }

        private void CancelSettingsButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void SaveSettingsButton_Click(object sender, EventArgs e)
        {
            if (int.Parse(setRefreshTimerMaskedTextBox.Text) > 29  && int.Parse(setRefreshTimerMaskedTextBox.Text) < 10000)
            {
                Properties.Settings.Default.refreshTime = int.Parse(setRefreshTimerMaskedTextBox.Text);
                Properties.Settings.Default.Save();
                

            } else
            {
                MessageBox.Show("Invalid value entered: Refresh timer value must be between 30 and 9,999 seconds.");
                return;
            }

            this.returnRefreshTimer = int.Parse(setRefreshTimerMaskedTextBox.Text);
            this.returnMinimizeToTray = minimizeToTrayCheckbox.Checked;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
