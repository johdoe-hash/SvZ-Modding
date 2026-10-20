using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using SvZModding;

namespace SvZModManager
{
	// The manager window: game folder and loader status on top, the mod list (load order, on/off) on
	// the left, the selected mod's details and per-part switches on the right, Play at the bottom.
	// Every change is saved to Mods/modlist.txt immediately; the game reads it when it starts.
	internal class MainForm : Form
	{
		private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SvZModManager", "settings.txt");

		private GameInstall m_Game;
		private List<ModEntry> m_Mods = new List<ModEntry>();
		private bool m_Updating;

		private readonly TextBox m_GameFolder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
		private readonly Label m_LoaderStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
		private readonly Button m_InstallLoader = new Button { AutoSize = true };
		private readonly Button m_UninstallLoader = new Button { Text = "Uninstall loader…", AutoSize = true };
		private readonly ListView m_List = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill };
		private readonly Button m_Up = new Button { Text = "▲ Up", AutoSize = true };
		private readonly Button m_Down = new Button { Text = "▼ Down", AutoSize = true };
		private readonly Label m_Name = new Label { AutoSize = true };
		private readonly Label m_Byline = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
		private readonly Label m_Description = new Label { AutoSize = true };
		private readonly GroupBox m_PartsBox = new GroupBox { Text = "Use from this mod", AutoSize = true, Dock = DockStyle.Top };
		private readonly Dictionary<ModCategory, CheckBox> m_Parts = new Dictionary<ModCategory, CheckBox>();
		private readonly Label m_Overlaps = new Label { AutoSize = true, ForeColor = Color.DarkGoldenrod };
		private readonly ListBox m_Files = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
		private readonly CheckBox m_ModsEnabled = new CheckBox { Text = "Load mods when the game starts", AutoSize = true, Anchor = AnchorStyles.Left };
		private readonly Label m_Status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };
		private readonly Button m_Play = new Button { Text = "▶  Play", AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
		private readonly TableLayoutPanel m_Details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
		private readonly SplitContainer m_Split = new SplitContainer { Dock = DockStyle.Fill };

		public MainForm()
		{
			// Sizes below are in 96-DPI pixels; WinForms scales them to the screen's DPI.
			SuspendLayout();
			AutoScaleDimensions = new SizeF(96f, 96f);
			AutoScaleMode = AutoScaleMode.Dpi;
			Text = "SvZ Mod Manager";
			Font = SystemFonts.MessageBoxFont;
			ClientSize = new Size(980, 640);
			MinimumSize = new Size(760, 480);
			StartPosition = FormStartPosition.CenterScreen;
			BuildLayout();
			ResumeLayout(false);
			Shown += (s, e) =>
			{
				// Only now does the window have its real (DPI-scaled) size to divide.
				m_Split.SplitterDistance = m_Split.Width * 47 / 100;
				OpenGame(FindGameFolder());
			};
		}

		// ---------------------------------------------------------------- layout

		private void BuildLayout()
		{
			var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);

			// Game folder + loader status.
			var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
			top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			var browse = new Button { Text = "Browse…", AutoSize = true };
			browse.Click += (s, e) => BrowseForGame();
			top.Controls.Add(new Label { Text = "Game folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
			top.Controls.Add(m_GameFolder, 1, 0);
			top.Controls.Add(browse, 2, 0);
			var loaderRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 4, 0, 6) };
			loaderRow.Controls.Add(m_LoaderStatus);
			loaderRow.Controls.Add(m_InstallLoader);
			loaderRow.Controls.Add(m_UninstallLoader);
			top.Controls.Add(new Label { Text = "Mod loader:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
			top.Controls.Add(loaderRow, 1, 1);
			top.SetColumnSpan(loaderRow, 2);
			m_InstallLoader.Click += (s, e) => InstallLoader();
			m_UninstallLoader.Click += (s, e) => UninstallLoader();
			root.Controls.Add(top, 0, 0);

			// Mod list | details.
			SplitContainer split = m_Split;
			root.Controls.Add(split, 0, 1);
			var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			left.Controls.Add(new Label { Text = "Load order: top to bottom. Where two mods change the same thing, the lower one wins.", AutoSize = true, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
			m_List.Columns.Add("Mod", 190);
			m_List.Columns.Add("Version", 70);
			m_List.Columns.Add("Changes", 180);
			m_List.ItemChecked += OnModChecked;
			m_List.ClientSizeChanged += (s, e) => FitLastColumn();
			m_List.SelectedIndexChanged += (s, e) => ShowDetails();
			left.Controls.Add(m_List, 0, 1);
			var listButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
			var addZip = new Button { Text = "Add mod (.zip)…", AutoSize = true };
			var remove = new Button { Text = "Remove…", AutoSize = true };
			var openFolder = new Button { Text = "Open Mods folder", AutoSize = true };
			var refresh = new Button { Text = "Refresh", AutoSize = true };
			m_Up.Click += (s, e) => MoveSelected(-1);
			m_Down.Click += (s, e) => MoveSelected(1);
			addZip.Click += (s, e) => AddZip();
			remove.Click += (s, e) => RemoveMod();
			openFolder.Click += (s, e) => OpenModsFolder();
			refresh.Click += (s, e) => LoadMods();
			listButtons.Controls.AddRange(new Control[] { m_Up, m_Down, addZip, remove, openFolder, refresh });
			left.Controls.Add(listButtons, 0, 2);
			split.Panel1.Controls.Add(left);
			split.Panel1.Padding = new Padding(0, 0, 6, 0);

			m_Name.Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold);
			m_Description.Margin = new Padding(3, 6, 3, 10);
			var parts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(4) };
			foreach (ModCategory category in ModLayout.Categories)
			{
				var box = new CheckBox { AutoSize = true, Tag = category };
				box.CheckedChanged += OnPartChanged;
				m_Parts[category] = box;
				parts.Controls.Add(box);
			}
			m_PartsBox.Controls.Add(parts);
			m_Overlaps.Margin = new Padding(3, 8, 3, 4);
			m_Details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			m_Details.RowCount = 7;
			for (int i = 0; i < 6; i++)
			{
				m_Details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			}
			m_Details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			m_Details.Controls.Add(m_Name, 0, 0);
			m_Details.Controls.Add(m_Byline, 0, 1);
			m_Details.Controls.Add(m_Description, 0, 2);
			m_Details.Controls.Add(m_PartsBox, 0, 3);
			m_Details.Controls.Add(m_Overlaps, 0, 4);
			m_Details.Controls.Add(new Label { Text = "Files", AutoSize = true, Margin = new Padding(3, 6, 3, 2) }, 0, 5);
			m_Details.Controls.Add(m_Files, 0, 6);
			m_Details.SizeChanged += (s, e) => WrapLabels();
			split.Panel2.Controls.Add(m_Details);
			split.Panel2.Padding = new Padding(6, 0, 0, 0);

			// Bottom bar.
			var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			m_ModsEnabled.CheckedChanged += (s, e) =>
			{
				if (!m_Updating && m_Game != null)
				{
					m_Game.ModsEnabled = m_ModsEnabled.Checked;
					SetStatus(m_ModsEnabled.Checked ? "Mods will load when the game starts." : "The game will start without mods.");
				}
			};
			m_Play.Font = new Font(Font, FontStyle.Bold);
			m_Play.Click += (s, e) => Play();
			bottom.Controls.Add(m_ModsEnabled, 0, 0);
			bottom.Controls.Add(m_Status, 1, 0);
			bottom.Controls.Add(m_Play, 2, 0);
			root.Controls.Add(bottom, 0, 2);
			AcceptButton = m_Play;
		}

		private void FitLastColumn()
		{
			int others = m_List.Columns[0].Width + m_List.Columns[1].Width;
			m_List.Columns[2].Width = Math.Max(80, m_List.ClientSize.Width - others);
		}

		private void WrapLabels()
		{
			int width = Math.Max(100, m_Details.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
			m_Description.MaximumSize = new Size(width, 0);
			m_Overlaps.MaximumSize = new Size(width, 0);
		}

		private void SetStatus(string text)
		{
			m_Status.Text = text;
		}

		// ---------------------------------------------------------------- game folder and loader

		// Next to the manager if it was put in the game folder; otherwise the last folder used.
		private static string FindGameFolder()
		{
			string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
			if (GameInstall.TryOpen(here) != null)
			{
				return here;
			}
			try
			{
				return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null;
			}
			catch (IOException)
			{
				return null;
			}
		}

		private void BrowseForGame()
		{
			using var dialog = new FolderBrowserDialog { Description = "Choose the folder that contains the game's .exe", UseDescriptionForTitle = true };
			if (m_Game != null)
			{
				dialog.SelectedPath = m_Game.Folder;
			}
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			if (GameInstall.TryOpen(dialog.SelectedPath) == null)
			{
				MessageBox.Show(this, "That folder doesn't contain a Unity game (an .exe with a matching _Data folder).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			OpenGame(dialog.SelectedPath);
		}

		private void OpenGame(string folder)
		{
			m_Game = GameInstall.TryOpen(folder);
			m_GameFolder.Text = m_Game != null ? m_Game.Folder : "(not chosen - click Browse)";
			if (m_Game != null)
			{
				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
					File.WriteAllText(SettingsFile, m_Game.Folder);
				}
				catch (IOException)
				{
				}
			}
			UpdateLoaderStatus();
			LoadMods();
		}

		private void UpdateLoaderStatus()
		{
			m_Updating = true;
			LoaderState state = m_Game != null ? m_Game.Loader : LoaderState.NotInstalled;
			m_LoaderStatus.Text = m_Game == null ? "choose the game folder first"
				: state == LoaderState.Installed ? "installed ✓"
				: state == LoaderState.Outdated ? "installed (a newer version is available)"
				: "not installed - mods can't load until it is";
			m_InstallLoader.Text = state == LoaderState.Outdated ? "Update loader" : "Install loader";
			m_InstallLoader.Visible = m_Game != null && state != LoaderState.Installed;
			m_UninstallLoader.Visible = m_Game != null && state != LoaderState.NotInstalled;
			m_ModsEnabled.Enabled = m_Game != null && state != LoaderState.NotInstalled;
			m_ModsEnabled.Checked = m_ModsEnabled.Enabled && m_Game.ModsEnabled;
			m_Play.Enabled = m_Game != null;
			m_Updating = false;
		}

		private void InstallLoader()
		{
			string message = "This adds BepInEx (a mod-loading framework) and the SvZ mod loader to\n\n" + m_Game.Folder +
				"\n\nThe game's own files aren't changed, and \"Uninstall loader\" removes it again.";
			if (MessageBox.Show(this, message, "Install mod loader", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
			{
				return;
			}
			try
			{
				m_Game.InstallLoader();
				SetStatus("Mod loader installed.");
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
				MessageBox.Show(this, "Couldn't install the mod loader: " + e.Message + "\n\nIs the game running?", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			UpdateLoaderStatus();
			LoadMods();
		}

		private void UninstallLoader()
		{
			string message = "Remove BepInEx and the SvZ mod loader from the game folder?\n\nThis also deletes the BepInEx folder (its settings and logs). Your Mods folder is kept.";
			if (MessageBox.Show(this, message, "Uninstall mod loader", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			{
				return;
			}
			try
			{
				m_Game.UninstallLoader();
				SetStatus("Mod loader removed. The game will start as shipped.");
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
				MessageBox.Show(this, "Couldn't remove the mod loader: " + e.Message + "\n\nIs the game running?", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			UpdateLoaderStatus();
		}

		private void Play()
		{
			if (m_Game == null)
			{
				return;
			}
			Process.Start(new ProcessStartInfo(m_Game.Exe) { WorkingDirectory = m_Game.Folder, UseShellExecute = true });
			SetStatus(m_ModsEnabled.Enabled && m_ModsEnabled.Checked ? "Game started with " + m_Mods.Count(m => m.Enabled) + " mod(s)." : "Game started without mods.");
		}

		// ---------------------------------------------------------------- mod list

		private void LoadMods()
		{
			string selected = SelectedMod?.FolderName;
			m_Mods = m_Game != null ? ModEntry.Scan(m_Game.ModsFolder) : new List<ModEntry>();
			m_Updating = true;
			m_List.BeginUpdate();
			m_List.Items.Clear();
			foreach (ModEntry mod in m_Mods)
			{
				var item = new ListViewItem(new[] { mod.Manifest.Name, mod.Manifest.Version, mod.Summary }) { Checked = mod.Enabled, Tag = mod };
				m_List.Items.Add(item);
				if (mod.FolderName == selected)
				{
					item.Selected = true;
				}
			}
			if (m_List.SelectedItems.Count == 0 && m_List.Items.Count > 0)
			{
				m_List.Items[0].Selected = true;
			}
			m_List.EndUpdate();
			m_Updating = false;
			if (m_Game != null && m_Mods.Count == 0)
			{
				SetStatus("No mods yet - add one with \"Add mod (.zip)\" or put its folder in the Mods folder.");
			}
			ShowDetails();
		}

		private ModEntry SelectedMod => m_List.SelectedItems.Count > 0 ? (ModEntry)m_List.SelectedItems[0].Tag : null;

		private void Save()
		{
			if (m_Game == null || !Directory.Exists(m_Game.ModsFolder))
			{
				return;
			}
			try
			{
				ModEntry.Save(m_Game.ModsFolder, m_Mods);
				SetStatus("Saved. Changes apply the next time the game starts.");
			}
			catch (IOException e)
			{
				SetStatus("Couldn't save: " + e.Message);
			}
		}

		private void OnModChecked(object sender, ItemCheckedEventArgs e)
		{
			if (m_Updating)
			{
				return;
			}
			((ModEntry)e.Item.Tag).Enabled = e.Item.Checked;
			Save();
			ShowDetails();
		}

		private void MoveSelected(int direction)
		{
			ModEntry mod = SelectedMod;
			if (mod == null)
			{
				return;
			}
			int from = m_Mods.IndexOf(mod);
			int to = from + direction;
			if (to < 0 || to >= m_Mods.Count)
			{
				return;
			}
			m_Mods.RemoveAt(from);
			m_Mods.Insert(to, mod);
			Save();
			LoadMods();
		}

		private void ShowDetails()
		{
			ModEntry mod = SelectedMod;
			m_Details.Visible = mod != null;
			m_Up.Enabled = mod != null && m_Mods.IndexOf(mod) > 0;
			m_Down.Enabled = mod != null && m_Mods.IndexOf(mod) < m_Mods.Count - 1;
			if (mod == null)
			{
				return;
			}
			m_Updating = true;
			m_Name.Text = mod.Manifest.Name;
			var byline = new List<string>();
			if (mod.Manifest.Version.Length > 0)
			{
				byline.Add("version " + mod.Manifest.Version);
			}
			if (mod.Manifest.Author.Length > 0)
			{
				byline.Add("by " + mod.Manifest.Author);
			}
			byline.Add("folder Mods\\" + mod.FolderName);
			m_Byline.Text = string.Join("  ·  ", byline);
			m_Description.Text = mod.Manifest.Description.Length > 0 ? mod.Manifest.Description : "(no description - add one to the mod's mod.txt)";
			foreach (ModCategory category in ModLayout.Categories)
			{
				int count = mod.Count(category);
				CheckBox box = m_Parts[category];
				box.Text = ModLayout.Describe(category) + (count > 0 ? $"  ({count} file{(count == 1 ? "" : "s")})" : "  (none in this mod)");
				box.Enabled = mod.Enabled && count > 0;
				box.Checked = count > 0 && !mod.Off.Contains(category);
			}
			m_PartsBox.Text = mod.Enabled ? "Use from this mod" : "Use from this mod (the mod is turned off)";
			m_Overlaps.Text = DescribeOverlaps(mod);
			m_Overlaps.Visible = m_Overlaps.Text.Length > 0;
			m_Files.BeginUpdate();
			m_Files.Items.Clear();
			foreach (ModCategory category in ModLayout.Categories)
			{
				if (mod.Files.TryGetValue(category, out List<string> files))
				{
					foreach (string file in files.OrderBy(f => f))
					{
						m_Files.Items.Add(file + (mod.Uses(category) ? "" : "   (not used)"));
					}
				}
			}
			m_Files.EndUpdate();
			WrapLabels();
			m_Updating = false;
		}

		// Other turned-on mods that change the same things as this one.
		private string DescribeOverlaps(ModEntry mod)
		{
			if (!mod.Enabled)
			{
				return "";
			}
			var lines = new List<string>();
			int index = m_Mods.IndexOf(mod);
			foreach (ModEntry other in m_Mods.Where(m => m != mod && m.Enabled))
			{
				var mine = new Dictionary<string, string>();
				foreach (ModCategory category in ModLayout.Categories.Where(mod.Uses))
				{
					foreach (string file in mod.Files[category])
					{
						string key = ModEntry.GameKey(file);
						if (key != null)
						{
							mine[key] = file;
						}
					}
				}
				var shared = new List<string>();
				foreach (ModCategory category in ModLayout.Categories.Where(other.Uses))
				{
					foreach (string file in other.Files[category])
					{
						string key = ModEntry.GameKey(file);
						if (key != null && mine.ContainsKey(key))
						{
							shared.Add(mine[key]);
						}
					}
				}
				if (shared.Count == 0)
				{
					continue;
				}
				bool allData = shared.All(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".variant.txt", StringComparison.OrdinalIgnoreCase));
				string winner = m_Mods.IndexOf(other) > index ? other.Manifest.Name : mod.Manifest.Name;
				string how = allData ? "both apply (data files are merged; where they set the same value, " + winner + " wins)" : winner + " wins (it's lower in the list)";
				lines.Add($"Also changed by {other.Manifest.Name}: {string.Join(", ", shared.Distinct().Take(4))}{(shared.Count > 4 ? ", …" : "")} - {how}.");
			}
			return string.Join("\n", lines);
		}

		private void OnPartChanged(object sender, EventArgs e)
		{
			ModEntry mod = SelectedMod;
			if (m_Updating || mod == null)
			{
				return;
			}
			var box = (CheckBox)sender;
			var category = (ModCategory)box.Tag;
			if (box.Checked)
			{
				mod.Off.Remove(category);
			}
			else
			{
				mod.Off.Add(category);
			}
			Save();
			foreach (ListViewItem item in m_List.Items)
			{
				if (item.Tag == mod)
				{
					item.SubItems[2].Text = mod.Summary;
				}
			}
			ShowDetails();
		}

		// ---------------------------------------------------------------- adding and removing mods

		private void AddZip()
		{
			if (m_Game == null)
			{
				return;
			}
			using var dialog = new OpenFileDialog { Filter = "Mod archives (*.zip)|*.zip", Title = "Add a mod" };
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			string temp = Path.Combine(Path.GetTempPath(), "SvZModManager-" + Guid.NewGuid().ToString("N"));
			try
			{
				ZipFile.ExtractToDirectory(dialog.FileName, temp);
				// A zip either holds the mod folder itself or the mod's contents directly.
				string source = temp;
				string name = Path.GetFileNameWithoutExtension(dialog.FileName);
				string[] dirs = Directory.GetDirectories(temp);
				if (dirs.Length == 1 && Directory.GetFiles(temp).Length == 0)
				{
					source = dirs[0];
					name = Path.GetFileName(dirs[0]);
				}
				Directory.CreateDirectory(m_Game.ModsFolder);
				string target = Path.Combine(m_Game.ModsFolder, name);
				if (Directory.Exists(target))
				{
					if (MessageBox.Show(this, $"A mod folder named \"{name}\" already exists. Replace it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
					{
						return;
					}
					Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
				}
				Directory.Move(source, target);
				LoadMods();
				Save();
				SetStatus($"Added \"{name}\".");
			}
			catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
			{
				MessageBox.Show(this, "Couldn't add the mod: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				if (Directory.Exists(temp))
				{
					Directory.Delete(temp, true);
				}
			}
		}

		private void RemoveMod()
		{
			ModEntry mod = SelectedMod;
			if (mod == null)
			{
				return;
			}
			if (MessageBox.Show(this, $"Move \"{mod.Manifest.Name}\" (Mods\\{mod.FolderName}) to the Recycle Bin?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			{
				return;
			}
			try
			{
				Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(mod.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
			}
			catch (Exception e) when (e is IOException || e is OperationCanceledException)
			{
				MessageBox.Show(this, "Couldn't remove the mod: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			m_Mods.Remove(mod);
			Save();
			LoadMods();
		}

		private void OpenModsFolder()
		{
			if (m_Game == null)
			{
				return;
			}
			Directory.CreateDirectory(m_Game.ModsFolder);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + m_Game.ModsFolder + "\""));
		}
	}
}
