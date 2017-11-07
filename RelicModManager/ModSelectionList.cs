﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Text.RegularExpressions;
using System.Net;
using System.Xml.Linq;
using System.Xml.XPath;

namespace RelhaxModpack
{
    //the mod selectin window. allows users to select which mods they wish to install
    public partial class ModSelectionList : Form
    {
        public List<Category> parsedCatagoryList;//can be grabbed by MainWindow
        public List<Mod> userMods;//can be grabbed by MainWindow
        public bool cancel = true;//used to determine if the user canceled
        private Preview p;
        private PleaseWait pw;
        public List<Dependency> globalDependencies;
        public List<Dependency> dependencies;
        public List<LogicalDependency> logicalDependencies;
        public List<SelectableDatabasePackage> completeModSearchList;
        private bool loadingConfig = false;
        private bool taskBarHidden = false;
        private const int titleBar = 23;//set origionally for 23
        private int difference = 0;
        private string tanksVersion = "";
        private string tanksLocation = "";
        private int mainWindowStartX = 0;
        private int mainWindowStartY = 0;
        bool hasRadioButtonConfigSelected = false;
        bool modHasRadioButtons = false;
        bool firstLoad = true;
        bool ignoreSelections = true;
        private static string NoDescriptionAvailable = Translations.getTranslatedString("noDescription");
        //bool mouseCLick = false;
        int prog = 0;
        private enum loadConfigMode
        {
            error = -1,
            fromButton = 0,//this is the default state
            fromSaveLastConfig = 1,//this is the state if the user selected the setting "save last install's selection"
            fromAutoInstall = 2,//this is for when the user started the application in auto install mode. this takes precedence over the above 2
            fromModInfoDat = 3//this is when the manager is reading the modInfo.xml from the downloaded moInfo.dat file
        };
        private loadConfigMode loadMode = loadConfigMode.fromButton;

        public ModSelectionList(string version, string theTanksVersion, int mainWindowX, int mainWindowY)
        {
            InitializeComponent();
            tanksVersion = version;
            tanksLocation = theTanksVersion;
            mainWindowStartX = mainWindowX;
            mainWindowStartY = mainWindowY;
        }

        private void applyTranslations()
        {
            continueButton.Text = Translations.getTranslatedString(continueButton.Name);
            cancelButton.Text = Translations.getTranslatedString(cancelButton.Name);
            helpLabel.Text = Translations.getTranslatedString(helpLabel.Name);
            loadConfigButton.Text = Translations.getTranslatedString(loadConfigButton.Name);
            saveConfigButton.Text = Translations.getTranslatedString(saveConfigButton.Name);
            label2.Text = Translations.getTranslatedString(label2.Name);
            clearSelectionsButton.Text = Translations.getTranslatedString(clearSelectionsButton.Name);
            colapseAllButton.Text = Translations.getTranslatedString(colapseAllButton.Name);
            expandAllButton.Text = Translations.getTranslatedString(expandAllButton.Name);
        }

        //called on application startup
        private void ModSelectionList_Load(object sender, EventArgs e)
        {
            //create the loading window
            pw = new PleaseWait(mainWindowStartX, mainWindowStartY);
            pw.Show();
            prog = 0;
            //font scaling
            this.AutoScaleMode = Settings.AppScalingMode;
            this.Font = Settings.AppFont;
            if (Settings.AppScalingMode == System.Windows.Forms.AutoScaleMode.Dpi)
            {
                this.Scale(new System.Drawing.SizeF(Settings.ScaleSize, Settings.ScaleSize));
            }
            //apply the translations
            this.applyTranslations();
            pw.loadingDescBox.Text = Translations.getTranslatedString("readingDatabase");
            Application.DoEvents();
            string databaseURL = Settings.ModInfoDatFile;
            if (Program.testMode)
            {
                // if customModInfoPath is empty, this creates a full valid path to the current manager location folder
                databaseURL = Path.Combine(string.IsNullOrEmpty(Settings.CustomModInfoPath) ? Application.StartupPath : Settings.CustomModInfoPath, "modInfo.xml");
                if (!File.Exists(databaseURL))
                {
                    Utils.AppendToLog("Databasefile not found: " + databaseURL);
                    MessageBox.Show(string.Format(Translations.getTranslatedString("testModeDatabaseNotFound"), databaseURL));
                    Application.Exit();
                }
                Settings.TanksOnlineFolderVersion = XMLUtils.ReadOnlineFolderFromModInfo(databaseURL);
            }
            else if (Program.betaDatabase)
            {
                Utils.AppendToLog("downloading modInfo.dat (betaDatabase url)");
                string xmlString = Utils.GetStringFromZip(Settings.ManagerInfoDatFile, "manager_version.xml");
                XDocument doc = XDocument.Parse(xmlString);
                //parse the database version
                databaseURL = doc.XPathSelectElement("//version/database_beta_url").Value;
                string localDest = Path.Combine(Application.StartupPath, "RelHaxTemp", "modInfo_beta.xml");
                //always delete the file before redownloading
                if (File.Exists(localDest))
                    File.Delete(localDest);
                using (WebClient downloader = new WebClient() { Proxy = null })
                {
                    try
                    {
                        downloader.DownloadFile(databaseURL, localDest);
                        databaseURL = localDest;
                    }
                    catch (Exception ex)
                    {
                        Utils.ExceptionLog("ModSelectionList_Load", string.Format(@"Tried to access {0}", databaseURL), ex);
                        MessageBox.Show(string.Format("{0} modInfo.dat", Translations.getTranslatedString("failedToDownload_1")));
                        Application.Exit();
                    }
                }
            }
            else
            {
                //download the modInfo.dat
                Utils.AppendToLog("downloading modInfo.dat");
                string dlURL = "";
                using (WebClient downloader = new WebClient() { Proxy = null })
                {
                    try
                    {
                        dlURL = string.Format("http://wotmods.relhaxmodpack.com/WoT/{0}/modInfo.dat", Settings.TanksOnlineFolderVersion);
                        downloader.DownloadFile(dlURL, Settings.ModInfoDatFile);
                    }
                    catch (Exception ex)
                    {
                        Utils.ExceptionLog(string.Format("ModSelectionList_Load", @"Tried to access {0}", dlURL), ex);
                        MessageBox.Show(string.Format("{0} modInfo.dat", Translations.getTranslatedString("failedToDownload_1")));
                        Application.Exit();
                    }
                }
            }
            //create new lists for memory database and serialize from xml->lists
            globalDependencies = new List<Dependency>();
            parsedCatagoryList = new List<Category>();
            dependencies = new List<Dependency>();
            logicalDependencies = new List<LogicalDependency>();
            XMLUtils.CreateModStructure(databaseURL, globalDependencies, dependencies, logicalDependencies, parsedCatagoryList);
            if (Program.testMode)
            {
                if (Utils.Duplicates(parsedCatagoryList))
                {
                    Utils.AppendToLog("WARNING: Duplicate mod name detected!!");
                    MessageBox.Show(Translations.getTranslatedString("duplicateMods"), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Application.Exit();
                }
                int duplicatesCounter = 0;
                if (Utils.DuplicatesPackageName(parsedCatagoryList, ref duplicatesCounter))
                {
                    Utils.AppendToLog(string.Format("ERROR: {0} duplicate PackageName's detected", duplicatesCounter));
                    MessageBox.Show(string.Format("ERROR: {0} duplicate PackageName's detected!\n\nmore information, see Logfile ...", duplicatesCounter), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Application.Exit();
                }
            }
            pw.loadingDescBox.Text = Translations.getTranslatedString("buildingUI");
            Application.DoEvents();
            this.initUserMods();
            this.initDependencies();
            //the default loadConfig mode shold be from clicking the button
            loadMode = loadConfigMode.fromButton;
            //if the load config from last selection is checked, then set the mode to it
            if (Settings.SaveLastConfig)
            {
                loadMode = loadConfigMode.fromSaveLastConfig;
            }
            //if this is in auto install mode, then it takes precedence
            if (Program.autoInstall)
            {
                loadMode = loadConfigMode.fromAutoInstall;
            }
            //check which mode the load config is in and act accordingly. it should only load the config in one of the following scenarios
            if (loadMode == loadConfigMode.fromSaveLastConfig)
            {
                this.parseLoadConfig();
            }
            else if (loadMode == loadConfigMode.fromAutoInstall)
            {
                this.parseLoadConfig();
                this.cancel = false;
                pw.Close();
                pw.Dispose();
                this.Close();
                return;
            }
            //build the UI display
            if (pw != null)
            {
                pw.progres_max = XMLUtils.TotalModConfigComponents;
                pw.SetProgress(0);
            }
            loadingConfig = true;
            Utils.AppendToLog("Loading ModSelectionList with view " + Settings.SView);
            completeModSearchList = new List<SelectableDatabasePackage>();
            if (modTabGroups.TabPages.Count > 0)
                modTabGroups.TabPages.Clear();
            modTabGroups.Font = Settings.AppFont;
            this.addAllMods();
            this.FinishLoad();
        }

        private void FinishLoad()
        {
            this.addUserMods(false);
            loadingConfig = false;
            firstLoad = false;
            //set the size to the last closed size
            this.Size = new Size(Settings.ModSelectionWidth, Settings.ModSelectionHeight);
            //set the UI colors
            Settings.setUIColor(this);
            pw.Close();
            pw.Dispose();
            //set label properties
            TanksVersionLabel.Text = TanksVersionLabel.Text + tanksVersion;
            TanksPath.Text = string.Format(Translations.getTranslatedString("InstallingTo"), tanksLocation);
            //if the task bar was set to auto hide, set it to always on top
            //it will be set back to auto hide when this window closes
            Settings.AppBarStates currentState = Settings.GetTaskbarState();
            if (currentState == Settings.AppBarStates.AutoHide)
            {
                taskBarHidden = true;
                Settings.SetTaskbarState(Settings.AppBarStates.AlwaysOnTop);
            }
            //get the maximum height of the screen
            //get the size of the title bar window
            Rectangle screenRektangle = RectangleToScreen(this.ClientRectangle);
            int titleHeight = screenRektangle.Top - this.Top;
            //largest possible is 46
            //mine (programmed for) is 23
            if (titleHeight > titleBar)
            {
                difference = titleHeight - titleBar;
            }
            //force a resize
            this.ModSelectionList_SizeChanged(null, null);
            if (Settings.ModSelectionFullscreen)
            {
                this.WindowState = FormWindowState.Maximized;
            }
            if (Settings.SView == Settings.SelectionView.Default)
            {
                colapseAllButton.Enabled = false;
                colapseAllButton.Visible = false;
                expandAllButton.Enabled = false;
                expandAllButton.Visible = false;
            }
            else
            {
                colapseAllButton.Enabled = true;
                colapseAllButton.Visible = true;
                expandAllButton.Enabled = true;
                expandAllButton.Visible = true;
            }
            MainWindow.usedFilesList = Utils.CreateUsedFilesList(parsedCatagoryList, globalDependencies, dependencies, logicalDependencies);
        }
        //initializes the userMods list. This should only be run once
        private void initUserMods()
        {
            //create all the user mod objects
            string modsPath = Path.Combine(Application.StartupPath, "RelHaxUserMods");
            string[] userModFiles = Directory.GetFiles(modsPath);
            userMods = new List<Mod>();
            foreach (string s in userModFiles)
            {
                if (Path.GetExtension(s).Equals(".zip"))
                {
                    Mod m = new Mod();
                    m.ZipFile = s;
                    m.name = Path.GetFileNameWithoutExtension(s);
                    m.Enabled = true;
                    userMods.Add(m);
                }
            }
        }
        //initialize the globalDependency, dependency, and logicalDependency list. should only be done once
        private void initDependencies()
        {
            string modDownloadFilePath = "";
            //init the global dependencies
            foreach (Dependency d in globalDependencies)
            {
                //default to false, then check if it can be true
                d.DownloadFlag = false;
                modDownloadFilePath = Path.Combine(Application.StartupPath, "RelHaxDownloads", d.ZipFile);
                //get the local md5 hash. a -1 indicates the file is not on the disk
                string oldCRC2 = XMLUtils.GetMd5Hash(modDownloadFilePath);
                if ((!d.ZipFile.Equals("")) && (!d.CRC.Equals(oldCRC2)))
                {
                    d.DownloadFlag = true;
                }
            }
            //init the dependencies
            foreach (Dependency d in dependencies)
            {
                //default to false, then check if it can be true
                d.DownloadFlag = false;
                modDownloadFilePath = Path.Combine(Application.StartupPath, "RelHaxDownloads", d.ZipFile);
                //get the local md5 hash. a -1 indicates the file is not on the disk
                string oldCRC2 = XMLUtils.GetMd5Hash(modDownloadFilePath);
                if ((!d.ZipFile.Equals("")) && (!d.CRC.Equals(oldCRC2)))
                {
                    d.DownloadFlag = true;
                }
            }
            //init the logicalDependencies
            foreach (LogicalDependency d in logicalDependencies)
            {
                //default to false, then check if it can be true
                d.DownloadFlag = false;
                modDownloadFilePath = Path.Combine(Application.StartupPath, "RelHaxDownloads", d.ZipFile);
                //get the local md5 hash. a -1 indicates the file is not on the disk
                string oldCRC2 = XMLUtils.GetMd5Hash(modDownloadFilePath);
                if ((!d.ZipFile.Equals("")) && (!d.CRC.Equals(oldCRC2)))
                {
                    d.DownloadFlag = true;
                }
            }
        }
        //adds all usermods to thier own userMods tab
        private void addUserMods(bool forceUnchecked)
        {
            //make the new tab
            TabPage tb = new TabPage("User Mods");
            tb.AutoScroll = true;
            //add all mods to the tab page
            for (int i = 0; i < userMods.Count; i++)
            {
                //make modCheckBox
                ModFormCheckBox modCheckBox = new ModFormCheckBox();
                userMods[i].modFormCheckBox = modCheckBox;
                modCheckBox.mod = userMods[i];
                modCheckBox.Font = Settings.AppFont;
                modCheckBox.AutoSize = true;
                int yLocation = 3 + (modCheckBox.Size.Height * (i));
                modCheckBox.Location = new System.Drawing.Point(3, yLocation);
                modCheckBox.TabIndex = 1;
                modCheckBox.Text = userMods[i].name;
                if (forceUnchecked)
                {
                    modCheckBox.Checked = false;
                }
                else
                {
                    modCheckBox.Checked = userMods[i].Checked;
                }
                modCheckBox.UseVisualStyleBackColor = true;
                modCheckBox.Enabled = true;
                modCheckBox.CheckedChanged += new EventHandler(modCheckBox_CheckedChanged);
                tb.Controls.Add(modCheckBox);
            }
            modTabGroups.TabPages.Add(tb);
        }
        //adds all the tab pages for each catagory
        //must be only one catagory
        private void addAllMods()
        {
            foreach (Category c in parsedCatagoryList)
            {
                TabPage t = new TabPage(c.name)
                {
                    AutoScroll = true,
                    //link the names of catagory and tab so eithor can be searched for
                    Name = c.name,
                    Text = c.selectionType.Equals("single") ? c.name + "*" : c.name
                };
                c.TabPage = t;
                //matched the catagory to tab
                //add to the ui every mod of that catagory
                Utils.SortModsList(c.mods);
                
                LegacySelectionList lsl = null;
                if (Settings.SView == Settings.SelectionView.Legacy)
                {
                    //create the WPF host for this tabPage
                    ElementHost host = new ElementHost();
                    host.Location = new Point(5, 5);
                    host.Size = new Size(t.Size.Width - 5 - 5, t.Size.Height - 5 - 5);
                    host.BackColorTransparent = false;
                    host.BackColor = Color.White;
                    lsl = new LegacySelectionList();
                    host.Child = lsl;
                    lsl.legacyTreeView.Items.Clear();
                    t.Controls.Add(host);
                }
                modTabGroups.TabPages.Add(t);
            }
            foreach (Category c in parsedCatagoryList)
            {
                foreach (Mod m in c.mods)
                {
                    
                    if (pw != null)
                    {
                        pw.loadingDescBox.Text = string.Format("{0} {1}", Translations.getTranslatedString("loading"), m.name);
                        prog++;
                        pw.SetProgress(prog);
                        Application.DoEvents();
                    }
                    
                    if (Settings.SView == Settings.SelectionView.Default)
                    {
                        //use default UI
                        this.AddModDefaultView(m, c.TabPage, c);
                    }
                    else if (Settings.SView == Settings.SelectionView.Legacy)
                    {
                        //use legacy OMC UI
                        ElementHost h = (ElementHost)c.TabPage.Controls[0];
                        this.AddModOMCView(m, c.TabPage, (LegacySelectionList)h.Child, c);
                    }
                    else
                    {
                        //default case, use default view
                        this.AddModDefaultView(m, c.TabPage, c);
                    }
                }
            }
        }
        //adds a mod m to a tabpage t, OMC treeview style
        private void AddModOMCView(Mod m, TabPage t, LegacySelectionList lsl, Category c)
        {
            if (!m.visible)
                return;
            if (Settings.DarkUI)
                lsl.legacyTreeView.Background = System.Windows.Media.Brushes.Gray;
            //helpfull stuff
            string modDownloadFilePath = Path.Combine(Application.StartupPath, "RelHaxDownloads", m.ZipFile);
            hasRadioButtonConfigSelected = false;
            modHasRadioButtons = false;
            //link the catagory and mod in memory
            m.parent = c;
            //create base mod checkbox
            ModWPFCheckBox modCheckBox = new ModWPFCheckBox();
            //add the ToolTip description to the checkbox
            //http://wpftutorial.net/ToolTip.html
            modCheckBox.ToolTip = m.description.Equals("")? NoDescriptionAvailable : m.description;
            //use a custom datatype for the name
            modCheckBox.mod = m;
            modCheckBox.catagory = c;
            //add the root UI object to the memory database
            m.modFormCheckBox = modCheckBox;
            m.tabIndex = t;
            completeModSearchList.Add(m);
            // completeModSearchList_New.Add(m);
            switch (Settings.FontSizeforum)
            {
                case Settings.FontSize.Font100:
                    break;
                case Settings.FontSize.Font125:
                    modCheckBox.FontSize = modCheckBox.FontSize + 4;
                    break;
                case Settings.FontSize.Font175:
                    modCheckBox.FontSize = modCheckBox.FontSize + 8;
                    break;
                case Settings.FontSize.Font225:
                    modCheckBox.FontSize = modCheckBox.FontSize + 12;
                    break;
                case Settings.FontSize.Font275:
                    modCheckBox.FontSize = modCheckBox.FontSize + 16;
                    break;
            }
            modCheckBox.FontFamily = new System.Windows.Media.FontFamily(Settings.FontName);
            if (Settings.DarkUI)
                modCheckBox.FontWeight = System.Windows.FontWeights.Bold;
            modCheckBox.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            modCheckBox.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
            //make the tree view item for the modCheckBox
            System.Windows.Controls.TreeViewItem tvi = new System.Windows.Controls.TreeViewItem();
            if (Settings.ExpandAllLegacy)
                tvi.IsExpanded = true;
            //process configs
            if (m.configs.Count > 0)
                AddConfigsOMCView(c, m, m.configs, tvi, true);
            string nameForModCB = Utils.ReplaceMacro(m);
            //if there are underscores you need to actually display them #thanksWPF
            nameForModCB = Regex.Replace(nameForModCB, "_", "__");
            modCheckBox.Content = nameForModCB;
            //if the CRC's don't match and the mod actually has a zip file
            if (firstLoad)
            {
                //get the local md5 hash. a -1 indicates the file is not on the disk
                string oldCRC2 = XMLUtils.GetMd5Hash(modDownloadFilePath);
                if ((!m.ZipFile.Equals("")) && (!m.CRC.Equals(oldCRC2)))
                {
                    modCheckBox.Content = string.Format("{0} ({1})", modCheckBox.Content, Translations.getTranslatedString("updated"));
                    m.DownloadFlag = true;
                    if ((m.size > 0))
                        modCheckBox.Content = string.Format("{0} ({1})", modCheckBox.Content, Utils.SizeSuffix(m.size, 1, true));
                }
            }
            else
            {
                if (m.DownloadFlag)
                {
                    modCheckBox.Content = string.Format("{0} ({1})", modCheckBox.Content, Translations.getTranslatedString("updated"));
                    if ((m.size > 0))
                        modCheckBox.Content = string.Format("{0} ({1})", modCheckBox.Content, Utils.SizeSuffix(m.size, 1, true));
                }
            }
            //set mod's Enabled status
            modCheckBox.IsEnabled = m.Enabled;
            //set the mods checked status
            if (m.Enabled && m.Checked)
                modCheckBox.IsChecked = true;

            tvi.Header = modCheckBox;
            //add it's handlers, right click and when checked
            modCheckBox.MouseDown += new System.Windows.Input.MouseButtonEventHandler(Generic_MouseDown);

            //add the mod check box to the legacy tree view
            lsl.legacyTreeView.Items.Add(tvi);
            modCheckBox.Checked += modCheckBoxL_Click;
            modCheckBox.Unchecked += modCheckBoxL_Click;
        }

        void AddConfigsOMCView(Category c, Mod m, List<Config> configs, System.Windows.Controls.TreeViewItem tvi, bool parentIsMod = false, Config parentConfig = null)
        {
            //create the twp possible drop down options, and the mod optional config check box i guess
            ConfigWPFComboBox configControlDD = new ConfigWPFComboBox();
            configControlDD.Items.Clear();
            configControlDD.IsEditable = false;
            //configControlDD.FontSize = Settings.fontSize;
            configControlDD.Name = "notAddedYet";
            configControlDD.IsEnabled = false;
            ConfigWPFComboBox configControlDD2 = new ConfigWPFComboBox();
            configControlDD2.Items.Clear();
            configControlDD2.IsEditable = false;
            //configControlDD2.FontSize = Settings.fontSize;
            configControlDD2.Name = "notAddedYet";
            configControlDD2.IsEnabled = false;
            //process the configs
            foreach (Config con in configs)
            {
                if (!con.visible)
                    continue;
                //link stuff in memory
                con.parentMod = m;
                if (parentIsMod)
                {
                    // completeModSearchList_New.Add(con);
                    completeModSearchList.Add(con);
                    con.parent = m;
                }
                else
                    con.parent = parentConfig;
                //create the init stuff for each config
                ConfigWPFComboBox configControlDDALL = null;
                if (con.type.Equals("single") || con.type.Equals("single1"))
                {
                    modHasRadioButtons = true;
                    //make the radio button
                    ConfigWPFRadioButton configControlRB = new ConfigWPFRadioButton();
                    configControlRB.ToolTip = con.description.Equals("")? NoDescriptionAvailable : con.description;
                    switch (Settings.FontSizeforum)
                    {
                        case Settings.FontSize.Font100:
                            break;
                        case Settings.FontSize.Font125:
                            configControlRB.FontSize = configControlRB.FontSize + 4;
                            break;
                        case Settings.FontSize.Font175:
                            configControlRB.FontSize = configControlRB.FontSize + 8;
                            break;
                    }
                    configControlRB.FontFamily = new System.Windows.Media.FontFamily(Settings.FontName);
                    if (Settings.DarkUI)
                        configControlRB.FontWeight = System.Windows.FontWeights.Bold;
                    configControlRB.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
                    configControlRB.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
                    configControlRB.catagory = c;
                    configControlRB.mod = m;
                    configControlRB.config = con;
                    //add the UI component to the config item in memory database
                    con.configUIComponent = configControlRB;
                    //logic for determining if it can be Enabled
                    configControlRB.IsEnabled = false;
                    configControlRB.IsChecked = false;
                    //get all levels up to the mod, then deal with the mod
                    bool canBeEnabled = true;
                    //check all parent configs, if any
                    if (con.parent is Config)
                    {
                        Config parentConfig2 = (Config)con.parent;
                        while (parentConfig2 is Config)
                        {
                            if (!parentConfig2.Enabled)
                                canBeEnabled = false;
                            if (parentConfig2.parent is Mod)
                                break;
                            parentConfig2 = (Config)parentConfig2.parent;
                        }
                    }
                    //check the parent mod
                    if (!con.parentMod.Enabled)
                        canBeEnabled = false;
                    //check itself (before it reks itself)
                    if (!con.Enabled)
                        canBeEnabled = false;
                    if (canBeEnabled)
                        configControlRB.IsEnabled = true;
                    if (configControlRB.IsEnabled)
                        if (con.Checked)
                            configControlRB.IsChecked = true;
                    //run the checksum logix
                    string nameForModCB = Utils.ReplaceMacro(con);
                    //if there are underscores you need to actually display them #thanksWPF
                    nameForModCB = Regex.Replace(nameForModCB, "_", "__");
                    configControlRB.Content = nameForModCB;
                    if (firstLoad)
                    {
                        string oldCRC = XMLUtils.GetMd5Hash(Path.Combine(Application.StartupPath, "RelHaxDownloads", con.ZipFile));
                        if ((!con.CRC.Equals("")) && (!oldCRC.Equals(con.CRC)))
                        {
                            configControlRB.Content = string.Format("{0} ({1})", configControlRB.Content, Translations.getTranslatedString("updated"));
                            con.DownloadFlag = true;
                            if (con.size > 0)
                                configControlRB.Content = string.Format("{0} ({1})", configControlRB.Content, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    else
                    {
                        if (con.DownloadFlag)
                        {
                            configControlRB.Content = string.Format("{0} ({1})", configControlRB.Content, Translations.getTranslatedString("updated"));
                            if (con.size > 0)
                                configControlRB.Content = string.Format("{0} ({1})", configControlRB.Content, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    //add the handlers at the end
                    configControlRB.Checked += configControlRB_Click;
                    configControlRB.Unchecked += configControlRB_Click;
                    configControlRB.MouseDown += new System.Windows.Input.MouseButtonEventHandler(Generic_MouseDown);
                    //add it to the mod config list
                    System.Windows.Controls.TreeViewItem configControlTVI = new System.Windows.Controls.TreeViewItem();
                    if (Settings.ExpandAllLegacy)
                        configControlTVI.IsExpanded = true;
                    configControlTVI.Header = configControlRB;
                    tvi.Items.Add(configControlTVI);
                    //process the subconfigs
                    if (con.configs.Count > 0)
                        AddConfigsOMCView(c, m, con.configs, configControlTVI, false, con);
                }
                else if (con.type.Equals("single_dropdown") || con.type.Equals("single_dropdown1") || con.type.Equals("single_dropdown2"))
                {
                    //set the all to whichever one it actually is
                    if (con.type.Equals("single_dropdown") || con.type.Equals("single_dropdown1"))
                    {
                        configControlDDALL = configControlDD;
                        //add the UI component to the config item in memory database
                        con.configUIComponent = configControlDD;
                    }
                    else if (con.type.Equals("single_dropdown2"))
                    {
                        configControlDDALL = configControlDD2;
                        //add the UI component to the config item in memory database
                        con.configUIComponent = configControlDD2;
                    }
                    //make the dropdown selection list
                    configControlDDALL.MinWidth = 100;
                    ComboBoxItem cbi = null;
                    string toAdd = Utils.ReplaceMacro(con);
                    //run the CRC logics
                    if (firstLoad)
                    {
                        string oldCRC = XMLUtils.GetMd5Hash(Path.Combine(Application.StartupPath, "RelHaxDownloads", con.ZipFile));
                        if ((!con.CRC.Equals("")) && (!oldCRC.Equals(con.CRC)))
                        {
                            toAdd = string.Format("{0} ({1})", toAdd, Translations.getTranslatedString("updated"));
                            con.DownloadFlag = true;
                            if (con.size > 0)
                                toAdd = string.Format("{0} ({1})", toAdd, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    else
                    {
                        if (con.DownloadFlag)
                        {
                            toAdd = string.Format("{0} ({1})", toAdd, Translations.getTranslatedString("updated"));
                            con.DownloadFlag = true;
                            if (con.size > 0)
                                toAdd = string.Format("{0} ({1})", toAdd, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    //add it
                    if (con.Enabled)
                    {
                        cbi = new ComboBoxItem(con, toAdd);
                        configControlDDALL.Items.Add(cbi);
                        if (con.Checked)
                        {
                            configControlDDALL.SelectedItem = cbi;
                            configControlDDALL.ToolTip = cbi.config.description.Equals("") ? NoDescriptionAvailable : cbi.config.description;
                        }
                    }
                    //add the dropdown to the thing. it will only run this once
                    if (configControlDDALL.Name.Equals("notAddedYet"))
                    {
                        configControlDDALL.Name = "added";
                        configControlDDALL.catagory = c;
                        configControlDDALL.mod = m;
                        configControlDDALL.SelectionChanged += new System.Windows.Controls.SelectionChangedEventHandler(configControlDDALL_SelectionChanged);
                        configControlDDALL.PreviewMouseRightButtonDown += new System.Windows.Input.MouseButtonEventHandler(Generic_MouseDown);
                        if (configControlDDALL.Items.Count > 0)
                            configControlDDALL.IsEnabled = true;
                        if (configControlDDALL.SelectedIndex == -1)
                            configControlDDALL.SelectedIndex = 0;
                        System.Windows.Controls.TreeViewItem configControlTVI = new System.Windows.Controls.TreeViewItem();
                        configControlDDALL.ToolTip = con.description.Equals("") ? NoDescriptionAvailable : con.description;
                        configControlTVI.Header = configControlDDALL;
                        tvi.Items.Add(configControlTVI);
                    }
                }
                else if (con.type.Equals("multi"))
                {
                    //make the checkbox
                    ConfigWPFCheckBox configControlCB = new ConfigWPFCheckBox();
                    //add the tooltip
                    configControlCB.ToolTip = con.description.Equals("") ? NoDescriptionAvailable : con.description;
                    switch (Settings.FontSizeforum)
                    {
                        case Settings.FontSize.Font100:
                            break;
                        case Settings.FontSize.Font125:
                            configControlCB.FontSize = configControlCB.FontSize + 4;
                            break;
                        case Settings.FontSize.Font175:
                            configControlCB.FontSize = configControlCB.FontSize + 8;
                            break;
                    }
                    configControlCB.FontFamily = new System.Windows.Media.FontFamily(Settings.FontName);
                    if (Settings.DarkUI)
                        configControlCB.FontWeight = System.Windows.FontWeights.Bold;
                    configControlCB.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
                    configControlCB.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
                    configControlCB.catagory = c;
                    configControlCB.mod = m;
                    configControlCB.config = con;
                    //add the UI component to the config item in memory database
                    con.configUIComponent = configControlCB;
                    //logic for determining if it can be Enabled
                    configControlCB.IsEnabled = false;
                    configControlCB.IsChecked = false;
                    //get all levels up to the mod, then deal with the mod
                    bool canBeEnabled = true;
                    //check all parent configs, if any
                    if (con.parent is Config)
                    {
                        Config parentConfig2 = (Config)con.parent;
                        while (parentConfig2 is Config)
                        {
                            if (!parentConfig2.Enabled)
                                canBeEnabled = false;
                            if (parentConfig2.parent is Mod)
                                break;
                            parentConfig2 = (Config)parentConfig2.parent;
                        }
                    }
                    //check the parent mod
                    if (!con.parentMod.Enabled)
                        canBeEnabled = false;
                    //check itself (before it reks itself)
                    if (!con.Enabled)
                        canBeEnabled = false;
                    if (canBeEnabled)
                        configControlCB.IsEnabled = true;
                    if (configControlCB.IsEnabled)
                        if (con.Checked)
                            configControlCB.IsChecked = true;
                    //run the checksum logix
                    string nameForModCB = Utils.ReplaceMacro(con);
                    //if there are underscores you need to actually display them #thanksWPF
                    nameForModCB = Regex.Replace(nameForModCB, "_", "__");
                    configControlCB.Content = nameForModCB;
                    if (firstLoad)
                    {
                        string oldCRC = XMLUtils.GetMd5Hash(Path.Combine(Application.StartupPath, "RelHaxDownloads", con.ZipFile));
                        if ((!con.CRC.Equals("")) && (!oldCRC.Equals(con.CRC)))
                        {
                            configControlCB.Content = string.Format("{0} ({1})", configControlCB.Content, Translations.getTranslatedString("updated"));
                            con.DownloadFlag = true;
                            if (con.size > 0)
                                configControlCB.Content = string.Format("{0} ({1})", configControlCB.Content, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    else
                    {
                        if (con.DownloadFlag)
                        {
                            configControlCB.Content = string.Format("{0} ({1})", configControlCB.Content, Translations.getTranslatedString("updated"));
                            con.DownloadFlag = true;
                            if (con.size > 0)
                                configControlCB.Content = string.Format("{0} ({1})", configControlCB.Content, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    //add the handlers at the end
                    configControlCB.Checked += configControlCB_Click;
                    configControlCB.Unchecked += configControlCB_Click;
                    configControlCB.MouseDown += new System.Windows.Input.MouseButtonEventHandler(Generic_MouseDown);
                    //add it to the mod config list
                    System.Windows.Controls.TreeViewItem configControlTVI = new System.Windows.Controls.TreeViewItem();
                    if (Settings.ExpandAllLegacy)
                        configControlTVI.IsExpanded = true;
                    configControlTVI.Header = configControlCB;
                    tvi.Items.Add(configControlTVI);
                    //process the subconfigs
                    if (con.configs.Count > 0)
                        AddConfigsOMCView(c, m, con.configs, configControlTVI, false, con);
                }
                else
                {
                    Utils.AppendToLog(string.Format("WARNING: Unknown config type for {0}: {1}", con.name, con.type));
                }

            }
        }
        //when a legacy mod checkbox is clicked
        void modCheckBoxL_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (loadingConfig)
                return;
            ModWPFCheckBox cb = (ModWPFCheckBox)sender;
            Mod m = cb.mod;
            Category cat = m.parent;
            System.Windows.Controls.TreeViewItem TVI = (System.Windows.Controls.TreeViewItem)cb.Parent;
            System.Windows.Controls.TreeView TV = (System.Windows.Controls.TreeView)TVI.Parent;
            //check to see if this is a single selection categtory
            //if it is, then uncheck the other mod, then check this one
            if ((bool)cb.IsChecked && cat.selectionType.Equals("single"))
            {
                //check if any other mods in this catagory are already checked
                bool anyModsChecked = false;
                foreach (Mod mm in cat.mods)
                {
                    if (mm.Checked)
                    {
                        anyModsChecked = true;
                        mm.Checked = false;
                    }
                }
                if (anyModsChecked)
                {
                    cb.IsChecked = false;
                    //all other mods in this category need to be unchecked
                    foreach (System.Windows.Controls.TreeViewItem tvi in TV.Items)
                    {
                        ModWPFCheckBox modCB = (ModWPFCheckBox)tvi.Header;
                        if ((bool)modCB.IsChecked)
                        {
                            modCB.IsChecked = false;
                            modCB.mod.Checked = false;
                        }
                    }
                }
                //now it is safe to check the mod we want
                cb.Checked -= modCheckBoxL_Click;
                cb.Unchecked -= modCheckBoxL_Click;
                cb.IsChecked = true;
                cb.mod.Checked = true;
                cb.Checked += modCheckBoxL_Click;
                cb.Unchecked += modCheckBoxL_Click;
            }
            //check the mod in memory database
            m.Checked = (bool)cb.IsChecked;
            //this section deals with enabling the configs, if there are any
            if (m.configs.Count == 0)
                return;
            if (m.Checked)
            {
                //mod checked, check at least one single1, one single_dropdown1, one single_dropdown2
                //checking for single/single1 configs
                bool configSelected = false;
                foreach (Config con in m.configs)
                {
                    if ((!con.visible) || (!con.Enabled))
                        continue;
                    if ((con.type.Equals("single")) || (con.type.Equals("single1")))
                    {
                        if (con.Checked)
                            configSelected = true;
                    }
                }
                if (!configSelected)
                {
                    foreach (Config con in m.configs)
                    {
                        if ((!con.visible) || (!con.Enabled))
                            continue;
                        if ((con.type.Equals("single")) || (con.type.Equals("single1")))
                        {
                            con.Checked = true;
                            ConfigWPFRadioButton cwpfrb = (ConfigWPFRadioButton)con.configUIComponent;
                            cwpfrb.IsChecked = true;
                            break;
                        }
                    }
                }
                //checking for single_dropdown/single_dropdown1 configs
                configSelected = false;
                foreach (Config con in m.configs)
                {
                    if ((!con.visible) || (!con.Enabled))
                        continue;
                    if ((con.type.Equals("single_dropdown")) || (con.type.Equals("single_dropdown1")))
                    {
                        if (con.Checked)
                            configSelected = true;
                    }
                }
                if (!configSelected)
                {
                    foreach (Config con in m.configs)
                    {
                        if ((!con.visible) || (!con.Enabled))
                            continue;
                        if ((con.type.Equals("single_dropdown")) || (con.type.Equals("single_dropdown1")))
                        {
                            con.Checked = true;
                            ConfigWPFComboBox cwpfcb = (ConfigWPFComboBox)con.configUIComponent;
                            bool breakOut = false;
                            foreach (ComboBoxItem cbi in cwpfcb.Items)
                            {
                                if (cbi.config.name.Equals(con.name))
                                {
                                    cwpfcb.SelectionChanged -= configControlDDALL_SelectionChanged;
                                    cwpfcb.SelectedItem = cbi;
                                    cwpfcb.SelectionChanged += configControlDDALL_SelectionChanged;
                                    breakOut = true;
                                    break;
                                }
                            }
                            if (breakOut)
                                break;
                        }
                    }
                }
                //checking for single_dropdown2 configs
                configSelected = false;
                foreach (Config con in m.configs)
                {
                    if ((!con.visible) || (!con.Enabled) || (!con.type.Equals("single_dropdown2")))
                        continue;
                    if (con.Checked)
                        configSelected = true;
                }
                if (!configSelected)
                {
                    foreach (Config con in m.configs)
                    {
                        if ((!con.visible) || (!con.Enabled) || (!con.type.Equals("single_dropdown2")))
                            continue;
                        con.Checked = true;
                        ConfigWPFComboBox cwpfcb = (ConfigWPFComboBox)con.configUIComponent;
                        bool breakOut = false;
                        foreach (ComboBoxItem cbi in cwpfcb.Items)
                        {
                            if (cbi.config.name.Equals(con.name))
                            {
                                cwpfcb.SelectionChanged -= configControlDDALL_SelectionChanged;
                                cwpfcb.SelectedItem = cbi;
                                cwpfcb.SelectionChanged += configControlDDALL_SelectionChanged;
                                breakOut = true;
                                break;
                            }
                        }
                        if (breakOut)
                            break;
                    }
                }
            }
            else
            {
                //mod not checked, uncheck all the configs
                foreach (Config cfg in m.configs)
                {
                    if (cfg.Enabled)
                    {
                        cfg.Checked = false;
                        // if (cfg.configUIComponent is ConfigFormCheckBox)
                        if (cfg.configUIComponent is ConfigWPFCheckBox)
                        {
                            // ConfigFormCheckBox cfcb = (ConfigFormCheckBox)cfg.configUIComponent;
                            ConfigWPFCheckBox cfcb = (ConfigWPFCheckBox)cfg.configUIComponent;
                            // cfcb.Checked = false;
                            cfcb.IsChecked = false;
                        }
                        // else if (cfg.configUIComponent is ConfigFormRadioButton)
                        else if (cfg.configUIComponent is ConfigWPFRadioButton) 
                        {
                            // ConfigFormRadioButton cfrb = (ConfigFormRadioButton)cfg.configUIComponent;
                            ConfigWPFRadioButton cfrb = (ConfigWPFRadioButton)cfg.configUIComponent;
                            // cfrb.Checked = false;
                            cfrb.IsChecked = false;
                        }
                        // else if (cfg.configUIComponent is ConfigFormComboBox)
                        else if (cfg.configUIComponent is ConfigWPFComboBox) 
                        {
                            // ConfigFormComboBox cfcb = (ConfigFormComboBox)cfg.configUIComponent;
                            ConfigWPFComboBox cfcb = (ConfigWPFComboBox)cfg.configUIComponent;
                            // cfcb.SelectedIndexChanged -= configControlDD_SelectedIndexChanged;
                            cfcb.SelectionChanged -= configControlDDALL_SelectionChanged;
                            cfcb.SelectedIndex = -1;
                            // cfcb.SelectedIndexChanged += configControlDD_SelectedIndexChanged;
                            cfcb.SelectionChanged += configControlDDALL_SelectionChanged;
                        }
                    }
                }
            }
        }
        //when a legacy checkbox of OMC view is clicked
        void configControlCB_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (loadingConfig)
                return;
            //checkboxes still don't need to be be unselected
            ConfigWPFCheckBox cb = (ConfigWPFCheckBox)sender;
            Mod m = cb.mod;
            Config cfg = cb.config;
            System.Windows.Controls.TreeViewItem tvi = (System.Windows.Controls.TreeViewItem)cb.Parent;
            cfg.Checked = (bool)cb.IsChecked;
            //propagate the check if required
            if (tvi.Parent is System.Windows.Controls.TreeViewItem && cfg.Checked)
            {
                System.Windows.Controls.TreeViewItem parenttvi = (System.Windows.Controls.TreeViewItem)tvi.Parent;
                if (parenttvi.Header is ModWPFCheckBox)
                {
                    ModWPFCheckBox c = (ModWPFCheckBox)parenttvi.Header;
                    c.IsChecked = true;
                }
                else if (parenttvi.Header is ConfigWPFCheckBox)
                {
                    ConfigWPFCheckBox c = (ConfigWPFCheckBox)parenttvi.Header;
                    c.IsChecked = true;
                }
                else if (parenttvi.Header is ConfigWPFRadioButton)
                {
                    ConfigWPFRadioButton c = (ConfigWPFRadioButton)parenttvi.Header;
                    c.IsChecked = true;
                }
            }
            //process the subconfigs
            bool configSelected = false;
            int radioButtonCount = 0;
            if (cfg.configs.Count > 0 && cfg.Checked)
            {
                //determine if at least one radioButton is checked
                foreach (System.Windows.Controls.TreeViewItem subTVI in tvi.Items)
                {
                    if (subTVI.Header is ConfigWPFRadioButton)
                    {
                        radioButtonCount++;
                        ConfigWPFRadioButton subRB = (ConfigWPFRadioButton)subTVI.Header;
                        Config subc = subRB.config;
                        if ((bool)subRB.IsEnabled && (bool)subRB.IsChecked)
                        {
                            //getting here means cb is Enabled
                            subRB.IsEnabled = true;
                            //this needs to be changed
                            if (subc.Checked)
                                configSelected = true;
                        }
                    }
                }
                if (!configSelected && (bool)cb.IsChecked && (bool)cb.IsEnabled && radioButtonCount > 0)
                {
                    foreach (System.Windows.Controls.TreeViewItem subTVI in tvi.Items)
                    {
                        if (subTVI.Header is ConfigWPFRadioButton)
                        {
                            ConfigWPFRadioButton subRB = (ConfigWPFRadioButton)subTVI.Header;
                            Config subc = subRB.config;
                            if ((bool)subRB.IsEnabled && subc.Enabled)
                            {
                                subc.Checked = true;
                                subRB.IsChecked = true;
                                break;
                            }
                        }
                    }
                }
            }
            else if (cfg.configs.Count > 0 && !cfg.Checked)
            {
                foreach (Config c in cfg.configs)
                {
                    if (c.type.Equals("single") || c.type.Equals("single1") || c.type.Equals("multi"))
                    {
                        c.Checked = false;
                        if (c.configUIComponent is ConfigWPFCheckBox)
                        {
                            ConfigWPFCheckBox tempCB = (ConfigWPFCheckBox)c.configUIComponent;
                            tempCB.IsChecked = false;
                        }
                        else if (c.configUIComponent is ConfigWPFRadioButton)
                        {
                            ConfigWPFRadioButton tempCB = (ConfigWPFRadioButton)c.configUIComponent;
                            tempCB.IsChecked = false;
                        }
                    }
                }
            }
        }
        //when a dropdown legacy combobox is index changed
        void configControlDDALL_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ConfigWPFComboBox cb = (ConfigWPFComboBox)sender;
            if (!loadingConfig)
            {
                //propagate the check if required
                System.Windows.Controls.TreeViewItem tvi = (System.Windows.Controls.TreeViewItem)cb.Parent;
                ComboBoxItem cbi22 = (ComboBoxItem)cb.SelectedItem;
                if (tvi.Parent is System.Windows.Controls.TreeViewItem && cb.SelectedIndex != -1)
                {
                    System.Windows.Controls.TreeViewItem parenttvi = (System.Windows.Controls.TreeViewItem)tvi.Parent;
                    if (parenttvi.Header is ModWPFCheckBox)
                    {
                        ModWPFCheckBox c = (ModWPFCheckBox)parenttvi.Header;
                        c.IsChecked = true;
                    }
                    else if (parenttvi.Header is ConfigWPFCheckBox)
                    {
                        ConfigWPFCheckBox c = (ConfigWPFCheckBox)parenttvi.Header;
                        c.IsChecked = true;
                    }
                    else if (parenttvi.Header is ConfigWPFRadioButton)
                    {
                        ConfigWPFRadioButton c = (ConfigWPFRadioButton)parenttvi.Header;
                        c.IsChecked = true;
                    }
                }
            }
            //first check if this is init, meaning first time Enabled
            //but now this should never have to run
            //getting here means that an item is confirmed to be selected
            //itterate through the items, get each config, disable it
            //enable the selected one at the end
            foreach (ComboBoxItem cbi in cb.Items)
            {
                cbi.config.Checked = false;
            }
            ComboBoxItem cbi2 = (ComboBoxItem)cb.SelectedItem;
            cbi2.config.Checked = true;
            //set the new tooltip
            cb.ToolTip = cbi2.config.description.Equals("") ? NoDescriptionAvailable : cbi2.config.description;
        }
        //when a radiobutton of the legacy view mode is clicked
        void configControlRB_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (loadingConfig)
                return;
            //get all required cool stuff
            ConfigWPFRadioButton cb = (ConfigWPFRadioButton)sender;
            Mod m = cb.mod;
            Config cfg = cb.config;
            //the config treeview
            System.Windows.Controls.TreeViewItem tvi = (System.Windows.Controls.TreeViewItem)cb.Parent;
            //the mod treeview
            System.Windows.Controls.TreeViewItem item1 = (System.Windows.Controls.TreeViewItem)tvi.Parent;
            if ((bool)cb.IsEnabled && (bool)cb.IsChecked)
            {
                //uincheck all single and single1 mods
                foreach (System.Windows.Controls.TreeViewItem item in item1.Items)
                {
                    if (item.Header is ConfigWPFRadioButton)
                    {
                        ConfigWPFRadioButton rb = (ConfigWPFRadioButton)item.Header;
                        if ((bool)rb.IsEnabled && (bool)rb.IsChecked && (!rb.Equals(cb)))
                        {
                            rb.config.Checked = false;
                            rb.IsChecked = false;
                        }
                    }
                }
            }
            cfg.Checked = (bool)cb.IsChecked;
            //propagate the check if required
            if (tvi.Parent is System.Windows.Controls.TreeViewItem && cfg.Checked)
            {
                System.Windows.Controls.TreeViewItem parenttvi = (System.Windows.Controls.TreeViewItem)tvi.Parent;
                if (parenttvi.Header is ModWPFCheckBox)
                {
                    ModWPFCheckBox c = (ModWPFCheckBox)parenttvi.Header;
                    c.IsChecked = true;
                }
                else if (parenttvi.Header is ConfigWPFCheckBox)
                {
                    ConfigWPFCheckBox c = (ConfigWPFCheckBox)parenttvi.Header;
                    c.IsChecked = true;
                }
                else if (parenttvi.Header is ConfigWPFRadioButton)
                {
                    ConfigWPFRadioButton c = (ConfigWPFRadioButton)parenttvi.Header;
                    c.IsChecked = true;
                }
            }
            //process the subconfigs
            bool configSelected = false;
            int radioButtonCount = 0;
            if (cfg.configs.Count > 0 && cfg.Checked)
            {
                //determine if at least one radioButton is checked
                foreach (System.Windows.Controls.TreeViewItem subTVI in tvi.Items)
                {
                    if (subTVI.Header is ConfigWPFRadioButton)
                    {
                        radioButtonCount++;
                        ConfigWPFRadioButton subRB = (ConfigWPFRadioButton)subTVI.Header;
                        Config subc = subRB.config;
                        if ((bool)subRB.IsEnabled && (bool)subRB.IsChecked)
                        {
                            //getting here means cb is Enabled
                            subRB.IsEnabled = true;
                            //this needs to be changed
                            if (subc.Checked)
                                configSelected = true;
                        }
                    }
                }
                if (!configSelected && (bool)cb.IsChecked && (bool)cb.IsEnabled && radioButtonCount > 0)
                {
                    foreach (System.Windows.Controls.TreeViewItem subTVI in tvi.Items)
                    {
                        if (subTVI.Header is ConfigWPFRadioButton)
                        {
                            ConfigWPFRadioButton subRB = (ConfigWPFRadioButton)subTVI.Header;
                            Config subc = subRB.config;
                            if ((bool)subRB.IsEnabled && subc.Enabled)
                            {
                                subc.Checked = true;
                                subRB.IsChecked = true;
                                break;
                            }
                        }
                    }
                }
            }
            else if (cfg.configs.Count > 0 && !cfg.Checked)
            {
                foreach (Config c in cfg.configs)
                {
                    if (c.type.Equals("single") || c.type.Equals("single1") || c.type.Equals("multi"))
                    {
                        c.Checked = false;
                        if (c.configUIComponent is ConfigWPFCheckBox)
                        {
                            ConfigWPFCheckBox tempCB = (ConfigWPFCheckBox)c.configUIComponent;
                            tempCB.IsChecked = false;
                        }
                        else if (c.configUIComponent is ConfigWPFRadioButton)
                        {
                            ConfigWPFRadioButton tempCB = (ConfigWPFRadioButton)c.configUIComponent;
                            tempCB.IsChecked = false;
                        }
                    }
                }
            }
        }

        //adds a mod m to a tabpage t
        private void AddModDefaultView(Mod m, TabPage t, Category catagory)
        {
            if (!m.visible)
                return;
            int newPanelCount = t.Controls.Count + 1;
            //bool for keeping track if a radioButton config has been selected
            hasRadioButtonConfigSelected = false;
            modHasRadioButtons = false;
            //make the mod check box
            ModFormCheckBox modCheckBox = new ModFormCheckBox();
            //add the ToolTip description to the checkbox
            DescriptionToolTip.SetToolTip(modCheckBox, m.description.Equals("")? NoDescriptionAvailable : m.description);
            modCheckBox.AutoSize = true;
            modCheckBox.Location = new System.Drawing.Point(3, 3);
            modCheckBox.Size = new System.Drawing.Size(49, 15);
            modCheckBox.TabIndex = 1;
            modCheckBox.Text = Utils.ReplaceMacro(m);
            modCheckBox.Name = t.Name + "_" + m.name;
            modCheckBox.Font = Settings.AppFont;
            modCheckBox.catagory = catagory;
            modCheckBox.mod = m;
            m.tabIndex = t;
            m.modFormCheckBox = modCheckBox;
            completeModSearchList.Add(m);
            // completeModSearchList_New.Add(m);
            //the mod checksum logic
            if (!m.ZipFile.Equals(""))
            {
                string modDownloadPath = Path.Combine(Application.StartupPath, "RelHaxDownloads", m.ZipFile);
                if (firstLoad)
                {
                    string oldCRC2 = XMLUtils.GetMd5Hash(modDownloadPath);
                    //if the CRC's don't match and the mod actually has a zip file
                    if ((!m.CRC.Equals(oldCRC2)))
                    {
                        modCheckBox.Text = string.Format("{0} ({1})", modCheckBox.Text, Translations.getTranslatedString("updated"));
                        m.DownloadFlag = true;
                        if ((m.size > 0))
                            modCheckBox.Text = string.Format("{0} ({1})", modCheckBox.Text, Utils.SizeSuffix(m.size, 1, true));
                    }
                }
                else
                {
                    if (m.DownloadFlag)
                    {
                        modCheckBox.Text = string.Format("{0} ({1})", modCheckBox.Text, Translations.getTranslatedString("updated"));
                        if ((m.size > 0))
                            modCheckBox.Text = string.Format("{0} ({1})", modCheckBox.Text, Utils.SizeSuffix(m.size, 1, true));
                    }
                }
            }
            modCheckBox.UseVisualStyleBackColor = true;
            modCheckBox.Enabled = m.Enabled;
            modCheckBox.MouseDown += new MouseEventHandler(Generic_MouseDown);
            //in theory it should trigger the handler for checked
            //when initially made it should be false, if Enabled from
            //from user configs
            //make the main panel
            Panel mainPanel = new Panel();
            mainPanel.BorderStyle = Settings.DisableBorders ? BorderStyle.None : BorderStyle.FixedSingle;
            mainPanel.TabIndex = 0;
            mainPanel.AutoSize = true;
            mainPanel.AutoSizeMode = AutoSizeMode.GrowOnly;
            mainPanel.Size = new System.Drawing.Size(t.Size.Width - 25, 20);
            if (m.Enabled && m.Checked && !Settings.DisableColorChange)
                mainPanel.BackColor = Color.BlanchedAlmond;
            else
                mainPanel.BackColor = Settings.getBackColor();
            int panelCountYLocation = 70 * (newPanelCount - 1);
            //if this is not the first mod being added to the panel
            int panelYLocation = 6; //tab plus delimiter
            if (newPanelCount > 1)
            {
                //create a list of other controlls and put this one 6 pixels below the others
                foreach (Control c in t.Controls)
                {
                    panelYLocation += c.Size.Height;
                    panelYLocation += 6;
                }
                panelCountYLocation = (newPanelCount - 1) * (t.Controls[0].Size.Height);
                panelCountYLocation = panelCountYLocation + 5;
            }
            mainPanel.Location = new System.Drawing.Point(5, panelYLocation);
            //processes the subconfigs here
            mainPanel.Controls.Clear();
            //add to main panel
            mainPanel.Controls.Add(modCheckBox);
            if (m.configs.Count > 0)
                AddConfigsDefaultView(t, m, catagory, modCheckBox, mainPanel, true, m.configs, mainPanel);
            //add to tab
            t.Controls.Add(mainPanel);
            //add the event handler before changing the checked state so the event
            //event handler is #triggered
            modCheckBox.Checked = m.Checked;
            //disable the logic for now
            if (false && modHasRadioButtons && modCheckBox.Checked && !hasRadioButtonConfigSelected)
            {
                //getting here means that the user has saved the prefrence for a selected mandatory radiobutton config that has been disabled, so his selection of that mod needs to be disabled
                modCheckBox.Checked = false;
                m.Checked = false;
            }
            modCheckBox.CheckedChanged += new EventHandler(modCheckBox_CheckedChanged);
        }

        private void AddConfigsDefaultView(TabPage t, Mod m, Category catagory, ModFormCheckBox modCheckBox, Panel mainPanel, bool parentIsMod, List<Config> configs, Panel topPanal, Config parentConfig = null)
        {
            //make config panel
            Panel configPanel = new Panel();
            configPanel.Enabled = true;
            configPanel.BorderStyle = Settings.DisableBorders ? BorderStyle.None : BorderStyle.FixedSingle;
            configPanel.Location = new System.Drawing.Point(3, 10);
            configPanel.TabIndex = 2;
            configPanel.Size = new System.Drawing.Size(t.Size.Width - 35, 30);
            configPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowOnly;
            configPanel.AutoSize = true;
            if (parentIsMod)
            {
                if (m.Enabled && m.Checked && !Settings.DisableColorChange)
                    configPanel.BackColor = Color.BlanchedAlmond;
                else
                    configPanel.BackColor = Settings.getBackColor();
            }
            else
            {
                if (parentConfig.Enabled && parentConfig.Checked && !Settings.DisableColorChange)
                    configPanel.BackColor = Color.BlanchedAlmond;
                else
                    configPanel.BackColor = Settings.getBackColor();
            }
            int spacer = modCheckBox.Location.Y + modCheckBox.Size.Height + 5;
            switch (Settings.FontSizeforum)
            {
                case Settings.FontSize.Font100:
                    break;
                case Settings.FontSize.Font125:
                    spacer += 3;
                    break;
                case Settings.FontSize.Font175:
                    spacer += 6;
                    break;
                case Settings.FontSize.Font225:
                    spacer += 9;
                    break;
                case Settings.FontSize.Font275:
                    spacer += 12;
                    break;
            }
            if (parentIsMod)
            {
                configPanel.Location = new Point(configPanel.Location.X + 10, spacer);
            }
            else
            {
                configPanel.Location = new Point(configPanel.Location.X + 10, getYLocation(mainPanel.Controls));
            }
            mainPanel.Controls.Add(configPanel);
            //create the comboBox outside of the loop
            //later add it if the items count is above 0
            ConfigFormComboBox configControlDD = new ConfigFormComboBox();
            configControlDD.AutoSize = true;
            configControlDD.Location = new System.Drawing.Point(0, 0);
            configControlDD.Size = new System.Drawing.Size(225, 15);
            configControlDD.TabIndex = 1;
            configControlDD.TabStop = true;
            configControlDD.Enabled = false;
            configControlDD.SelectedIndexChanged += new EventHandler(configControlDD_SelectedIndexChanged);
            //https://stackoverflow.com/questions/1882993/c-sharp-how-do-i-prevent-mousewheel-scrolling-in-my-combobox
            configControlDD.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            configControlDD.MouseDown += new MouseEventHandler(Generic_MouseDown);
            configControlDD.Name = t.Name + "_" + m.name + "_DropDown";
            configControlDD.DropDownStyle = ComboBoxStyle.DropDownList;
            ConfigFormComboBox configControlDD2 = new ConfigFormComboBox();
            configControlDD2.AutoSize = true;
            configControlDD2.Location = new System.Drawing.Point(0, 0);
            configControlDD2.Size = new System.Drawing.Size(225, 15);
            configControlDD2.TabIndex = 1;
            configControlDD2.TabStop = true;
            configControlDD2.Enabled = false;
            configControlDD2.SelectedIndexChanged += new EventHandler(configControlDD_SelectedIndexChanged);
            configControlDD2.MouseWheel += (o, e) => ((HandledMouseEventArgs)e).Handled = true;
            configControlDD2.MouseDown += new MouseEventHandler(Generic_MouseDown);
            configControlDD2.Name = t.Name + "_" + m.name + "_DropDown2";
            configControlDD2.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (Config con in configs)
            {
                if (!con.visible)
                    continue;
                ConfigFormComboBox configControlDDALL = null;
                con.parentMod = m;
                if (parentIsMod)
                {
                    // completeModSearchList_New.Add(con);
                    con.parent = m;
                    completeModSearchList.Add(con);
                }
                else
                {
                    con.parent = parentConfig;
                }
                if (con.type.Equals("single") || con.type.Equals("single1"))
                {
                    modHasRadioButtons = true;
                    //make default radioButton
                    ConfigFormRadioButton configControlRB = new ConfigFormRadioButton();
                    //add the ToolTip description to the checkbox
                    DescriptionToolTip.SetToolTip(configControlRB, con.description.Equals("")? NoDescriptionAvailable : con.description);
                    configControlRB.AutoSize = true;
                    configControlRB.Location = new Point(6, getYLocation(configPanel.Controls));
                    configControlRB.Size = new System.Drawing.Size(150, 15);
                    configControlRB.TabIndex = 1;
                    configControlRB.TabStop = true;
                    configControlRB.Font = Settings.AppFont;
                    configControlRB.catagory = catagory;
                    configControlRB.mod = m;
                    configControlRB.config = con;
                    con.configUIComponent = configControlRB;
                    //logic for determining if it can be Enabled
                    configControlRB.Enabled = false;
                    configControlRB.Checked = false;
                    //get all levels up to the mod, then deal with the mod
                    bool canBeEnabled = true;
                    //check all parent configs, if any
                    if (con.parent is Config)
                    {
                        Config parentConfig2 = (Config)con.parent;
                        while (parentConfig2 is Config)
                        {
                            if (!parentConfig2.Enabled)
                                canBeEnabled = false;
                            if (parentConfig2.parent is Mod)
                                break;
                            parentConfig2 = (Config)parentConfig2.parent;
                        }
                    }
                    //check the parent mod
                    if (!con.parentMod.Enabled)
                        canBeEnabled = false;
                    //check itself (before it reks itself)
                    if (!con.Enabled)
                        canBeEnabled = false;
                    if (canBeEnabled)
                        configControlRB.Enabled = true;
                    if (configControlRB.Enabled)
                        if (con.Checked)
                            configControlRB.Checked = true;
                    //add handlers
                    configControlRB.CheckedChanged += new EventHandler(configControlRB_CheckedChanged);
                    configControlRB.MouseDown += new MouseEventHandler(Generic_MouseDown);
                    configControlRB.Name = t.Name + "_" + m.name + "_" + con.name;
                    //run checksum logic
                    configControlRB.Text = Utils.ReplaceMacro(con);
                    if (!con.ZipFile.Equals(""))
                    {
                        if (firstLoad)
                        {
                            string oldCRC = XMLUtils.GetMd5Hash(Path.Combine(Application.StartupPath, "RelHaxDownloads", con.ZipFile));
                            if ((!oldCRC.Equals(con.CRC)))
                            {
                                configControlRB.Text = string.Format("{0} ({1})", configControlRB.Text, Translations.getTranslatedString("updated"));
                                con.DownloadFlag = true;
                                if (con.size > 0)
                                    configControlRB.Text = string.Format("{0} ({1})", configControlRB.Text, Utils.SizeSuffix(con.size, 1, true));
                            }
                        }
                        else
                        {
                            if (con.DownloadFlag)
                            {
                                configControlRB.Text = string.Format("{0} ({1})", configControlRB.Text, Translations.getTranslatedString("updated"));
                                if (con.size > 0)
                                    configControlRB.Text = string.Format("{0} ({1})", configControlRB.Text, Utils.SizeSuffix(con.size, 1, true));
                            }
                        }
                    }
                    //add the config to the form
                    configPanel.Controls.Add(configControlRB);
                    //process the subconfigs
                    if (con.configs.Count > 0)
                        AddConfigsDefaultView(t, m, catagory, modCheckBox, configPanel, false, con.configs, topPanal, con);
                }
                else if (con.type.Equals("single_dropdown") || con.type.Equals("single_dropdown1") || con.type.Equals("single_dropdown2"))
                {
                    //set the all one to the version is actually is
                    if (con.type.Equals("single_dropdown") || con.type.Equals("single_dropdown1"))
                    {
                        configControlDDALL = configControlDD;
                        con.configUIComponent = configControlDD;
                    }
                    else if (con.type.Equals("single_dropdown2"))
                    {
                        configControlDDALL = configControlDD2;
                        con.configUIComponent = configControlDD2;
                    }
                    //make a dropDown selection box
                    if (configControlDDALL.Location.X == 0 && configControlDDALL.Location.Y == 0)
                    {
                        //init the box, including adding the label
                        configControlDDALL.Location = new System.Drawing.Point(6, getYLocation(configPanel.Controls));
                        configPanel.Controls.Add(configControlDDALL);
                    }
                    ComboBoxItem cbi = null;
                    string toAdd = Utils.ReplaceMacro(con);
                    //run the checksum locics
                    if (firstLoad)
                    {
                        string oldCRC = XMLUtils.GetMd5Hash(Path.Combine(Application.StartupPath, "RelHaxDownloads", con.ZipFile));
                        if ((!con.CRC.Equals("")) && (!oldCRC.Equals(con.CRC)))
                        {
                            con.DownloadFlag = true;
                            toAdd = string.Format("{0} ({1})", toAdd, Translations.getTranslatedString("updated"));
                            if (con.size > 0)
                                toAdd = string.Format("{0} ({1})", toAdd, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    else
                    {
                        if (con.DownloadFlag)
                        {
                            toAdd = string.Format("{0} ({1})", toAdd, Translations.getTranslatedString("updated"));
                            if (con.size > 0)
                                toAdd = string.Format("{0} ({1})", toAdd, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    //add it
                    if (con.Enabled)
                    {
                        cbi = new ComboBoxItem(con, toAdd);
                        configControlDDALL.Items.Add(cbi);
                        if (con.Checked)
                        {
                            configControlDDALL.SelectedItem = cbi;
                            configControlDDALL.Enabled = true;
                            //set the tooltip to the checked option
                            DescriptionToolTip.SetToolTip(configControlDDALL, con.description.Equals("")? NoDescriptionAvailable : con.description);
                        }
                    }
                    if (configControlDDALL.Items.Count > 0)
                    {
                        configControlDDALL.Enabled = true;
                        if (configControlDDALL.SelectedIndex == -1)
                        {
                            configControlDDALL.SelectedIndex = 0;
                            //set the tooltip since nothing has been selected
                            ComboBoxItem cbiTT = (ComboBoxItem)configControlDDALL.Items[0];
                            DescriptionToolTip.SetToolTip(configControlDDALL, cbiTT.config.description.Equals("")? NoDescriptionAvailable : cbiTT.config.description);
                        }
                    }
                }
                else if (con.type.Equals("multi"))
                {
                    //make a checkBox
                    ConfigFormCheckBox configControlCB = new ConfigFormCheckBox();
                    //add the ToolTip description to the checkbox
                    DescriptionToolTip.SetToolTip(configControlCB, con.description.Equals("")? NoDescriptionAvailable : con.description);
                    configControlCB.AutoSize = true;
                    configControlCB.Location = new Point(6, getYLocation(configPanel.Controls));
                    configControlCB.Size = new System.Drawing.Size(150, 15);
                    configControlCB.TabIndex = 1;
                    configControlCB.TabStop = true;
                    configControlCB.Font = Settings.AppFont;
                    configControlCB.catagory = catagory;
                    configControlCB.mod = m;
                    configControlCB.config = con;
                    con.configUIComponent = configControlCB;
                    //logic for determining if it can be Enabled
                    configControlCB.Enabled = false;
                    configControlCB.Checked = false;
                    //get all levels up to the mod, then deal with the mod
                    bool canBeEnabled = true;
                    //check all parent configs, if any
                    if (con.parent is Config)
                    {
                        Config parentConfig2 = (Config)con.parent;
                        while (parentConfig2 is Config)
                        {
                            if (!parentConfig2.Enabled)
                                canBeEnabled = false;
                            if (parentConfig2.parent is Mod)
                                break;
                            parentConfig2 = (Config)parentConfig2.parent;
                        }
                    }
                    //check the parent mod
                    if (!con.parentMod.Enabled)
                        canBeEnabled = false;
                    //check itself (before it reks itself)
                    if (!con.Enabled)
                        canBeEnabled = false;
                    if (canBeEnabled)
                        configControlCB.Enabled = true;
                    if (configControlCB.Enabled)
                        if (con.Checked)
                            configControlCB.Checked = true;
                    //add handlers
                    configControlCB.CheckedChanged += new EventHandler(configControlCB_CheckedChanged);
                    configControlCB.MouseDown += new MouseEventHandler(Generic_MouseDown);
                    configControlCB.Name = t.Name + "_" + m.name + "_" + con.name;
                    //checksum logic
                    configControlCB.Text = Utils.ReplaceMacro(con);
                    if (firstLoad)
                    {
                        string oldCRC = XMLUtils.GetMd5Hash(Path.Combine(Application.StartupPath, "RelHaxDownloads", con.ZipFile));
                        if ((!con.CRC.Equals("")) && (!oldCRC.Equals(con.CRC)))
                        {
                            con.DownloadFlag = true;
                            configControlCB.Text = string.Format("{0} ({1})", configControlCB.Text, Translations.getTranslatedString("updated"));
                            if (con.size > 0)
                                configControlCB.Text = string.Format("{0} ({1})", configControlCB.Text, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    else
                    {
                        if (con.DownloadFlag)
                        {
                            configControlCB.Text = string.Format("{0} ({1})", configControlCB.Text, Translations.getTranslatedString("updated"));
                            if (con.size > 0)
                                configControlCB.Text = string.Format("{0} ({1})", configControlCB.Text, Utils.SizeSuffix(con.size, 1, true));
                        }
                    }
                    //add config to the form
                    configPanel.Controls.Add(configControlCB);
                    //process subconfigs
                    if (con.configs.Count > 0)
                        AddConfigsDefaultView(t, m, catagory, modCheckBox, configPanel, false, con.configs, topPanal, con);
                }
                else
                {
                    Utils.AppendToLog(string.Format("WARNING: Unknown config type for {0}: {1}", con.name, con.type));
                }
            }
        }
        
        //method for finding the location of which to put a control
        private int getYLocation(System.Windows.Forms.Control.ControlCollection ctrl)
        {
            int y = 0;
            //the first 5 pixels to give it room
            y += 5;
            //only look for the dropDown menu options or checkboxes
            foreach (Control c in ctrl)
            {
                if ((c is ConfigFormCheckBox) || (c is ConfigFormComboBox) || (c is Panel) || (c is ConfigFormRadioButton) || (c is ModFormCheckBox))
                {
                    y += c.Size.Height;
                    //spacing
                    y += 2;
                }
                if (c is ConfigFormComboBox)
                {
                    switch (Settings.FontSizeforum)
                    {
                        case Settings.FontSize.Font100:
                            break;
                        case Settings.FontSize.Font125:
                            y += 3;
                            break;
                        case Settings.FontSize.Font175:
                            y += 6;
                            break;
                        case Settings.FontSize.Font225:
                            y += 9;
                            break;
                        case Settings.FontSize.Font275:
                            y += 12;
                            break;
                        case Settings.FontSize.DPI100:
                            break;
                        case Settings.FontSize.DPI125:
                            y += 3;
                            break;
                        case Settings.FontSize.DPI175:
                            y += 6;
                            break;
                        case Settings.FontSize.DPI225:
                            y += 9;
                            break;
                        case Settings.FontSize.DPI275:
                            y += 12;
                            break;
                    }
                }
            }
            return y;
        }
        
        //handler for when a mod checkbox is changed
        void modCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (loadingConfig)
                return;
            ModFormCheckBox cbUser = (ModFormCheckBox)sender;
            if (cbUser.Parent is TabPage)
            {
                //user mod code
                TabPage t = (TabPage)cbUser.Parent;
                if (t.Text.Equals("User Mods"))
                {
                    //verified, this is a check from the user checkboxes
                    cbUser.mod.Checked = cbUser.Checked;
                    return;
                }
            }
            //the mod info handler should only be concerned with checking for enabling componets

            //get all required info for this checkbox change
            ModFormCheckBox cb = (ModFormCheckBox)sender;
            //this parent could nwo be the radioButton config selection panel or the config panel
            Panel modPanel = (Panel)cb.Parent;
            TabPage modTab = (TabPage)modPanel.Parent;
            Mod m = cb.mod;
            Category cat = cb.catagory;

            //just a check
            if (!m.visible)
                return;

            //check to see if the mod is part of a single selection only catagory
            //if it is uncheck the other mods first, then deal with mod loop selection
            if (cat.selectionType.Equals("single") && cb.Checked)
            {
                //check if any other mods in this catagory are already checked
                bool anyModsChecked = false;
                foreach (Mod mm in cat.mods)
                {
                    if (mm.Checked)
                    {
                        anyModsChecked = true;
                        mm.Checked = false;
                    }
                }
                if (anyModsChecked)
                {
                    //not safe to check the mod
                    //uncheck the other mod first
                    //each checkbox uncheck it
                    foreach (var cc in modTab.Controls)
                    {
                        if (cc is Panel)
                        {
                            //it's a mod panel
                            Panel pp = (Panel)cc;
                            foreach (var ccc in pp.Controls)
                            {
                                if (ccc is ModFormCheckBox)
                                {
                                    ModFormCheckBox cbb = (ModFormCheckBox)ccc;
                                    //disable the other mods
                                    cbb.Checked = false;
                                    cbb.mod.Checked = false;
                                }
                            }
                        }
                    }
                    //now it's safe to check the mods
                    //remove the handler for a sec to prevent stack overflow
                    cb.CheckedChanged -= modCheckBox_CheckedChanged;
                    cb.Checked = true;
                    cb.mod.Checked = true;
                    cb.CheckedChanged += modCheckBox_CheckedChanged;
                }

            }
            //toggle the mod in memory, Enabled or disabled
            m.Checked = cb.Checked;
            //toggle the mod panel color
            if (cb.Checked && !Settings.DisableColorChange)
            {
                modPanel.BackColor = Color.BlanchedAlmond;
            }
            else
            {
                modPanel.BackColor = Settings.getBackColor();
            }
            //this deals with enabling the componets and triggering the handlers
            if (m.configs.Count == 0)
                return;
            //the first one is always the mod checkbox
            //the second one is always the config panel
            Panel configPanel = (Panel)modPanel.Controls[1];
            if (cb.Checked && !Settings.DisableColorChange)
            {
                configPanel.BackColor = Color.BlanchedAlmond;
            }
            else
            {
                configPanel.BackColor = Settings.getBackColor();
            }
            if (m.Checked)
            {
                //mod checked, check at least one single1, one single_dropdown1, one single_dropdown2
                //checking for single/single1 configs
                bool configSelected = false;
                foreach (Config con in m.configs)
                {
                    if ((!con.visible) || (!con.Enabled))
                        continue;
                    if ((con.type.Equals("single")) || (con.type.Equals("single1")))
                    {
                        if (con.Checked)
                            configSelected = true;
                    }
                }
                if (!configSelected)
                {
                    foreach (Config con in m.configs)
                    {
                        if ((!con.visible) || (!con.Enabled))
                            continue;
                        if ((con.type.Equals("single")) || (con.type.Equals("single1")))
                        {
                            con.Checked = true;
                            ConfigFormRadioButton cfrb = (ConfigFormRadioButton)con.configUIComponent;
                            cfrb.Checked = true;
                            break;
                        }
                    }
                }
                //checking for single_dropdown/single_dropdown1 configs
                configSelected = false;
                foreach (Config con in m.configs)
                {
                    if ((!con.visible) || (!con.Enabled))
                        continue;
                    if ((con.type.Equals("single_dropdown")) || (con.type.Equals("single_dropdown1")))
                    {
                        if (con.Checked)
                            configSelected = true;
                    }
                }
                if (!configSelected)
                {
                    foreach (Config con in m.configs)
                    {
                        if ((!con.visible) || (!con.Enabled))
                            continue;
                        if ((con.type.Equals("single_dropdown")) || (con.type.Equals("single_dropdown1")))
                        {
                            con.Checked = true;
                            ConfigFormComboBox cfcb = (ConfigFormComboBox)con.configUIComponent;
                            bool breakOut = false;
                            foreach (ComboBoxItem cbi in cfcb.Items)
                            {
                                if (cbi.config.name.Equals(con.name))
                                {
                                    cfcb.SelectedIndexChanged -= configControlDD_SelectedIndexChanged;
                                    cfcb.SelectedItem = cbi;
                                    cfcb.SelectedIndexChanged += configControlDD_SelectedIndexChanged;
                                    breakOut = true;
                                    break;
                                }
                            }
                            if (breakOut)
                                break;
                        }
                    }
                }
                //checking for single_dropdown2 configs
                configSelected = false;
                foreach (Config con in m.configs)
                {
                    if ((!con.visible) || (!con.Enabled) || (!con.type.Equals("single_dropdown2")))
                        continue;
                    if (con.Checked)
                        configSelected = true;
                }
                if (!configSelected)
                {
                    foreach (Config con in m.configs)
                    {
                        if ((!con.visible) || (!con.Enabled) || (!con.type.Equals("single_dropdown2")))
                            continue;
                        con.Checked = true;
                        ConfigFormComboBox cfcb = (ConfigFormComboBox)con.configUIComponent;
                        bool breakOut = false;
                        foreach (ComboBoxItem cbi in cfcb.Items)
                        {
                            if (cbi.config.name.Equals(con.name))
                            {
                                cfcb.SelectedIndexChanged -= configControlDD_SelectedIndexChanged;
                                cfcb.SelectedItem = cbi;
                                cfcb.SelectedIndexChanged += configControlDD_SelectedIndexChanged;
                                breakOut = true;
                                break;
                            }
                        }
                        if (breakOut)
                            break;
                    }
                }
            }
            else
            {
                //mod not checked, uncheck all the configs
                foreach (Config cfg in m.configs)
                {
                    if (cfg.Enabled)
                    {
                        cfg.Checked = false;
                        if (cfg.configUIComponent is ConfigFormCheckBox)
                        {
                            ConfigFormCheckBox cfcb = (ConfigFormCheckBox)cfg.configUIComponent;
                            cfcb.Checked = false;
                        }
                        else if (cfg.configUIComponent is ConfigFormRadioButton)
                        {
                            ConfigFormRadioButton cfrb = (ConfigFormRadioButton)cfg.configUIComponent;
                            cfrb.Checked = false;
                        }
                        else if (cfg.configUIComponent is ConfigFormComboBox)
                        {
                            ConfigFormComboBox cfcb = (ConfigFormComboBox)cfg.configUIComponent;
                            cfcb.SelectedIndexChanged -= configControlDD_SelectedIndexChanged;
                            cfcb.SelectedIndex = 0;
                            cfcb.SelectedIndexChanged += configControlDD_SelectedIndexChanged;
                        }
                    }
                }
            }
        }

        //handler for when the config checkbox is checked or unchecked
        void configControlCB_CheckedChanged(object sender, EventArgs e)
        {
            if (loadingConfig)
                return;
            //checkboxes don't need to be unselected
            ConfigFormCheckBox cb = (ConfigFormCheckBox)sender;
            Mod m = cb.mod;
            Config cfg = cb.config;
            Category cat = cb.catagory;
            Panel configPanel = (Panel)cb.Parent;
            //checkbox is Enabled, toggle checked and checked
            cfg.Checked = cb.Checked;
            //propagate the check back up if required
            if (cfg.Checked)
            {
                SelectableDatabasePackage obj = cfg.parent;
                if (obj is Mod)
                {
                    Mod parentM = (Mod)obj;
                    if (parentM.modFormCheckBox is ModFormCheckBox)
                    {
                        ModFormCheckBox tempCB = (ModFormCheckBox)parentM.modFormCheckBox;
                        if (!tempCB.Checked)
                            tempCB.Checked = true;
                    }
                }
                else if (obj is Config)
                {
                    Config parentC = (Config)obj;
                    parentC.Checked = true;
                    if (parentC.configUIComponent is ConfigFormCheckBox)
                    {
                        ConfigFormCheckBox parentCB = (ConfigFormCheckBox)parentC.configUIComponent;
                        if (!parentCB.Checked)
                            parentCB.Checked = true;
                    }
                    else if (parentC.configUIComponent is ConfigFormRadioButton)
                    {
                        ConfigFormRadioButton parentRB = (ConfigFormRadioButton)parentC.configUIComponent;
                        if (!parentRB.Checked)
                            parentRB.Checked = true;
                    }
                }
            }
            //process any subconfigs
            bool configSelected = false;
            int radioButtonCount = 0;
            if (cfg.configs.Count > 0 && cb.Checked)
            {
                foreach (Config c in cfg.configs)
                {
                    if (c.type.Equals("single") || c.type.Equals("single1"))
                    {
                        radioButtonCount++;
                        if (c.Checked)
                            configSelected = true;
                    }
                }
                if (!configSelected && radioButtonCount > 0)
                {
                    //select the first one and leave
                    foreach (Config c in cfg.configs)
                    {
                        if ((c.type.Equals("single") || c.type.Equals("single1")) && c.Enabled)
                        {
                            c.Checked = true;
                            ConfigFormRadioButton subRB = (ConfigFormRadioButton)c.configUIComponent;
                            subRB.Checked = true;
                            break;
                        }
                    }
                }
            }
            else if (cfg.configs.Count > 0 && !cb.Checked)
            {
                foreach (Config c in cfg.configs)
                {
                    if (c.type.Equals("single") || c.type.Equals("single1") || c.type.Equals("multi"))
                    {
                        c.Checked = false;
                        if (c.configUIComponent is ConfigFormCheckBox)
                        {
                            ConfigFormCheckBox tempCB = (ConfigFormCheckBox)c.configUIComponent;
                            tempCB.Checked = false;
                        }
                        else if (c.configUIComponent is ConfigFormRadioButton)
                        {
                            ConfigFormRadioButton tempCB = (ConfigFormRadioButton)c.configUIComponent;
                            tempCB.Checked = false;
                        }
                    }
                }
            }
            //trigger the panel color change
            if (cb.Checked && !Settings.DisableColorChange)
                configPanel.BackColor = Color.BlanchedAlmond;
            else
                configPanel.BackColor = Settings.getBackColor();
        }
        //handler for when a config selection is made from the drop down list
        void configControlDD_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loadingConfig)
                return;
            //uncheck all other dorp down configs
            ConfigFormComboBox cb = (ConfigFormComboBox)sender;
            //due to recursion, save the cbi up here
            ComboBoxItem cbi2 = (ComboBoxItem)cb.SelectedItem;
            //propagate the check back up if required
            if (cb.SelectedIndex != -1)
            {
                ComboBoxItem cbi22 = (ComboBoxItem)cb.SelectedItem;
                SelectableDatabasePackage obj = cbi22.config.parent;
                if (obj is Mod)
                {
                    Mod parentM = (Mod)obj;
                    if (parentM.modFormCheckBox is ModFormCheckBox)
                    {
                        ModFormCheckBox tempCB = (ModFormCheckBox)parentM.modFormCheckBox;
                        //adding the if statement prevents it from running again when it's not needed to
                        if (!tempCB.Checked)
                            tempCB.Checked = true;
                    }
                }
                else if (obj is Config)
                {
                    Config parentC = (Config)obj;
                    parentC.Checked = true;
                    if (parentC.configUIComponent is ConfigFormCheckBox)
                    {
                        ConfigFormCheckBox parentCB = (ConfigFormCheckBox)parentC.configUIComponent;
                        if (!parentCB.Checked)
                            parentCB.Checked = true;
                    }
                    else if (parentC.configUIComponent is ConfigFormRadioButton)
                    {
                        ConfigFormRadioButton parentRB = (ConfigFormRadioButton)parentC.configUIComponent;
                        if (!parentRB.Checked)
                            parentRB.Checked = true;
                    }
                }
            }
            //itterate through the items, get each config, disable it
            //unless it's the same name as the selectedItem
            foreach (ComboBoxItem cbi in cb.Items)
            {
                cbi.config.Checked = false;
            }
            //ComboBoxItem cbi2 = (ComboBoxItem)cb.SelectedItem;
            cb.SelectedItem = cbi2;
            cbi2.config.Checked = true;
            DescriptionToolTip.SetToolTip(cb, cbi2.config.description.Equals("")? NoDescriptionAvailable : cbi2.config.description);
            Panel configPanel = (Panel)cb.Parent;
            if (!Settings.DisableColorChange)
                configPanel.BackColor = Color.BlanchedAlmond;
        }
        //handler for when a config radioButton is pressed
        void configControlRB_CheckedChanged(object sender, EventArgs e)
        {
            if (loadingConfig)
                return;
            //get all required cool stuff
            ConfigFormRadioButton rb = (ConfigFormRadioButton)sender;
            Panel configPanel = (Panel)rb.Parent;
            Mod m = rb.mod;
            Config cfg = rb.config;
            Category cat = rb.catagory;
            cfg.Checked = rb.Checked;
            //propagate the check back up if required
            if (cfg.Checked)
            {
                SelectableDatabasePackage obj = cfg.parent;
                if (obj is Mod)
                {
                    Mod parentM = (Mod)obj;
                    if (parentM.modFormCheckBox is ModFormCheckBox)
                    {
                        ModFormCheckBox tempCB = (ModFormCheckBox)parentM.modFormCheckBox;
                        if (!tempCB.Checked)
                            tempCB.Checked = true;
                    }
                }
                else if (obj is Config)
                {
                    Config parentC = (Config)obj;
                    parentC.Checked = true;
                    if (parentC.configUIComponent is ConfigFormCheckBox)
                    {
                        ConfigFormCheckBox parentCB = (ConfigFormCheckBox)parentC.configUIComponent;
                        if (!parentCB.Checked)
                            parentCB.Checked = true;
                    }
                    else if (parentC.configUIComponent is ConfigFormRadioButton)
                    {
                        ConfigFormRadioButton parentRB = (ConfigFormRadioButton)parentC.configUIComponent;
                        if (!parentRB.Checked)
                            parentRB.Checked = true;
                    }
                }
            }
            //propagate the change back down if required
            bool configSelected = false;
            int radioButtonCount = 0;
            if (cfg.configs.Count > 0 && rb.Checked)
            {
                //configs present and the radio button is checked
                //singles - at lease one must be selected
                foreach (Config c in cfg.configs)
                {
                    if (c.type.Equals("single") || c.type.Equals("single1"))
                    {
                        radioButtonCount++;
                        if (c.Checked)
                            configSelected = true;
                    }
                }
                if (!configSelected && radioButtonCount > 0)
                {
                    //select the first one and leave
                    foreach (Config c in cfg.configs)
                    {
                        if ((c.type.Equals("single") || c.type.Equals("single1")) && c.Enabled)
                        {
                            c.Checked = true;
                            ConfigFormRadioButton subRB = (ConfigFormRadioButton)c.configUIComponent;
                            subRB.Checked = true;
                            break;
                        }
                    }
                }
            }
            else if (cfg.configs.Count > 0 && !rb.Checked)
            {
                //configs present and the radio button is not checked
                //singles - uncheck all of them
                foreach (Config c in cfg.configs)
                {
                    if (c.type.Equals("single") || c.type.Equals("single1") || c.type.Equals("multi"))
                    {
                        c.Checked = false;
                        if (c.configUIComponent is ConfigFormCheckBox)
                        {
                            ConfigFormCheckBox tempCB = (ConfigFormCheckBox)c.configUIComponent;
                            tempCB.Checked = false;
                        }
                        else if (c.configUIComponent is ConfigFormRadioButton)
                        {
                            ConfigFormRadioButton tempCB = (ConfigFormRadioButton)c.configUIComponent;
                            tempCB.Checked = false;
                        }
                    }
                }
            }
            //trigger the panel color change
            if (rb.Checked && !Settings.DisableColorChange)
                configPanel.BackColor = Color.BlanchedAlmond;
            else
                configPanel.BackColor = Settings.getBackColor();
        }
        //generic hander for when any mouse button is clicked for MouseDown Events
        void Generic_MouseDown(object sender, EventArgs e)
        {
            //we only care about the right mouse click tho
            if (e is MouseEventArgs)
            {
                MouseEventArgs ee = (MouseEventArgs)e;
                if (ee.Button != MouseButtons.Right)
                    return;
            }
            else if (e is System.Windows.Input.MouseButtonEventArgs)
            {
                System.Windows.Input.MouseButtonEventArgs ee = (System.Windows.Input.MouseButtonEventArgs)e;
                if (ee.RightButton != System.Windows.Input.MouseButtonState.Pressed)
                {
                    return;
                }
            }
            if (sender is UIComponent)
            {
                UIComponent UIC = (UIComponent)sender;
                SelectableDatabasePackage DBO = null;
                //check if comboBox before mod or config
                //check config before checking mod
                if (sender is ConfigFormComboBox)
                {
                    ConfigFormComboBox cfcb = (ConfigFormComboBox)sender;
                    ComboBoxItem cbi = (ComboBoxItem)cfcb.SelectedItem;
                    DBO = cbi.config;
                }
                else if (sender is ConfigWPFComboBox)
                {
                    ConfigWPFComboBox cwpfcb = (ConfigWPFComboBox)sender;
                    ComboBoxItem cbi = (ComboBoxItem)cwpfcb.SelectedItem;
                    DBO = cbi.config;
                }
                else if (UIC.config != null)
                {
                    DBO = UIC.config;
                }
                else if (UIC.mod != null)
                {
                    DBO = UIC.mod;
                }
                if (DBO != null)
                {
                    if (DBO.devURL == null)
                        DBO.devURL = "";
                    if (p != null)
                    {
                        p.Close();
                        p.Dispose();
                        p = null;
                        GC.Collect();
                    }
                    p = new Preview(DBO.name, DBO.pictureList, DBO.description, DBO.updateComment, DBO.devURL);
                    p.Show();
                }
            }
        }

        //handler to set the cancel bool to false
        private void continueButton_Click(object sender, EventArgs e)
        {
            cancel = false;
            //save the last config if told to do so
            if (Settings.SaveLastConfig)
            {
                XMLUtils.SaveConfig(false, null, parsedCatagoryList, userMods);
            }
            this.Close();
        }
        //handler for when the cancal button is clicked
        private void cancelButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        //handler for when the "load config" button is pressed
        private void loadConfigButton_Click(object sender, EventArgs e)
        {
            loadMode = loadConfigMode.fromButton;
            this.parseLoadConfig();
        }
        //handler for when the "save config" button is pressed
        private void saveConfigButton_Click(object sender, EventArgs e)
        {
            XMLUtils.SaveConfig(true, null, parsedCatagoryList, userMods);
        }
        //handler for when the close button is pressed
        private void ModSelectionList_FormClosing(object sender, FormClosingEventArgs e)
        {
            //save the size of this window for later.
            Settings.ModSelectionHeight = this.Size.Height;
            Settings.ModSelectionWidth = this.Size.Width;
            if (taskBarHidden)
                Settings.SetTaskbarState(Settings.AppBarStates.AutoHide);
            //save wether the window was in fullscreen mods before closing
            if (this.WindowState == FormWindowState.Maximized)
                Settings.ModSelectionFullscreen = true;
            else
                Settings.ModSelectionFullscreen = false;
            //close the preview window if it is open
            if (p != null)
            {
                p.Close();
                p.Dispose();
                p = null;
            }
        }

        private void clearSelectionsButton_Click(object sender, EventArgs e)
        {
            //not actually *loading* a config, but want to disable the handlers anyways
            loadingConfig = true;
            Utils.AppendToLog("clearSelectionsButton pressed, clearing selections");
            Utils.ClearSelectionMemory(parsedCatagoryList, userMods);
            //dispose of not needed stuff and reload the UI

            loadingConfig = false;
            MessageBox.Show(Translations.getTranslatedString("selectionsCleared"));
            ModSelectionList_SizeChanged(null, null);
        }

        private void parseLoadConfig()
        {
            loadingConfig = true;
            OpenFileDialog loadLocation = new OpenFileDialog();
            string filePath = "";
            using (SelectionViewer sv = new SelectionViewer(this.Location.X + 100, this.Location.Y + 100, Settings.ModInfoDatFile))
            {
                if (loadMode == loadConfigMode.fromAutoInstall)
                {
                    filePath = Path.Combine(Application.StartupPath, "RelHaxUserConfigs", Program.configName);
                    if (!File.Exists(filePath))
                    {
                        Utils.AppendToLog(string.Format("ERROR: {0} not found, not loading configs", filePath));
                        MessageBox.Show(Translations.getTranslatedString("configLoadFailed"), Translations.getTranslatedString("critical"), MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        return;
                    }
                }
                else if (loadMode == loadConfigMode.fromSaveLastConfig)
                {
                    filePath = Path.Combine(Application.StartupPath, "RelHaxUserConfigs", "lastInstalledConfig.xml");
                    if (!File.Exists(filePath))
                    {
                        Utils.AppendToLog(string.Format("ERROR: {0} not found, not loading configs", filePath));
                        return;
                    }
                }
                else
                {
                    DialogResult res = sv.ShowDialog();
                    if (res == DialogResult.OK)
                    {
                        filePath = sv.SelectedXML;
                    }
                    else
                    { return; }
                    if (filePath.Equals("localFile"))
                    {
                        loadLocation.AddExtension = true;
                        loadLocation.DefaultExt = ".xml";
                        loadLocation.Filter = "*.xml|*.xml";
                        loadLocation.InitialDirectory = Path.Combine(Application.StartupPath, "RelHaxUserConfigs");
                        loadLocation.Title = Translations.getTranslatedString("selectConfigFile");
                        if (loadLocation.ShowDialog().Equals(DialogResult.Cancel))
                        {
                            //quit
                            return;
                        }
                        filePath = loadLocation.FileName;
                    }
                }
            }
            XMLUtils.LoadConfig(loadMode == loadConfigMode.fromButton, filePath, parsedCatagoryList, userMods);
            if (loadMode == loadConfigMode.fromButton || loadMode == loadConfigMode.fromAutoInstall)
            {
                if (loadMode == loadConfigMode.fromButton) MessageBox.Show(Translations.getTranslatedString("prefrencesSet"), Translations.getTranslatedString("information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                loadingConfig = false;
                ModSelectionList_SizeChanged(null, null);
            }
        }
        #region UI event handlers (resize, expand toggling)
        //resizing handler for the window
        private void ModSelectionList_SizeChanged(object sender, EventArgs e)
        {
            if (!this.Visible)
                return;
            if (this.WindowState == FormWindowState.Minimized)
                return;
            colapseAllButton.Location = new Point(this.Size.Width - 20 - colapseAllButton.Size.Width - 6 - expandAllButton.Size.Width, colapseAllButton.Location.Y);
            expandAllButton.Location = new Point(this.Size.Width - 20 - expandAllButton.Size.Width, expandAllButton.Location.Y);
            searchCB.Location = new Point(this.Size.Width - 20 - searchCB.Size.Width, searchCB.Location.Y);
            continueButton.Location = new Point(this.Size.Width - 20 - continueButton.Size.Width, this.Size.Height - 39 - continueButton.Size.Height - difference);
            cancelButton.Location = new Point(this.Size.Width - 20 - continueButton.Size.Width - 6 - cancelButton.Size.Width, this.Size.Height - 39 - continueButton.Size.Height - difference);
            modTabGroups.Size = new Size(this.Size.Width - 20 - modTabGroups.Location.X, this.Size.Height - modTabGroups.Location.Y - 39 - continueButton.Size.Height - 6 - difference);
            label1.Text = "" + this.Size.Width + " x " + this.Size.Height;
            loadConfigButton.Location = new Point(this.Size.Width - 20 - continueButton.Size.Width - 6 - cancelButton.Size.Width - 6 - saveConfigButton.Size.Width - 6 - loadConfigButton.Size.Width, this.Size.Height - 39 - continueButton.Size.Height - difference);
            saveConfigButton.Location = new Point(this.Size.Width - 20 - continueButton.Size.Width - 6 - cancelButton.Size.Width - 6 - saveConfigButton.Size.Width, this.Size.Height - 39 - continueButton.Size.Height - difference);
            clearSelectionsButton.Location = new Point(this.Size.Width - 20 - continueButton.Size.Width - 6 - cancelButton.Size.Width - 6 - saveConfigButton.Size.Width - 6 - loadConfigButton.Size.Width - 6 - clearSelectionsButton.Size.Width, this.Size.Height - 39 - continueButton.Size.Height - difference);
            if (this.Size.Height < 250)
            {
                this.Size = new Size(this.Size.Width, 250);
            }
            if (this.Size.Width < 550)
            {
                this.Size = new Size(550, this.Size.Height);
            }
            foreach (TabPage t in modTabGroups.TabPages)
            {
                foreach (Control c in t.Controls)
                {
                    if (c is Panel)
                    {
                        //mod panel
                        Panel p = (Panel)c;
                        resizePanel(p, t, 25);
                    }
                    else if (c is ElementHost)
                    {
                        ElementHost eh = (ElementHost)c;
                        eh.Size = new Size(t.Size.Width - 12, t.Size.Height - 10);
                        LegacySelectionList lsl = (LegacySelectionList)eh.Child;
                        try
                        {
                            lsl.RenderSize = new System.Windows.Size(eh.Size.Width - 2, eh.Size.Height - 2);
                            lsl.legacyTreeView.Width = eh.Size.Width - 4;
                            lsl.legacyTreeView.Height = eh.Size.Height - 4;
                        }
                        catch
                        {
                            // values are negative !
                            // this is catching the exception if the ModSelectionWindow is going to minimize
                        }
                    }
                }
            }
        }
        //recursive resize of the control panals
        private void resizePanel(Panel current, TabPage tp, int shrinkFactor)
        {
            current.Size = new Size(tp.Size.Width - shrinkFactor, current.Size.Height);
            foreach (Control controfds in current.Controls)
            {
                if (controfds is Panel)
                {
                    Panel subpp = (Panel)controfds;
                    resizePanel(subpp, tp, shrinkFactor + 25);
                }
            }
        }
        //handler for when a new tab page is selected
        private void modTabGroups_Selected(object sender, TabControlEventArgs e)
        {
            this.ModSelectionList_SizeChanged(null, null);
        }
        //handler for when a mod tab group is clicked on
        private void modTabGroups_Click(object sender, EventArgs e)
        {
            this.ModSelectionList_SizeChanged(null, null);
        }
        private void ColapseAllButton_Click(object sender, EventArgs e)
        {
            foreach (Control c in modTabGroups.SelectedTab.Controls)
            {
                if (c is ElementHost)
                {
                    ElementHost eh = (ElementHost)c;
                    LegacySelectionList lsl = (LegacySelectionList)eh.Child;
                    foreach (System.Windows.Controls.TreeViewItem tvi in lsl.legacyTreeView.Items)
                    {
                        tvi.IsExpanded = false;
                        if (tvi.Items.Count > 0)
                        {
                            processTreeViewItems(tvi.Items, false);
                        }
                    }
                }
            }
        }

        private void expandAllButton_Click(object sender, EventArgs e)
        {
            foreach (Control c in modTabGroups.SelectedTab.Controls)
            {
                if (c is ElementHost)
                {
                    ElementHost eh = (ElementHost)c;
                    LegacySelectionList lsl = (LegacySelectionList)eh.Child;
                    foreach (System.Windows.Controls.TreeViewItem tvi in lsl.legacyTreeView.Items)
                    {
                        tvi.IsExpanded = true;
                        if (tvi.Items.Count > 0)
                        {
                            processTreeViewItems(tvi.Items, true);
                        }
                    }
                }
            }
        }

        private void processTreeViewItems(System.Windows.Controls.ItemCollection ic, bool expand)
        {
            foreach (System.Windows.Controls.TreeViewItem tvi in ic)
            {
                if (expand)
                    tvi.IsExpanded = true;
                else
                    tvi.IsExpanded = false;
                if (tvi.Items.Count > 0)
                {
                    processTreeViewItems(tvi.Items, expand);
                }
            }
        }
        #endregion

        #region search box code
        private void searchComboBox_TextUpdate(object sender, EventArgs e)
        {
            ComboBox searchComboBox = (ComboBox)sender;
            string filter_param = searchComboBox.Text;
            List<SelectableDatabasePackage> filteredItems = null;
            if (!String.IsNullOrWhiteSpace(filter_param))
            {
                String[] filtered_parts = filter_param.Split('*');
                //force filteredItems to be mod or first level config
                filteredItems = new List<SelectableDatabasePackage>(completeModSearchList);
                foreach (var f in filtered_parts)
                {
                    filteredItems = filteredItems.FindAll(x => x.nameFormated.ToLower().Contains(f.ToLower()));
                }
            }
            if (filteredItems == null)
            {
                searchComboBox.DataSource = completeModSearchList;
                searchComboBox.SelectedIndex = -1;
            }
            else if (filteredItems.Count == 0)
            {
                searchComboBox.ForeColor = System.Drawing.Color.Red;
            }
            else
            {
                ignoreSelections = false;
                searchComboBox.ForeColor = System.Drawing.Color.Black;
                searchComboBox.DataSource = filteredItems;
                searchComboBox.DroppedDown = true;
            }
            Cursor.Current = Cursors.Default;
            searchComboBox.IntegralHeight = true;
            searchComboBox.Text = filter_param;
            // set the position of the cursor
            searchComboBox.SelectionStart = filter_param.Length;
            searchComboBox.SelectionLength = 0;
        }

        private void searchCB_SelectionChangeCommitted(object sender, EventArgs e)
        {
            ComboBox sendah = (ComboBox)sender;
            if (sendah.SelectedIndex == -1 || ignoreSelections)
            {
                return;
            }
            if (Settings.SView == Settings.SelectionView.Default)
            {
                if (sendah.SelectedItem is Mod)
                {
                    Mod m = (Mod)sendah.SelectedItem;
                    if (modTabGroups.TabPages.Contains(m.tabIndex))
                    {
                        modTabGroups.SelectedTab = m.tabIndex;
                    }
                    ModFormCheckBox c = (ModFormCheckBox)m.modFormCheckBox;
                    c.Focus();

                }
                else if (sendah.SelectedItem is Config)
                {
                    Config c = (Config)sendah.SelectedItem;
                    if (modTabGroups.TabPages.Contains(c.parentMod.tabIndex))
                    {
                        modTabGroups.SelectedTab = c.parentMod.tabIndex;
                    }
                    if (c.configUIComponent is ConfigFormCheckBox)
                    {
                        ConfigFormCheckBox cb = (ConfigFormCheckBox)c.configUIComponent;
                        cb.Focus();
                    }
                    else if (c.configUIComponent is ConfigFormComboBox)
                    {
                        ConfigFormComboBox cb = (ConfigFormComboBox)c.configUIComponent;
                        cb.Focus();
                    }
                    else if (c.configUIComponent is ConfigFormRadioButton)
                    {
                        //this one is the problem
                        ConfigFormRadioButton cb = (ConfigFormRadioButton)c.configUIComponent;
                        cb.CheckedChanged -= configControlRB_CheckedChanged;
                        bool realChecked = cb.Checked;
                        cb.Focus();
                        cb.Checked = realChecked;
                        cb.CheckedChanged += configControlRB_CheckedChanged;
                    }
                }
            }
            else if (Settings.SView == Settings.SelectionView.Legacy)
            {
                if (sendah.SelectedItem is Mod)
                {
                    Mod m = (Mod)sendah.SelectedItem;
                    if (modTabGroups.TabPages.Contains(m.tabIndex))
                    {
                        modTabGroups.SelectedTab = m.tabIndex;
                    }
                    ModWPFCheckBox c = (ModWPFCheckBox)m.modFormCheckBox;
                    c.Focus();
                    this.ModSelectionList_SizeChanged(null, null);
                }
                else if (sendah.SelectedItem is Config)
                {
                    Config c = (Config)sendah.SelectedItem;
                    if (modTabGroups.TabPages.Contains(c.parentMod.tabIndex))
                    {
                        modTabGroups.SelectedTab = c.parentMod.tabIndex;
                    }
                    System.Windows.Controls.Control con = (System.Windows.Controls.Control)c.configUIComponent;
                    con.Focus();
                    this.ModSelectionList_SizeChanged(null, null);
                }
            }
        }

        private void searchCB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                searchCB_SelectionChangeCommitted(sender, null);
            }
        }
        #endregion
    }
}
