using System.Text.Json;

namespace Ag4Gui;

internal sealed class MainForm : Form
{
    private readonly ComboBox _lang = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _pattern = new() { Dock = DockStyle.Fill };
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    private readonly Button _search = new() { Text = "Search", AutoSize = true };
    private readonly ListView _results = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
    };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "ag4 sample GUI (C# / PInvoke)";
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

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

        _results.Columns.Add("File", 280);
        _results.Columns.Add("Line", 60);
        _results.Columns.Add("Col", 60);
        _results.Columns.Add("Text", 420);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 120,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(
            new Label
            {
                Text = "Language",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            },
            0,
            0
        );
        top.Controls.Add(_lang, 1, 0);
        top.Controls.Add(
            new Label
            {
                Text = "Pattern",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            },
            0,
            1
        );
        top.Controls.Add(_pattern, 1, 1);
        top.Controls.Add(
            new Label
            {
                Text = "Path",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            },
            0,
            2
        );
        top.Controls.Add(_path, 1, 2);
        top.Controls.Add(_search, 1, 3);

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(8, 4, 8, 4),
        };
        bottom.Controls.Add(_status);

        Controls.Add(_results);
        Controls.Add(bottom);
        Controls.Add(top);

        _search.Click += (_, _) => RunSearch();
        AcceptButton = _search;
    }

    private void RunSearch()
    {
        _results.Items.Clear();
        try
        {
            var req = JsonSerializer.Serialize(
                new
                {
                    pattern = _pattern.Text,
                    language = _lang.SelectedItem?.ToString() ?? "java",
                    paths = new[] { _path.Text },
                    strictness = "smart",
                    maxResults = 200,
                }
            );
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
