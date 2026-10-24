using System.Text.Json;

namespace Ag4Gui;

internal sealed class MainForm : Form
{
    private readonly ComboBox _lang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _pattern = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _modePath = new()
    {
        Text = "Path (files)",
        AutoSize = true,
        Checked = true,
    };
    private readonly RadioButton _modeSource = new() { Text = "Source (string)", AutoSize = true };
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    private readonly TextBox _sourcePath = new() { Dock = DockStyle.Fill, Text = "snippet.java" };
    private readonly TextBox _source = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        AcceptsReturn = true,
        Font = new Font(FontFamily.GenericMonospace, 9f),
        Text = "class Demo {\n  void m() {\n    foo(1, 2);\n  }\n}\n",
    };
    private readonly Button _search = new()
    {
        Text = "Search",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
    };
    private readonly ListView _results = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
    };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Panel _pathPanel = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _sourcePanel = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 2,
        Visible = false,
    };
    private readonly Panel _inputHost = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "ag4 sample GUI (C# / PInvoke)";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(720, 560);
        Width = 960;
        Height = 680;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont;

        _lang.Items.AddRange(["java", "csharp"]);
        _lang.SelectedIndex = 0;
        _pattern.Text = "foo($$$ARGS)";
        try
        {
            _path.Text = Ag4Native.DefaultFixturesDir();
            _status.Text = $"ag4_native {Ag4Native.Version()}  lib={Ag4Native.ResolveLibDir()}";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }

        _results.Columns.Add("File", 300);
        _results.Columns.Add("Line", 70);
        _results.Columns.Add("Col", 70);
        _results.Columns.Add("Text", 460);

        _pathPanel.Controls.Add(_path);

        _sourcePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _sourcePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _sourcePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        _sourcePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _sourcePanel.Controls.Add(MakeLabel("Label"), 0, 0);
        _sourcePanel.Controls.Add(_sourcePath, 1, 0);
        _sourcePanel.Controls.Add(MakeLabel("Content"), 0, 1);
        _sourcePanel.Controls.Add(_source, 1, 1);

        _inputHost.Controls.Add(_pathPanel);
        _inputHost.Controls.Add(_sourcePanel);

        var modeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
        };
        modeRow.Controls.Add(_modePath);
        modeRow.Controls.Add(_modeSource);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 280,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12, 12, 12, 8),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        top.Controls.Add(MakeLabel("Language"), 0, 0);
        top.Controls.Add(_lang, 1, 0);
        top.Controls.Add(MakeLabel("Pattern"), 0, 1);
        top.Controls.Add(_pattern, 1, 1);
        top.Controls.Add(MakeLabel("Mode"), 0, 2);
        top.Controls.Add(modeRow, 1, 2);
        top.Controls.Add(MakeLabel("Input"), 0, 3);
        top.Controls.Add(_inputHost, 1, 3);
        top.Controls.Add(_search, 1, 4);

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            Padding = new Padding(12, 6, 12, 6),
        };
        bottom.Controls.Add(_status);

        Controls.Add(_results);
        Controls.Add(bottom);
        Controls.Add(top);

        _modePath.CheckedChanged += (_, _) => ApplyMode();
        _modeSource.CheckedChanged += (_, _) => ApplyMode();
        _search.Click += (_, _) => RunSearch();
        AcceptButton = _search;
        ApplyMode();
    }

    private void ApplyMode()
    {
        var useSource = _modeSource.Checked;
        _pathPanel.Visible = !useSource;
        _sourcePanel.Visible = useSource;
        if (useSource)
        {
            _sourcePanel.BringToFront();
        }
        else
        {
            _pathPanel.BringToFront();
        }
    }

    private static Label MakeLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 12, 0),
        };

    private void RunSearch()
    {
        _results.Items.Clear();
        try
        {
            object payload;
            if (_modeSource.Checked)
            {
                payload = new
                {
                    pattern = _pattern.Text,
                    language = _lang.SelectedItem?.ToString() ?? "java",
                    sources = new[] { new { path = _sourcePath.Text, content = _source.Text } },
                    strictness = "smart",
                    maxResults = 200,
                };
            }
            else
            {
                payload = new
                {
                    pattern = _pattern.Text,
                    language = _lang.SelectedItem?.ToString() ?? "java",
                    paths = new[] { _path.Text },
                    strictness = "smart",
                    maxResults = 200,
                };
            }

            var req = JsonSerializer.Serialize(payload);
            var (code, json) = Ag4Native.Search(req);
            if (code != 0)
            {
                using var err = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
                var msg =
                    err.RootElement.ValueKind == JsonValueKind.Object
                    && err.RootElement.TryGetProperty("error", out var e)
                        ? e.GetString()
                        : json;
                _status.Text = $"Error ({code}): {msg}";
                return;
            }

            var rows = MatchParser.Parse(json);
            foreach (var m in rows)
            {
                var line = (m.Range?.Start?.Line ?? 0) + 1;
                var col = (m.Range?.Start?.Column ?? 0) + 1;
                _results.Items.Add(new ListViewItem([m.File ?? "", line.ToString(), col.ToString(), m.Text ?? ""]));
            }

            _status.Text = $"{rows.Count} match(es)";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "ag4", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
