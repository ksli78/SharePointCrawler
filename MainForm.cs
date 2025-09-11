using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SharePointCrawler;

public partial class MainForm : Form
{
    private SharePointClient? _client;

    public MainForm()
    {
        InitializeComponent();
        _btnStart.Click += BtnStart_Click;
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        _btnStart.Enabled = false;
        var credential = new NetworkCredential(_txtUsername.Text, _txtPassword.Text, _txtDomain.Text);
        _client = new SharePointClient(_txtSiteUrl.Text, credential, new HashSet<string>(), 350, 80, "docs_v2", _txtIngestUrl.Text);
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
        ConsoleColor.Green => Color.Lime,
        _ => Color.Lime
    };
}
