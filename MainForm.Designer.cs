using System;
using System.Windows.Forms;
using System.Drawing;

namespace SharePointCrawler;

partial class MainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    private TextBox _txtSiteUrl;
    private TextBox _txtLibraryUrl;
    private TextBox _txtUsername;
    private TextBox _txtPassword;
    private TextBox _txtDomain;
    private TextBox _txtIngestUrl;
    private Button _btnStart;
    private RichTextBox _currentPane;
    private RichTextBox _previousPane;
    private ProgressBar _progressBar;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        _txtSiteUrl = new TextBox();
        _txtLibraryUrl = new TextBox();
        _txtUsername = new TextBox();
        _txtPassword = new TextBox();
        _txtDomain = new TextBox();
        _txtIngestUrl = new TextBox();
        _btnStart = new Button();
        _currentPane = new RichTextBox();
        _previousPane = new RichTextBox();
        _progressBar = new ProgressBar();
        _metricsLabel = new Label();
        SuspendLayout();
        // 
        // _txtSiteUrl
        // 
        _txtSiteUrl.Location = new Point(12, 12);
        _txtSiteUrl.Name = "_txtSiteUrl";
        _txtSiteUrl.PlaceholderText = "Site URL";
        _txtSiteUrl.Size = new Size(282, 23);
        _txtSiteUrl.TabIndex = 1;
        // 
        // _txtLibraryUrl
        // 
        _txtLibraryUrl.Location = new Point(300, 12);
        _txtLibraryUrl.Name = "_txtLibraryUrl";
        _txtLibraryUrl.PlaceholderText = "Library Relative URL";
        _txtLibraryUrl.Size = new Size(280, 23);
        _txtLibraryUrl.TabIndex = 3;
        // 
        // _txtUsername
        // 
        _txtUsername.Location = new Point(114, 41);
        _txtUsername.Name = "_txtUsername";
        _txtUsername.PlaceholderText = "Username";
        _txtUsername.Size = new Size(180, 23);
        _txtUsername.TabIndex = 5;
        // 
        // _txtPassword
        // 
        _txtPassword.Location = new Point(300, 41);
        _txtPassword.Name = "_txtPassword";
        _txtPassword.PlaceholderText = "Password";
        _txtPassword.Size = new Size(280, 23);
        _txtPassword.TabIndex = 7;
        _txtPassword.UseSystemPasswordChar = true;
        // 
        // _txtDomain
        // 
        _txtDomain.Location = new Point(12, 41);
        _txtDomain.Name = "_txtDomain";
        _txtDomain.PlaceholderText = "Domain";
        _txtDomain.Size = new Size(96, 23);
        _txtDomain.TabIndex = 9;
        // 
        // _txtIngestUrl
        // 
        _txtIngestUrl.Location = new Point(12, 70);
        _txtIngestUrl.Name = "_txtIngestUrl";
        _txtIngestUrl.PlaceholderText = "Ingest Endpoint URL";
        _txtIngestUrl.Size = new Size(568, 23);
        _txtIngestUrl.TabIndex = 11;
        // 
        // _btnStart
        // 
        _btnStart.Location = new Point(500, 435);
        _btnStart.Name = "_btnStart";
        _btnStart.Size = new Size(80, 23);
        _btnStart.TabIndex = 12;
        _btnStart.Text = "Start";
        // 
        // _currentPane
        // 
        _currentPane.BackColor = Color.Black;
        _currentPane.Font = new Font("Consolas", 9F);
        _currentPane.ForeColor = Color.Lime;
        _currentPane.Location = new Point(12, 99);
        _currentPane.Name = "_currentPane";
        _currentPane.ReadOnly = true;
        _currentPane.Size = new Size(568, 150);
        _currentPane.TabIndex = 1;
        _currentPane.Text = "";
        // 
        // _previousPane
        // 
        _previousPane.BackColor = Color.Black;
        _previousPane.Font = new Font("Consolas", 9F);
        _previousPane.ForeColor = Color.Lime;
        _previousPane.Location = new Point(12, 255);
        _previousPane.Name = "_previousPane";
        _previousPane.ReadOnly = true;
        _previousPane.Size = new Size(568, 150);
        _previousPane.TabIndex = 3;
        _previousPane.Text = "";
        // 
        // _progressBar
        // 
        _progressBar.Location = new Point(12, 435);
        _progressBar.Name = "_progressBar";
        _progressBar.Size = new Size(412, 23);
        _progressBar.TabIndex = 4;
        // 
        // _metricsLabel
        // 
        _metricsLabel.BorderStyle = BorderStyle.FixedSingle;
        _metricsLabel.Location = new Point(12, 409);
        _metricsLabel.Name = "_metricsLabel";
        _metricsLabel.Size = new Size(568, 23);
        _metricsLabel.TabIndex = 13;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(593, 471);
        Controls.Add(_metricsLabel);
        Controls.Add(_currentPane);
        Controls.Add(_progressBar);
        Controls.Add(_previousPane);
        Controls.Add(_txtSiteUrl);
        Controls.Add(_txtLibraryUrl);
        Controls.Add(_btnStart);
        Controls.Add(_txtUsername);
        Controls.Add(_txtPassword);
        Controls.Add(_txtDomain);
        Controls.Add(_txtIngestUrl);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "MainForm";
        Text = "SharePoint Crawler";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label _metricsLabel;
}

