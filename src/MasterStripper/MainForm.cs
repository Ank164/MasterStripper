using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace MasterStripper;

internal sealed class MainForm : Form
{
    private readonly ListBox _files = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly ListBox _masters = new()
    {
        Dock = DockStyle.Fill,
        SelectionMode = SelectionMode.MultiExtended,
        Font = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size + 2)
    };
    private readonly TextBox _masterSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "Type any part of the master filename…" };
    private readonly List<string> _availableMasters = [];
    private readonly Button _strip = new() { Text = "Strip selected master(s)", Dock = DockStyle.Fill, Enabled = false };
    private readonly Button _add = new() { Text = "Add patches…", Dock = DockStyle.Fill };
    private readonly Button _remove = new() { Text = "Remove selected", Dock = DockStyle.Fill };
    private readonly Button _flagLight = new() { Text = "Flag selected light", Dock = DockStyle.Fill, Enabled = false };
    private readonly CheckBox _copies = new() { Text = "Write cleaned copies instead of replacing originals", Checked = false, AutoSize = true };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
    };

    public MainForm(IEnumerable<string> initialFiles)
    {
        Text = "Master Stripper 2.4.1";
        Width = 780;
        Height = 620;
        MinimumSize = new System.Drawing.Size(650, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

        var lowerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lowerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        lowerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lowerLayout.Controls.Add(_copies, 0, 0);
        lowerLayout.Controls.Add(_strip, 1, 0);
        lowerLayout.Controls.Add(_log, 0, 1);
        lowerLayout.SetColumnSpan(_log, 2);

        var masterAndLog = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new System.Drawing.Size(100, 250),
            SplitterDistance = 105,
            SplitterWidth = 6,
            Panel1MinSize = 55,
            Panel2MinSize = 100,
            Margin = Padding.Empty
        };
        masterAndLog.Panel1.Controls.Add(_masters);
        masterAndLog.Panel2.Controls.Add(lowerLayout);

        var intro = new Label
        {
            Text = "Drop Skyrim patch plugins here, then choose the master to remove.",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        };
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 3);
        layout.Controls.Add(_files, 0, 1);
        layout.SetColumnSpan(_files, 3);
        layout.Controls.Add(_add, 0, 2);
        layout.Controls.Add(_remove, 1, 2);
        layout.Controls.Add(_flagLight, 2, 2);
        layout.Controls.Add(new Label { Text = "Master to strip — search by partial filename:", AutoSize = true, Padding = new Padding(0, 8, 0, 2) }, 0, 3);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 3)!, 3);
        layout.Controls.Add(_masterSearch, 0, 4);
        layout.SetColumnSpan(_masterSearch, 3);
        layout.Controls.Add(masterAndLog, 0, 5);
        layout.SetColumnSpan(masterAndLog, 3);
        Controls.Add(layout);

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) => AddFiles((string[]?)e.Data?.GetData(DataFormats.FileDrop) ?? []);
        _add.Click += (_, _) => BrowseFiles();
        _remove.Click += (_, _) =>
        {
            foreach (var item in _files.SelectedItems.Cast<string>().ToArray()) _files.Items.Remove(item);
            RefreshMasters();
        };
        _files.SelectedIndexChanged += (_, _) => UpdateActionButtons();
        _flagLight.Click += async (_, _) => await FlagSelectedLightAsync();
        _masters.SelectedIndexChanged += (_, _) => UpdateStripButton();
        _masterSearch.TextChanged += (_, _) => ApplyMasterFilter();
        _strip.Click += async (_, _) => await StripAsync();

        AddFiles(initialFiles);
        Log("Ready. Originals are backed up before replacement.");
    }

    private void BrowseFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Skyrim plugins (*.esp;*.esm;*.esl)|*.esp;*.esm;*.esl",
            Multiselect = true,
            Title = "Choose patch plugins"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var existing = _files.Items.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            if (!File.Exists(path) || !IsPlugin(path) || !existing.Add(path)) continue;
            _files.Items.Add(path);
        }
        RefreshMasters();
    }

    private void RefreshMasters()
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = true;

        foreach (var path in _files.Items.Cast<string>())
        {
            try
            {
                using var mod = OpenReadOnly(path);
                var names = mod.MasterReferences.Select(x => x.Master.FileName.String).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (first) { common = names; first = false; }
                else common.IntersectWith(names);
            }
            catch (Exception ex)
            {
                Log($"Could not inspect {Path.GetFileName(path)}: {ShortError(ex)}");
            }
        }

        _availableMasters.Clear();
        _availableMasters.AddRange(common.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        ApplyMasterFilter();
    }

    private void ApplyMasterFilter()
    {
        var previous = _masters.SelectedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = _masterSearch.Text.Trim();
        _masters.BeginUpdate();
        _masters.Items.Clear();
        foreach (var name in _availableMasters)
        {
            if (query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase))
                _masters.Items.Add(name);
        }
        _masters.EndUpdate();

        foreach (var name in previous)
        {
            var index = _masters.Items.IndexOf(name);
            if (index >= 0) _masters.SetSelected(index, true);
        }
        UpdateStripButton();
    }

    private async Task StripAsync()
    {
        var masterNames = _masters.SelectedItems.Cast<string>().ToArray();
        if (masterNames.Length == 0) return;
        var paths = _files.Items.Cast<string>().ToArray();
        var targets = masterNames.Select(name => ModKey.FromFileName(name)).ToHashSet();
        var copyMode = _copies.Checked;
        var masterSummary = string.Join(", ", masterNames);
        var answer = MessageBox.Show(
            this,
            $"Remove every complete record defined by or referencing the following {masterNames.Length} master(s) from {paths.Length} patch(es)?\n\n" +
            $"{string.Join(Environment.NewLine, masterNames)}\n\n" +
            (copyMode ? "Cleaned copies will be created beside the originals." : "Originals will be replaced after .backup copies are created."),
            "Confirm master strip",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.OK) return;

        SetBusy(true);
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    var result = await Task.Run(() => StripOne(path, targets, copyMode));
                    Log(result);
                }
                catch (Exception ex)
                {
                    Log($"FAILED {Path.GetFileName(path)} while stripping {masterSummary}: {ShortError(ex)}");
                }
            }
            MessageBox.Show(this, "Finished. Check the log for results.", "Master Stripper",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task FlagSelectedLightAsync()
    {
        var paths = _files.SelectedItems.Cast<string>()
            .Where(path => Path.GetExtension(path).Equals(".esp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (paths.Length == 0) return;

        var answer = MessageBox.Show(
            this,
            $"Set the ESL/light flag on {paths.Length} selected ESP(s)?\n\n" +
            "This does not compact FormIDs. Only continue if these plugins are already compacted for ESL. " +
            "A .backup copy will be created before each changed plugin is replaced.",
            "Confirm light flag",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.OK) return;

        SetBusy(true);
        var changedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    var changed = await Task.Run(() => PluginHeaderEditor.FlagLight(path));
                    if (changed) changedCount++;
                    else skippedCount++;
                    Log(changed
                        ? $"OK {Path.GetFileName(path)} — set ESL/light flag"
                        : $"SKIPPED {Path.GetFileName(path)} — already ESL/light flagged");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Log($"FAILED {Path.GetFileName(path)} while setting ESL/light flag: {ShortError(ex)}");
                }
            }

            MessageBox.Show(
                this,
                $"Verified ESL/light flag on {changedCount} plugin(s).\n" +
                $"Already flagged: {skippedCount}. Failed: {failedCount}.\n\n" +
                "Press F5 in MO2 to refresh its Plugins pane. If MO2 still does not show ESL, " +
                "check whether another mod supplies the winning copy of the same plugin filename.",
                "Light flag results",
                MessageBoxButtons.OK,
                failedCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string StripOne(string path, IReadOnlySet<ModKey> targets, bool copyMode)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var output = copyMode
            ? UniquePath(Path.Combine(directory, $"{stem}.stripped{extension}"))
            : Path.Combine(directory, $".{Path.GetFileName(path)}.masterstripper.tmp");
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "MasterStripper", Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(stagingDirectory, "input");
        var writeDirectory = Path.Combine(stagingDirectory, "output");
        var workingPlugin = Path.Combine(workingDirectory, Path.GetFileName(path));
        var stagedPlugin = Path.Combine(writeDirectory, Path.GetFileName(path));
        Directory.CreateDirectory(stagingDirectory);
        var rawRemoved = 0;
        var structuredRemoved = 0;
        try
        {
            Directory.CreateDirectory(workingDirectory);
            Directory.CreateDirectory(writeDirectory);
            File.Copy(path, workingPlugin);

            ISkyrimMod mod;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    mod = OpenMutable(workingPlugin);
                    break;
                }
                catch (Exception ex) when (FindMissing(ex) is { FormKey: not null } missing && attempt < 64)
                {
                    var removed = RawPluginEditor.RemoveRecordOrContainers(
                        workingPlugin, missing.FormKey!.Value, DataFolder());
                    if (removed == 0)
                        throw new InvalidOperationException(
                            $"Raw fallback could not locate {missing.FormKey.Value} in any record.", ex);
                    rawRemoved += removed;
                }
            }

            var doomed = mod.EnumerateMajorRecords()
                .Where(record =>
                    targets.Contains(record.FormKey.ModKey) ||
                    record.EnumerateFormLinks().Any(link => targets.Contains(link.FormKey.ModKey)))
                .Select(record => (record.FormKey, record.Type))
                .Distinct()
                .ToArray();
            structuredRemoved = doomed.Length;

            foreach (var (formKey, type) in doomed.Reverse())
                mod.Remove(formKey, type, throwIfUnknown: false);

            var remaining = mod.EnumerateMajorRecords()
                .Any(record => targets.Contains(record.FormKey.ModKey) ||
                               record.EnumerateFormLinks().Any(link => targets.Contains(link.FormKey.ModKey)));
            if (remaining) throw new InvalidOperationException("A reference to a selected master remained after removal.");

            for (var i = mod.MasterReferences.Count - 1; i >= 0; i--)
                if (targets.Contains(mod.MasterReferences[i].Master)) mod.MasterReferences.RemoveAt(i);

            RebuildOverriddenForms(mod);

            mod.BeginWrite
                .ToPath(stagedPlugin)
                .WithLoadOrderFromHeaderMasters()
                .WithDataFolder(DataFolder())
                .Write();
            File.Move(stagedPlugin, output);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }

        if (!copyMode)
        {
            var backup = UniquePath(path + ".backup");
            File.Move(path, backup);
            try { File.Move(output, path); }
            catch
            {
                File.Move(backup, path);
                throw;
            }
        }

        return $"OK {Path.GetFileName(path)} — removed {rawRemoved + structuredRemoved} record(s); wrote {Path.GetFileName(copyMode ? output : path)}";
    }

    internal static void RebuildOverriddenForms(ISkyrimMod mod)
    {
        if (!mod.IsMaster && mod.ModHeader.OverriddenForms is null) return;

        mod.ModHeader.SetOverriddenForms(
            mod.EnumerateMajorRecords()
                .Where(record => record.FormKey.ModKey != mod.ModKey)
                .Select(record => record.FormKey)
                .Distinct());
    }

    private static MissingRecordException? FindMissing(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is MissingRecordException missing) return missing;
        return null;
    }

    private static ISkyrimModDisposableGetter OpenReadOnly(string path)
    {
        using var env = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
        var masters = env.LoadOrder.ListedOrder.Select(x => x.Mod).OfType<IModMasterStyledGetter>();
        return SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(path)
            .WithLoadOrder(masters)
            .WithDataFolder(DataFolder())
            .Construct();
    }

    private static ISkyrimMod OpenMutable(string path)
    {
        using var header = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(path)
            .WithLoadOrder(Array.Empty<ModKey>())
            .WithDataFolder(DataFolder())
            .Construct();
        var masters = header.MasterReferences.Select(x => x.Master).ToArray();
        return SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(path)
            .WithLoadOrder(masters)
            .WithDataFolder(DataFolder())
            .Mutable()
            .Construct();
    }

    private static string DataFolder()
    {
        using var env = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
        return env.DataFolderPath;
    }

    private static bool IsPlugin(string path) =>
        new[] { ".esp", ".esm", ".esl" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string UniquePath(string desired)
    {
        if (!File.Exists(desired)) return desired;
        var directory = Path.GetDirectoryName(desired)!;
        var stem = Path.GetFileNameWithoutExtension(desired);
        var extension = Path.GetExtension(desired);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _strip.Enabled = !busy && _masters.SelectedItems.Count > 0 && _files.Items.Count > 0;
        _add.Enabled = _remove.Enabled = _masters.Enabled = _masterSearch.Enabled = _copies.Enabled = !busy;
        _flagLight.Enabled = !busy && SelectedEspCount() > 0;
    }

    private void UpdateStripButton() =>
        _strip.Enabled = _masters.SelectedItems.Count > 0 && _files.Items.Count > 0;

    private void UpdateActionButtons() => _flagLight.Enabled = SelectedEspCount() > 0;

    private int SelectedEspCount() => _files.SelectedItems.Cast<string>()
        .Count(path => Path.GetExtension(path).Equals(".esp", StringComparison.OrdinalIgnoreCase));

    private void Log(string message) =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private static string ShortError(Exception ex)
    {
        if (ex is MissingRecordException missing)
            return $"Missing record {missing.FormKey?.ToString() ?? missing.EditorID ?? "(unknown)"}" +
                   (missing.Type is null ? "" : $" <{missing.Type.Name}>");
        if (ex.InnerException is MissingRecordException innerMissing)
            return $"{ex.Message}: Missing record {innerMissing.FormKey?.ToString() ?? innerMissing.EditorID ?? "(unknown)"}" +
                   (innerMissing.Type is null ? "" : $" <{innerMissing.Type.Name}>");
        while (ex.InnerException is not null && string.IsNullOrWhiteSpace(ex.Message)) ex = ex.InnerException;
        return ex.Message;
    }
}
