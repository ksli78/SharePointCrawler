using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SharePointCrawler;

public partial class MainForm : Form
{
    private TextBox _txtSiteUrl = new() { PlaceholderText = "Site URL", Width = 300 };
    private TextBox _txtLibraryUrl = new() { PlaceholderText = "Library Relative URL", Width = 300 };
    private TextBox _txtUsername = new() { PlaceholderText = "Username", Width = 150 };
    private TextBox _txtPassword = new() { PlaceholderText = "Password", UseSystemPasswordChar = true, Width = 150 };
    private TextBox _txtDomain = new() { PlaceholderText = "Domain", Width = 150 };
    private TextBox _txtIngestUrl = new() { PlaceholderText = "Ingest URL", Width = 150 };
    private Button _btnStart = new() { Text = "Start", Width = 80 };

    private RichTextBox _currentPane = new() { ReadOnly = true, Width = 700, Height = 150 };
    private RichTextBox _previousPane = new() { ReadOnly = true, Width = 700, Height = 150 };
    private Label _metricsLabel = new() { AutoSize = true };
    private ProgressBar _progressBar = new() { Width = 700 };

    private SharePointClient? _client;

    public MainForm()
    {
        Text = "SharePoint Crawler";
        Width = 750;
        Height = 650;
        InitializeComponent();
        //var table = new TableLayoutPanel
        //{
        //    Dock = DockStyle.Fill,
        //    RowCount = 7,
        //    ColumnCount = 2
        //};
        //table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        //table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        //Controls.Add(table);

        //table.Controls.Add(new Label { Text = "Site URL", AutoSize = true }, 0, 0);
        //table.Controls.Add(_txtSiteUrl, 1, 0);
        //table.Controls.Add(new Label { Text = "Library URL", AutoSize = true }, 0, 1);
        //table.Controls.Add(_txtLibraryUrl, 1, 1);
        //table.Controls.Add(new Label { Text = "Username", AutoSize = true }, 0, 2);
        //table.Controls.Add(_txtUsername, 1, 2);
        //table.Controls.Add(new Label { Text = "Password", AutoSize = true }, 0, 3);
        //table.Controls.Add(_txtPassword, 1, 3);
        //table.Controls.Add(new Label { Text = "Domain", AutoSize = true }, 0, 4);
        //table.Controls.Add(_txtDomain, 1, 4);
        //table.Controls.Add(_btnStart, 1, 5);

        //var outputPanel = new Panel { Dock = DockStyle.Fill };
        //table.Controls.Add(outputPanel, 0, 6);
        //table.SetColumnSpan(outputPanel, 2);

        //var outputLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        //outputPanel.Controls.Add(outputLayout);
        //outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //outputLayout.Controls.Add(new Label { Text = "Current Document", AutoSize = true, ForeColor = Color.Blue }, 0, 0);
        //outputLayout.Controls.Add(_currentPane, 0, 1);
        //outputLayout.Controls.Add(new Label { Text = "Previous Document", AutoSize = true }, 0, 2);
        //outputLayout.Controls.Add(_previousPane, 0, 3);
        //outputLayout.Controls.Add(_progressBar, 0, 4);
        //outputLayout.Controls.Add(_metricsLabel, 0, 5);

        _btnStart.Click += BtnStart_Click;
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        _btnStart.Enabled = false;
        var credential = new NetworkCredential(_txtUsername.Text, _txtPassword.Text, _txtDomain.Text);
        _client = new SharePointClient(_txtSiteUrl.Text, credential, new HashSet<string>(), 350, 80, "docs_v2");
        var libraryUrl = $"{_txtSiteUrl.Text}/_api/web/GetFolderByServerRelativeUrl('{_txtLibraryUrl.Text}')?$expand=Folders,Files";
        int total = await _client.CountDocumentsAsync(libraryUrl);
        ConsoleWindow.Initialize(this, total);

        await foreach (var doc in _client.GetDocumentsAsync(libraryUrl))
        {
            // Updates handled by ConsoleWindow
        }
        _btnStart.Enabled = true;
    }

    // Methods called by ConsoleWindow
    public void UpdateCurrentPane(List<(string Text, ConsoleColor Color)> lines)
    {
        _currentPane.Invoke(() =>
        {
            _currentPane.Clear();
            foreach (var line in lines)
            {
                AppendLine(_currentPane, line.Text, line.Color);
            }
        });
    }

    public void UpdatePreviousPane(List<(string Text, ConsoleColor Color)> lines)
    {
        _previousPane.Invoke(() =>
        {
            _previousPane.Clear();
            foreach (var line in lines)
            {
                AppendLine(_previousPane, line.Text, line.Color);
            }
        });
    }

    public void UpdateMetrics(int processedCount, TimeSpan totalTime)
    {
        double avgSeconds = processedCount > 0 ? totalTime.TotalSeconds / processedCount : 0;
        double avgMinutes = avgSeconds / 60.0;
        _metricsLabel.Invoke(() =>
            _metricsLabel.Text = $"Processed: {processedCount}  Avg Time: {avgSeconds:F1}s ({avgMinutes:F1}m)");
    }

    public void UpdateProgress(int value)
    {
        _progressBar.Invoke(() =>
        {
            if (value <= _progressBar.Maximum)
                _progressBar.Value = value;
        });
    }

    public void SetProgressMaximum(int total)
    {
        _progressBar.Invoke(() =>
        {
            _progressBar.Minimum = 0;
            _progressBar.Maximum = total <= 0 ? 1 : total;
            _progressBar.Value = 0;
        });
    }

    private static void AppendLine(RichTextBox box, string text, ConsoleColor color)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionColor = ToColor(color);
        box.AppendText(text + Environment.NewLine);
        box.SelectionColor = box.ForeColor;
    }

    private static Color ToColor(ConsoleColor color) => color switch
    {
        ConsoleColor.Red => Color.Red,
        ConsoleColor.Green => Color.Green,
        _ => Color.White
    };

    private void _txtSiteUrl_TextChanged(object sender, EventArgs e)
    {

    }
}
