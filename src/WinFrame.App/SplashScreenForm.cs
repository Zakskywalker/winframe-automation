using System.Drawing;
using System.Windows.Forms;

namespace WinFrame.App;

/// <summary>Small branded splash screen displaying the Hand of Glory artwork.</summary>
public sealed class SplashScreenForm : Form
{
    public SplashScreenForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(360, 480);
        BackColor = Color.FromArgb(15, 13, 18);
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;

        var pic = new PictureBox
        {
            Name = "picSplashArtwork",
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(15, 13, 18)
        };

        string imgPath = Path.Combine(AppContext.BaseDirectory, "assets", "splash.jpg");
        if (!File.Exists(imgPath))
        {
            string? walk = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(walk) && !File.Exists(Path.Combine(walk, "WinFrame.sln")))
                walk = Path.GetDirectoryName(walk);
            if (!string.IsNullOrEmpty(walk))
                imgPath = Path.Combine(walk, "assets", "splash.jpg");
        }
        if (File.Exists(imgPath))
        {
            try
            {
                using var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var src = Image.FromStream(fs);
                pic.Image = new Bitmap(src);
            }
            catch { }
        }

        var footer = new Panel
        {
            Name = "pnlSplashFooter",
            Dock = DockStyle.Bottom,
            Height = 76,
            BackColor = Color.FromArgb(24, 18, 28)
        };

        var lblBrand = new Label
        {
            Name = "lblSplashBrand",
            Text = "HAND OF GLORY GAMES AND SOFTWARE",
            ForeColor = Color.FromArgb(245, 158, 11),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.BottomCenter
        };

        var lblTitle = new Label
        {
            Name = "lblSplashTitle",
            Text = "WinFrame Automation v1.0  •  Zak Millikin (Zakskywalker M_A.P.)\nArt: Perchance AI (Cursed)  •  AI: Antigravity (Claude Opus & Gemini)",
            ForeColor = Color.FromArgb(230, 237, 243),
            Font = new Font("Segoe UI", 8.2f, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter
        };

        footer.Controls.Add(lblTitle);
        footer.Controls.Add(lblBrand);
        Controls.Add(pic);
        Controls.Add(footer);
    }
}
