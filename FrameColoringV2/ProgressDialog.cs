namespace FrameColoringV2;

public class ProgressDialog : Form
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
        ClientSize = new Size(420, 110);

        statusLabel = new Label
        {
            Location = new Point(15, 15),
            Size = new Size(390, 40),
            Text = "Starting...",
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(statusLabel);

        progressBar = new ProgressBar
        {
            Location = new Point(15, 60),
            Size = new Size(390, 30),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
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
            progressBar.Value = Math.Min(current, total);
        }
        statusLabel.Text = message;
    }
}
