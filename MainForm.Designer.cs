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
        private Label _metricsLabel;
        private ProgressBar _progressBar;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel outputPanel;
        private TableLayoutPanel outputLayout;

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
            this.components = new System.ComponentModel.Container();
            this._txtSiteUrl = new System.Windows.Forms.TextBox();
            this._txtLibraryUrl = new System.Windows.Forms.TextBox();
            this._txtUsername = new System.Windows.Forms.TextBox();
            this._txtPassword = new System.Windows.Forms.TextBox();
            this._txtDomain = new System.Windows.Forms.TextBox();
            this._txtIngestUrl = new System.Windows.Forms.TextBox();
            this._btnStart = new System.Windows.Forms.Button();
            this._currentPane = new System.Windows.Forms.RichTextBox();
            this._previousPane = new System.Windows.Forms.RichTextBox();
            this._metricsLabel = new System.Windows.Forms.Label();
            this._progressBar = new System.Windows.Forms.ProgressBar();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.outputPanel = new System.Windows.Forms.Panel();
            this.outputLayout = new System.Windows.Forms.TableLayoutPanel();
            this.SuspendLayout();
            // 
            // txt boxes
            // 
            this._txtSiteUrl.PlaceholderText = "Site URL";
            this._txtSiteUrl.Width = 300;
            this._txtLibraryUrl.PlaceholderText = "Library Relative URL";
            this._txtLibraryUrl.Width = 300;
            this._txtUsername.PlaceholderText = "Username";
            this._txtUsername.Width = 150;
            this._txtPassword.PlaceholderText = "Password";
            this._txtPassword.UseSystemPasswordChar = true;
            this._txtPassword.Width = 150;
            this._txtDomain.PlaceholderText = "Domain";
            this._txtDomain.Width = 150;
            this._txtIngestUrl.PlaceholderText = "Ingest Endpoint URL";
            this._txtIngestUrl.Width = 300;
            // 
            // _btnStart
            // 
            this._btnStart.Text = "Start";
            this._btnStart.Width = 80;
            // 
            // _currentPane
            // 
            this._currentPane.ReadOnly = true;
            this._currentPane.Width = 700;
            this._currentPane.Height = 150;
            this._currentPane.BackColor = Color.Black;
            this._currentPane.ForeColor = Color.Lime;
            this._currentPane.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            // 
            // _previousPane
            // 
            this._previousPane.ReadOnly = true;
            this._previousPane.Width = 700;
            this._previousPane.Height = 150;
            this._previousPane.BackColor = Color.Black;
            this._previousPane.ForeColor = Color.Lime;
            this._previousPane.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            // 
            // _metricsLabel
            // 
            this._metricsLabel.AutoSize = true;
            // 
            // _progressBar
            // 
            this._progressBar.Width = 700;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.tableLayoutPanel1.RowCount = 8;
            this.tableLayoutPanel1.Dock = DockStyle.Fill;
            // add controls
            this.tableLayoutPanel1.Controls.Add(new Label { Text = "Site URL", AutoSize = true }, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this._txtSiteUrl, 1, 0);
            this.tableLayoutPanel1.Controls.Add(new Label { Text = "Library URL", AutoSize = true }, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this._txtLibraryUrl, 1, 1);
            this.tableLayoutPanel1.Controls.Add(new Label { Text = "Username", AutoSize = true }, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this._txtUsername, 1, 2);
            this.tableLayoutPanel1.Controls.Add(new Label { Text = "Password", AutoSize = true }, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this._txtPassword, 1, 3);
            this.tableLayoutPanel1.Controls.Add(new Label { Text = "Domain", AutoSize = true }, 0, 4);
            this.tableLayoutPanel1.Controls.Add(this._txtDomain, 1, 4);
            this.tableLayoutPanel1.Controls.Add(new Label { Text = "Ingest URL", AutoSize = true }, 0, 5);
            this.tableLayoutPanel1.Controls.Add(this._txtIngestUrl, 1, 5);
            this.tableLayoutPanel1.Controls.Add(this._btnStart, 1, 6);
            // 
            // outputPanel
            // 
            this.outputPanel.Dock = DockStyle.Fill;
            this.tableLayoutPanel1.Controls.Add(this.outputPanel, 0, 7);
            this.tableLayoutPanel1.SetColumnSpan(this.outputPanel, 2);
            // 
            // outputLayout
            // 
            this.outputLayout.ColumnCount = 1;
            this.outputLayout.RowCount = 6;
            this.outputLayout.Dock = DockStyle.Fill;
            this.outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.outputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.outputLayout.Controls.Add(new Label { Text = "Current Document", AutoSize = true, ForeColor = Color.Blue }, 0, 0);
            this.outputLayout.Controls.Add(this._currentPane, 0, 1);
            this.outputLayout.Controls.Add(new Label { Text = "Previous Document", AutoSize = true }, 0, 2);
            this.outputLayout.Controls.Add(this._previousPane, 0, 3);
            this.outputLayout.Controls.Add(this._progressBar, 0, 4);
            this.outputLayout.Controls.Add(this._metricsLabel, 0, 5);
            this.outputPanel.Controls.Add(this.outputLayout);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(750, 650);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Text = "SharePoint Crawler";
            this.ResumeLayout(false);
        }

        #endregion
    }
}
