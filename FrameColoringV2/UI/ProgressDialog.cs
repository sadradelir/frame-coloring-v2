namespace FrameColoringV2.UI;

public sealed class ProgressDialog : Form
{
    private readonly ProgressBar progressBar;
    private readonly Label statusLabel;

    public ProgressDialog(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        ClientSize = new Size(440, 110);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        statusLabel = new Label
        {
            Location = new Point(16, 18),
            Size = new Size(408, 40),
            Text = "Starting…",
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(statusLabel);

        progressBar = new ProgressBar
        {
            Location = new Point(16, 62),
            Size = new Size(408, 22),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            ForeColor = Theme.Accent
        };
        Controls.Add(progressBar);
    }

    public void Report(int current, int total, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Report(current, total, message)));
            return;
        }

        if (total > 0)
        {
            progressBar.Maximum = total;
            progressBar.Value = Math.Clamp(current, 0, total);
        }

        statusLabel.Text = message;
    }
}
