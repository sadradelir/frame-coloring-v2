using FrameColoringV2.UI;

namespace FrameColoringV2;

internal static class Program
{
    /// <summary>The main entry point. Paths passed on the command line are opened at start up.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new MainForm(args));
    }
}
