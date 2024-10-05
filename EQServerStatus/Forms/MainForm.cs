using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using EQServerStatus.Classes;
using EQServerStatus.Forms;

namespace EQServerStatus
{
    public partial class MainForm : Form
    {
        private int timeTillServerRefresh;
        private bool minimizeToTrayBool = Properties.Settings.Default.minimizeToTray;
        private List<Triggers> userTriggers = Properties.Settings.Default.triggers;
        private List<Servers> eqServers;
        public const string eqResource = "https://www.eqresource.com";

        public MainForm()
        {
            InitializeComponent();
            // setting up event handlers
            this.refreshServersToolStripMenuItem.Click += delegate { UpdateServerStatus(); };
            this.Resize += delegate { MinimizeToTray(); };
            this.openToolStripMenuItem.Click += new System.EventHandler(this.ShowProgramFromTray);
            this.ServerTreeView.AfterSelect += delegate { RefreshServerInformation(); };
            // if this is the first time the application has ran on this machine,
            // our userTriggers will be null.
            if (userTriggers == null)
            {
                userTriggers = new List<Triggers>();
                Properties.Settings.Default.triggers = userTriggers;
                Properties.Settings.Default.Save();
            }
            // expanding all parent tree nodes
            ServerTreeView.ExpandAll();
            // creating an object for each server
            CreateServerObjects();
            // populating the triggersListView box with our saved triggers, if there are any.
            RefreshTriggersListView();
            // get initial statuses for everquest servers
            UpdateServerStatus();
            // initialize the timer for server updates
            RefreshTimerSetup(0);
        }

        //
        // create server objects
        //

        private void CreateServerObjects()
        {
            // looping through each childNode and creating a server object for each item
            eqServers = new List<Servers>();
            foreach (TreeNode rootNode in ServerTreeView.Nodes)
            {
                foreach (TreeNode childNode in rootNode.Nodes)
                {
                    Servers newServer = new Servers()
                    {
                        ServerName = childNode.Tag.ToString()
                    };
                    eqServers.Add(newServer);
                }
            }
        }

        //
        // update server statuses
        //

        private void UpdateServerStatus()
        {
            using (WebClient wc = new WebClient())
            {
                try
                {
                    mainStatusLabel.Text = "Updating server statuses...";
                    string jsondata = wc.DownloadString("https://census.daybreakgames.com/json/status/eq");

                    // Parse JSON data using JObject
                    JObject jsonResponse = JObject.Parse(jsondata);

                    foreach (TreeNode rootNode in ServerTreeView.Nodes)
                    {
                        foreach (TreeNode childNode in rootNode.Nodes)
                        {
                            // Finding our Servers object for this particular server
                            Servers currentServer = eqServers.FirstOrDefault(x => x.ServerName == childNode.Tag.ToString());
                            if (currentServer == null) continue;

                            string serverType = GetServerType(childNode.Tag.ToString());
                            if (serverType == null) continue;

                            // Retrieve server status and age data from the JSON object
                            JToken serverDataToken = jsonResponse["eq"]?[serverType]?[childNode.Tag.ToString()];
                            if (serverDataToken == null)
                            {
                                ErrorLogListBox.Items.Insert(0, GetTimestamp() + "Error: Server data not found for " + childNode.Tag);
                                continue;
                            }

                            string serverStatus = serverDataToken["status"]?.ToString();
                            int ageSeconds = int.TryParse(serverDataToken["ageSeconds"]?.ToString(), out var age) ? age : 0;

                            // Update the server status and history for the server
                            currentServer.LastUpdated = ageSeconds;

                            var newDataPoint = new ServerDataPoints
                            {
                                HistoryTime = DateTime.Now,
                                HistoryDataPoint = serverStatus
                            };
                            currentServer.ServerHistoryData.Insert(0, newDataPoint);

                            // Process triggers and update childNode based on the status
                            ProcessTriggersAndUpdateNode(childNode, serverStatus);
                        }
                    }

                    mainStatusLabel.Text = "Idle";
                    GeneralLogListBox.Items.Insert(0, GetTimestamp() + "Server statuses successfully updated.");
                }
                catch (Exception e)
                {
                    ErrorLogListBox.Items.Insert(0, GetTimestamp() + e.Message);
                    mainStatusLabel.Text = "ERROR: Check error log for details.";
                }
            }
        }

        private static string GetServerType(string tag)
        {
            switch (tag)
            {
                case "Beta": return "Beta";
                case "Test": return "Test";
                default: return "Live";
            }
        }

        // Processes triggers and updates the TreeNode based on server status
        private void ProcessTriggersAndUpdateNode(TreeNode childNode, string serverStatus)
        {
            if (!string.IsNullOrWhiteSpace(serverStatus))
            {
                // Loop through each Triggers object in userTriggers to see if any trigger should be fired
                foreach (Triggers trigger in userTriggers)
                {
                    if (trigger.Server == childNode.Tag.ToString() && trigger.StatusFrom == childNode.ToolTipText && trigger.StatusTo == serverStatus)
                    {
                        ProcessTrigger(trigger);
                        GeneralLogListBox.Items.Insert(0, GetTimestamp() + $"Trigger for server {trigger.Server} processed.");
                    }
                }

                // Update the childNode based on the server status
                switch (serverStatus)
                {
                    case "low":
                        SetNodeStatus(childNode, "low", 3);
                        break;

                    case "medium":
                        SetNodeStatus(childNode, "medium", 2);
                        break;

                    case "high":
                        SetNodeStatus(childNode, "high", 1);
                        break;

                    case "locked":
                        SetNodeStatus(childNode, "locked", 4);
                        break;

                    case "down":
                        SetNodeStatus(childNode, "down", 6);
                        break;

                    default:
                        SetNodeStatus(childNode, "Can't locate server data...", 5);
                        break;
                }
            }
            else
            {
                ErrorLogListBox.Items.Insert(0, GetTimestamp() + $"Error with {childNode.Tag}");
            }
        }

        // Updates the TreeNode status and image
        private void SetNodeStatus(TreeNode node, string tooltipText, int imageIndex)
        {
            node.ToolTipText = tooltipText;
            node.ImageIndex = imageIndex;
            node.SelectedImageIndex = imageIndex;
        }

        // create/update serverRefresh timer object
        private void RefreshTimerSetup(int refreshTime)
        {
            serverRefreshTimer.Interval = 1000;
            serverRefreshTimer.Enabled = true;
            if (refreshTime > 29 && refreshTime < 10000)
            {
                timeTillServerRefresh = refreshTime;
                GeneralLogListBox.Items.Insert(0, GetTimestamp() + "Set refresh timer to " + refreshTime + "s");
            }
            else
            {
                timeTillServerRefresh = Properties.Settings.Default.refreshTime;
                GeneralLogListBox.Items.Insert(0, GetTimestamp() + "Set refresh timer to " + Properties.Settings.Default.refreshTime + "s");
            }
        }

        private void ServerRefreshTimer_Tick(object sender, EventArgs e)
        {
            int minutes;
            int seconds;

            // each 1 second tick of the timer, we are decreasing our timeTillServerRefresh property by 1
            // next, we are updating the status label in a readable format
            timeTillServerRefresh -= 1;
            minutes = timeTillServerRefresh / 60;
            seconds = timeTillServerRefresh % 60;
            nextUpdateStatusLabel.Text = minutes.ToString() + ":" + seconds.ToString("00");

            // if we've run out of time, updating the server statuses and refreshing the
            // server information page for currently selected childNode
            if (timeTillServerRefresh == 0)
            {
                UpdateServerStatus();
                timeTillServerRefresh = Properties.Settings.Default.refreshTime;
                RefreshServerInformation();
            }
        }

        private void ServerTreeViewNodeClick(object sender, TreeViewCancelEventArgs e)
        {
            // when we click a node, if it's a parent node we are denying the selection
            if (e.Node == ServerTreeView.Nodes[0] || e.Node == ServerTreeView.Nodes[1] || e.Node == ServerTreeView.Nodes[2])
            {
                e.Cancel = true;
            }
        }

        //
        // refresh objects subs
        //

        private void RefreshTriggersListView()
        {
            // clears all items from the list view
            triggersListView.Items.Clear();

            // ensuring our userTriggers list is current
            userTriggers = Properties.Settings.Default.triggers;

            // if there are objects in the userTriggers list, populationg the triggersListView object
            if (userTriggers != null && userTriggers.Count > 0)
            {
                foreach (Triggers t in userTriggers)
                {
                    var lvi = new ListViewItem
                    {
                        Text = t.Server
                    };
                    lvi.SubItems.Add(t.StatusFrom);
                    lvi.SubItems.Add(t.StatusTo);
                    lvi.SubItems.Add(t.AlertRepeating.ToString());

                    triggersListView.Items.Insert(0, lvi);
                }
            }
        }

        // refreshes the server information tab for currently selected childNode
        private void RefreshServerInformation()
        {
            TreeNode selectedNode = ServerTreeView.SelectedNode;
            if (selectedNode == null) return;

            // Attempt to find the server object matching the selected node's tag
            Servers currentServer = eqServers.FirstOrDefault(x => x.ServerName == selectedNode.Tag?.ToString());
            serverNameLabel.Text = selectedNode.Text;

            if (currentServer == null)
            {
                // Clear the display and list view if no matching server is found
                serverLastUpdatedLabel.Text = "Server information not available.";
                serverHistoryListView.Items.Clear();
                return;
            }

            // Calculate the last updated time in minutes and seconds
            int minutes = currentServer.LastUpdated / 60;
            int seconds = currentServer.LastUpdated % 60;
            serverLastUpdatedLabel.Text = $"This server's data was updated {minutes}m {seconds}s before last server refresh.";

            // Populate the serverHistoryListView with each ServerDataPoints object for the current server
            serverHistoryListView.Items.Clear();
            foreach (ServerDataPoints sdp in currentServer.ServerHistoryData)
            {
                var listViewItem = new ListViewItem
                {
                    Text = sdp.HistoryTime.ToString()
                };
                listViewItem.SubItems.Add(sdp.HistoryDataPoint);

                serverHistoryListView.Items.Insert(0, listViewItem);
            }
        }

        //
        // trigger creation/deletion/execution subs
        //

        private void TriggerCreationCreateButton_Click(object sender, EventArgs e)
        {
            // ensuring each user has made a selection in each combo box
            if (!string.IsNullOrWhiteSpace(triggerCreationServerSelectComboBox.Text) && !string.IsNullOrWhiteSpace(triggerCreationStatusFromComboBox.Text) && !string.IsNullOrWhiteSpace(triggerCreationStatusToComboBox.Text) && !string.IsNullOrWhiteSpace(triggerCreationAlertOnceComboBox.Text) && !string.IsNullOrWhiteSpace(triggerCreationAlertOnceComboBox.Text))
            {
                bool alertRepeatingSetting;

                if (triggerCreationAlertOnceComboBox.Text == "Always")
                {
                    alertRepeatingSetting = true;
                }
                else
                {
                    alertRepeatingSetting = false;
                }

                // creating a new Triggers object based on users input
                Triggers newTrigger = new Triggers()
                {
                    Server = triggerCreationServerSelectComboBox.Text,
                    StatusFrom = triggerCreationStatusFromComboBox.Text,
                    StatusTo = triggerCreationStatusToComboBox.Text,
                    AlertRepeating = alertRepeatingSetting
                };

                // ensuring the new Triggers object is free of errors
                // if no errors, add the object to the userTriggers list and save updated list in settings
                if (!CheckTriggerForErrors(newTrigger))
                {
                    userTriggers.Add(newTrigger);
                    Properties.Settings.Default.triggers = userTriggers;
                    Properties.Settings.Default.Save();
                    RefreshTriggersListView();
                    ClearCreateTriggerForm();
                }
            }
            else
            {
                MessageBox.Show("Error: You must fill in all values to create an alert trigger!");
            }
        }

        private bool CheckTriggerForErrors(Triggers newTrigger)
        {
            // looping through each trigger in userTriggers to ensure the trigger we
            // are trying to create is not a duplicate
            foreach (Triggers t in userTriggers)
            {
                if (t.Server == newTrigger.Server && t.StatusFrom == newTrigger.StatusFrom && t.StatusTo == newTrigger.StatusTo)
                {
                    MessageBox.Show("Error: Duplicate trigger!" + Environment.NewLine + Environment.NewLine + "If you are trying to change your Alert method or frequency, please delete the previous trigger for Server " + t.Server + " with Status From of " + t.StatusFrom + " and Status To of " + t.StatusTo + " and create a new trigger.");
                    return true;
                }
            }

            // ensuring the status to and status from properties are not the same
            if (newTrigger.StatusFrom == newTrigger.StatusTo)
            {
                MessageBox.Show("Your trigger cannot have the same Status From and Status To properties!");
                return true;
            }

            // if we reach this point, the new Triggers object is valid and will be created
            return false;
        }

        // clears the combo boxes for our create new trigger tab
        private void ClearCreateTriggerForm()
        {
            triggerCreationServerSelectComboBox.ResetText();
            triggerCreationStatusFromComboBox.ResetText();
            triggerCreationStatusToComboBox.ResetText();
            triggerCreationAlertOnceComboBox.ResetText();
        }

        private void TriggerRemoveSelectedTriggerButton_Click(object sender, EventArgs e)
        {
            // if user clicks remove trigger button while no trigger is selected, throwing
            // an error.  if a trigger is selected, calling the reoveTriggerFromList sub
            if (triggersListView.SelectedItems.Count > 0)
            {
                RemoveTriggerFromList(null);
            }
            else
            {
                MessageBox.Show("Error: You must select a trigger from the 'My Triggers' box in order to remove a trigger!");
            }
        }

        private void RemoveTriggerFromList(Triggers t)
        {
            // if the call came from the processTrigger sub, we passed the trigger to remove to this sub
            // otherwise, the call came from the delete trigger button, in which case we need to locate the trigger selected
            if (t == null)
            {
                ListViewItem itemToRemove = triggersListView.SelectedItems[0];
                userTriggers.RemoveAll(x => x.Server == itemToRemove.Text && x.StatusFrom == itemToRemove.SubItems[1].Text && x.StatusTo == itemToRemove.SubItems[2].Text);
                GeneralLogListBox.Items.Insert(0, GetTimestamp() + "The trigger for " + itemToRemove.Text + " from " + itemToRemove.SubItems[1].Text + " to " + itemToRemove.SubItems[2].Text + " has been removed!");
            }
            else
            {
                userTriggers.Remove(t);
                GeneralLogListBox.Items.Insert(0, GetTimestamp() + "The trigger for " + t.Server + " from " + t.StatusFrom + " to " + t.StatusTo + " has been removed!");
            }

            // updating our settings, and calling the refreshTriggersListView sub to update the serverTreeView
            Properties.Settings.Default.triggers = userTriggers;
            Properties.Settings.Default.Save();
            RefreshTriggersListView();
        }

        private void ProcessTrigger(Triggers t)
        {
            // a trigger has fired, let's show an alert!
            programNotifyIcon.ShowBalloonTip(1000, t.Server, t.Server + " has moved from a status of " + t.StatusFrom + " to a status of " + t.StatusTo + ".", ToolTipIcon.Info);

            // if the trigger isn't set to repeat, we need to remove from listView and userTriggers list
            if (!t.AlertRepeating)
            {
                RemoveTriggerFromList(t);
            }
        }

        //
        // get formatted timestamp
        //

        private static string GetTimestamp() => DateTime.Now.ToString("[MM/dd/yyyy hh:mm:ss] ");

        //
        // minimize to system tray subs
        //

        private void MinimizeToTraySetup(bool minimize)
        {
            if (minimize)
            {
                minimizeToTrayBool = minimize;
                Properties.Settings.Default.minimizeToTray = minimize;
                Properties.Settings.Default.Save();
            }
        }

        private void MinimizeToTray()
        {
            if (this.WindowState == FormWindowState.Minimized && minimizeToTrayBool)
            {
                Hide();
                programNotifyIcon.Visible = true;
            }
        }

        private void ShowProgramFromTray(object sender, EventArgs e)
        {
            this.Show();
            programNotifyIcon.Visible = false;
        }

        //
        // application settings form
        //

        private void SetRefreshTimerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (ApplicationSettings apps = new ApplicationSettings())
            {
                var result = apps.ShowDialog();

                if (result == DialogResult.OK)
                {
                    int refreshValue = apps.returnRefreshTimer;
                    RefreshTimerSetup(refreshValue);
                    bool minimizeValue = apps.returnMinimizeToTray;
                    MinimizeToTraySetup(minimizeValue);
                }
            }
        }

        //
        // various menu click subs
        //

        private void EQResourcecomToolStripMenuItem_Click(object sender, EventArgs e) => System.Diagnostics.Process.Start(eqResource);

        private void UserGuideToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox ab = new AboutBox();
            ab.ShowDialog();
        }

        private void ExitToolStripMenuItem1_Click(object sender, EventArgs e) => this.Close();

        private void TriggerNotifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) => this.Show();

        private void RefreshDataToolStripMenuItem_Click(object sender, EventArgs e) => UpdateServerStatus();
    }
}