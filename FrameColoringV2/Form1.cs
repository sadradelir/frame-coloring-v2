using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = System.Drawing.Color;
using Image = SixLabors.ImageSharp.Image;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace FrameColoringV2;

public partial class Form1 : Form
{
    public List<int> selectedFileIndexes = new List<int>();

    private List<string> filePaths;
    public List<Image<Rgba32>> loadedImages = new List<Image<Rgba32>>();

    private PictureBox pictureBox;
    private ListView listView;
    public Rgba32 fillColor;
    Image<Rgba32> pictureBoxImage;
    
    // Cache for performance optimization
    private Image<Rgba32> cachedCheckerPattern;
    private Bitmap cachedDisplayBitmap;
    private int lastDisplayedWidth = -1;
    private int lastDisplayedHeight = -1;
    
    public string tool = "fill";
    public bool hold = false;
    private bool suppressSelectionUpdate = false;
    private Button brushButton;
    private Button fillButton;
    private CheckBox replaceFillCheckBox;
    bool onionSkin = true;
    public double zoom = .4f;
    public Rectangle crop = new Rectangle(0, 0, 0, 0);

    public Form1()
    {
        crop = new Rectangle(20, 40, 100, 400);

        // Enable KeyPreview to allow the form to capture key events before child controls
        this.KeyPreview = true;

        InitializeComponent();
        // background color to dark gray

        // #ccccc
        this.BackColor = Color.FromArgb(64, 64, 64); 
        
        this.Size = new Size(1925, 1050);
        pictureBox = new PictureBox();
        pictureBox.Location = new Point(0, 0);
        pictureBox.MouseDown += PictureBox_MouseClick;
        pictureBox.MouseMove += PictureBox_MouseMove;
        pictureBox.MouseUp += (sender, args) => { hold = false; };
        pictureBox.MouseEnter += (sender, args) => this.Focus();
        Controls.Add(pictureBox);

        this.MouseWheel += (sender, e) =>
        {
            if (selectedFileIndexes.Count != 1) return;
            int currentIndex = selectedFileIndexes[0];
            int newIndex = e.Delta < 0 ? currentIndex + 1 : currentIndex - 1;
            if (newIndex < 0 || newIndex >= listView.Items.Count) return;

            suppressSelectionUpdate = true;
            listView.BeginUpdate();
            listView.SelectedItems.Clear();
            listView.Items[newIndex].Selected = true;
            listView.Items[newIndex].EnsureVisible();
            listView.EndUpdate();
            suppressSelectionUpdate = false;
            UpdateDisplayingImagesFromListSelection();
        };
        
        // remove duplicate images button 
        var removeDuplicateButton = new Button();
        removeDuplicateButton.Location = new Point(0, 1100);
        removeDuplicateButton.Size = new Size(200, 50);
        removeDuplicateButton.Text = "Load Unique Images";
        removeDuplicateButton.Click += async (sender, args) =>
        {
            string folderPath = "./images";
            string processedFolderPath = "./processed";

            using var dialog = new ProgressDialog("Loading Unique Images");
            var progress = new Progress<(int current, int total, string message)>(p =>
                dialog.Report(p.current, p.total, p.message));

            var work = Task.Run(() =>
                FrameColoringHelper.CopyNonDuplicatesTo(folderPath, processedFolderPath, progress));

            dialog.Show(this);
            try
            {
                await work;
            }
            finally
            {
                dialog.Close();
            }

            Application.Restart();
        };
        Controls.Add(removeDuplicateButton);

        // select all images button
        var selectAllButton = new Button();
        selectAllButton.Location = new Point(210, 1100);
        selectAllButton.Size = new Size(150, 50);
        selectAllButton.Text = "Select All";
        selectAllButton.Click += (sender, args) =>
        {
            suppressSelectionUpdate = true;
            listView.BeginUpdate();
            foreach (ListViewItem item in listView.Items)
            {
                item.Selected = true;
            }
            listView.EndUpdate();
            suppressSelectionUpdate = false;
            UpdateDisplayingImagesFromListSelection();
        };
        Controls.Add(selectAllButton);

        fillColor = new Rgba32(0, 0, 0, 255);

        listView = new ListView();
        listView.Location = new Point(1550, 0);
        listView.Size = new Size(400, 1000);
        listView.View = View.List;
        listView.KeyPress += (sender, args) => { UpdateDisplayingImagesFromListSelection(); };
        listView.SelectedIndexChanged += (sender, args) =>
        {
            if (suppressSelectionUpdate) return;
            UpdateDisplayingImagesFromListSelection();
        };
        Controls.Add(listView);

        var opacityLabel = new Label();
        opacityLabel.Location = new Point(1300, 300);
        opacityLabel.Size = new Size(100, 25);
        opacityLabel.Text = "Opacity: 255";
        Controls.Add(opacityLabel);

        // create an slider for fill color alpha
        var trackBar = new TrackBar();
        trackBar.Location = new Point(1300, 250);
        trackBar.Size = new Size(100, 100);
        trackBar.Minimum = 0;
        trackBar.Maximum = 255;
        trackBar.Value = 255;
        trackBar.ValueChanged += (sender, args) =>
        {
            fillColor.A = (byte)trackBar.Value;
            opacityLabel.Text = "Opacity: " + trackBar.Value;
        };
        Controls.Add(trackBar);
        
        //create a checkbox for onion skin
        var onionSkinCheckBox = new CheckBox();
        onionSkinCheckBox.Location = new Point(1300, 100);
        onionSkinCheckBox.Size = new Size(100, 50);
        onionSkinCheckBox.Checked = true;
        onionSkinCheckBox.Text = "Onion Skin";
        onionSkinCheckBox.CheckedChanged += (sender, args) => { onionSkin = onionSkinCheckBox.Checked; };
        Controls.Add(onionSkinCheckBox);

        // create a save button which saves the selected images
        var saveButton = new Button();
        saveButton.Location = new Point(1300, 0);
        saveButton.Size = new Size(200, 50);
        saveButton.Text = "Save";
        saveButton.Click += (sender, args) =>
        {
            foreach (var index in selectedFileIndexes)
            {
                var image = loadedImages[index];
                using (var stream = File.OpenWrite(filePaths[index]))
                {
                    image.SaveAsPng(stream);
                }
            }
        };
        Controls.Add(saveButton);
        
        // create a save button which saves all images
        var saveAllButton = new Button();
        saveAllButton.Location = new Point(1300, 50);
        saveAllButton.Size = new Size(200, 50);
        saveAllButton.Text = "Save All";
        saveAllButton.Click += (sender, args) =>
        {
            for (var index = 0; index < filePaths.Count; index++)
            {
                var image = loadedImages[index];
                using (var stream = File.OpenWrite(filePaths[index]))
                {
                    image.SaveAsPng(stream);
                }
            }
        };
        Controls.Add(saveAllButton);
        
        // create a reload button which reloads the selected images
        var reloadButton = new Button();
        reloadButton.Location = new Point(1200, 850);
        reloadButton.Size = new Size(200, 50);
        reloadButton.Text = "Reload";
        reloadButton.Click += (sender, args) =>
        {
            // Reload selected images from disk
            foreach (var index in selectedFileIndexes)
            {
                loadedImages[index]?.Dispose();
                loadedImages[index] = Image.Load<Rgba32>(filePaths[index]);
            }
            
            // Refresh display
            UpdateDisplayImageFromAllSelected();
        };
        Controls.Add(reloadButton);
        
        // create a button to open file in photoshop
        var openButton = new Button();
        openButton.Location = new Point(1200, 900);
        openButton.Size = new Size(200, 50);
        openButton.Text = "Open in Photoshop";
        openButton.Click += (sender, args) =>
        {
            string photoshopPath = @"C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe";
            foreach (var index in selectedFileIndexes)
            {
                try
                {
                    var filePath = Path.GetFullPath(filePaths[index]);

                    using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        stream.Close();
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = photoshopPath,
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    Process.Start(processStartInfo);
                }
                catch (IOException e)
                {
                    MessageBox.Show("File is locked by another process. Please close the file and try again.", "Error");
                }
            }
        };
        Controls.Add(openButton);

        // create two buttons for brush and fill
        brushButton = new Button();
        brushButton.Location = new Point(1300, 200);
        brushButton.Size = new Size(100, 50);
        brushButton.Text = "Brush";
        brushButton.Click += (sender, args) =>
        {
            brushButton.BackColor = Color.DarkCyan;
            fillButton.BackColor = Color.DarkGray;
            tool = "brush";
        };
        Controls.Add(brushButton);
        
        fillButton = new Button();
        fillButton.Location = new Point(1300, 150);
        fillButton.Size = new Size(100, 50);
        fillButton.Text = "Fill";
        fillButton.Click += (sender, args) =>
        {
            brushButton.BackColor = Color.DarkGray;
            fillButton.BackColor = Color.DarkCyan;
            tool = "fill";
        };
        Controls.Add(fillButton);
        brushButton.BackColor = Color.DarkGray;
        fillButton.BackColor = Color.DarkCyan;
        
        // add a checkbox for replace fill
        replaceFillCheckBox = new CheckBox();
        replaceFillCheckBox.Location = new Point(1430, 150);
        replaceFillCheckBox.Size = new Size(150, 50);
        replaceFillCheckBox.Text = "Replace Fill";
        replaceFillCheckBox.Checked = false;
        Controls.Add(replaceFillCheckBox);
        
         
        // create empower button
        var empowerButton = new Button();
        empowerButton.Location = new Point(1200, 400);
        empowerButton.Size = new Size(100, 50);
        empowerButton.Text = "Empower";
        empowerButton.Click += (sender, args) =>
        {
            foreach (var fileIndex in selectedFileIndexes)
            {
                FrameColoringHelper.Empower(loadedImages[fileIndex]);
            }
            UpdateDisplayImageFromAllSelected();
        };
        Controls.Add(empowerButton);
        
        // create sdf button
        var sdfButton = new Button();
        sdfButton.Location = new Point(1200, 300);
        sdfButton.Size = new Size(100, 50);
        sdfButton.Text = "SDF";
        sdfButton.Click += (sender, args) =>
        {
            foreach (var fileIndex in selectedFileIndexes)
            {
                FrameColoringHelper.SDF(loadedImages[fileIndex]);
            }
            UpdateDisplayImageFromAllSelected();
        };
        Controls.Add(sdfButton);

        // create analyze button
        var analyze = new Button();
        analyze.Location = new Point(1200, 350);
        analyze.Size = new Size(100, 50);
        analyze.Text = "Analyze";
        analyze.Click += (sender, args) =>
        {
            if (selectedFileIndexes.Count == 0) return;

            if (selectedFileIndexes.Count == 1)
            {
                int count = FrameColoringHelper.Analyze(loadedImages[selectedFileIndexes[0]]);
                MessageBox.Show($"{count} pixels are not colored", "Analyze");
            }
            else
            {
                var lines = new List<string>();
                int total = 0;
                foreach (var fileIndex in selectedFileIndexes)
                {
                    int count = FrameColoringHelper.Analyze(loadedImages[fileIndex]);
                    total += count;
                    lines.Add($"{Path.GetFileName(filePaths[fileIndex])}: {count}");
                }
                lines.Add("");
                lines.Add($"Total: {total} pixels are not colored");
                MessageBox.Show(string.Join(Environment.NewLine, lines), "Analyze");
            }
        };
        Controls.Add(analyze);

        // crop tool
        var cropButton = new Button();
        cropButton.Location = new Point(1350, 350);
        cropButton.Size = new Size(100, 50);
        cropButton.Text = "crop";
        cropButton.Click += (sender, args) =>
        {
            if (tool == "crop")
            {
                // Apply crop to all selected images
                for (var index = 0; index < selectedFileIndexes.Count; index++)
                {
                    var fileIndex = selectedFileIndexes[index];
                    var oldImage = loadedImages[fileIndex];
                    loadedImages[fileIndex] = FrameColoringHelper.Crop(oldImage, crop.X, crop.Y, crop.Width, crop.Height);
                    oldImage.Dispose(); // Clean up old image
                }

                tool = "fill";
                cropButton.BackColor = Color.DarkGray;
                UpdateDisplayImageFromAllSelected();
            }
            else
            {
                tool = "crop";
                cropButton.BackColor = Color.IndianRed;
            }
        };
        Controls.Add(cropButton);
        
        var flipXButton = new Button();
        flipXButton.Location = new Point(1350, 450);
        flipXButton.Size = new Size(100, 50);
        flipXButton.Text = "Flip X";
        flipXButton.Click += (sender, args) =>
        {
            foreach (var fileIndex in selectedFileIndexes)
            {
                loadedImages[fileIndex].Mutate(x => x.Flip(FlipMode.Horizontal));
            }
            UpdateDisplayImageFromAllSelected();
        };
        Controls.Add(flipXButton);
        

        // Trim button (auto-crop to content bounds)
        var trimButton = new Button();
        trimButton.Location = new Point(1350, 400);
        trimButton.Size = new Size(100, 50);
        trimButton.Text = "Trim";
        trimButton.Click += (sender, args) =>
        {
            foreach (var fileIndex in selectedFileIndexes)
            {
                var oldImage = loadedImages[fileIndex];
                var trimBounds = FrameColoringHelper.GetTrimBounds(oldImage);
                
                if (trimBounds.Width > 0 && trimBounds.Height > 0)
                {
                    loadedImages[fileIndex] = FrameColoringHelper.Crop(oldImage, 
                        trimBounds.X, trimBounds.Y, trimBounds.Width, trimBounds.Height);
                    oldImage.Dispose();
                }
            }
            UpdateDisplayImageFromAllSelected();
        };
        Controls.Add(trimButton);

        string[] colorStrs = new string[]
        {
            "#0000ff/hair changable",
            "#844f69/leather",
            "#614a4a/hair dark",
            "#aa9c95/hair light",
            "#7a878f/metal",
            "#dae9f3/blade",
            "#503f36/dark wood",
            "#a07755/light wood",
            "#00ff00/TeamColor",
            "#ff0000/team color dark",
            "#ffff00/ExColor1",
            "#ff00ff/ExColor2",
            "#edcbba/skin",
            "#000000/feet",
            "#ffffff/shine"
        };

        for (int i = 0; i < colorStrs.Length; i++)
        {
            Button button = new Button();
            button.Location = new Point(1200 + 100 * (i % 3), 500 + 50 * (i / 3));
            button.Size = new Size(100, 50);
            button.Text = colorStrs[i].Split("/")[1];
            var color = Rgba32.ParseHex(colorStrs[i].Split("/")[0]);
            button.BackColor = Color.FromArgb(color.A, color.R, color.G, color.B);
            button.Click += (sender, args) =>
            {
                fillColor = color;
                var trackBarControl = Controls.OfType<TrackBar>().First();
                trackBarControl.Value = color.A;
            };
            Controls.Add(button);
        }

        // create zoom toggle buttons
        var zoomPlusButton = new Button();
        zoomPlusButton.Location = new Point(2050, 0);
        zoomPlusButton.Size = new Size(75, 50);
        zoomPlusButton.Text = "Zoom+";
        zoomPlusButton.Click += (sender, args) =>
        {
            zoom += .1;
            DisplayImage();
        };
        Controls.Add(zoomPlusButton);

        var zoomMinusButton = new Button();
        zoomMinusButton.Location = new Point(2130, 0);
        zoomMinusButton.Size = new Size(75, 50);
        zoomMinusButton.Text = "Zoom-";
        zoomMinusButton.Click += (sender, args) =>
        {
            zoom -= .1;
            if (zoom < 0.1) zoom = 0.1;
            DisplayImage();
        };
        Controls.Add(zoomMinusButton);

        // Pan controls
        var panUp = new Button();
        var panDown = new Button();
        var panRight = new Button();
        var panLeft = new Button();
        panUp.Location = new Point(2090, 50);
        panDown.Location = new Point(2090, 150);
        panLeft.Location = new Point(2015, 100);
        panRight.Location = new Point(2165, 100);
        panUp.Size = new Size(75, 50);
        panDown.Size = new Size(75, 50);
        panLeft.Size = new Size(75, 50);
        panRight.Size = new Size(75, 50);
        panUp.Text = "Up";
        panDown.Text = "Down";
        panLeft.Text = "Left";
        panRight.Text = "Right";
        panUp.Click += (sender, args) => { pictureBox.Top +=     140; };
        panDown.Click += (sender, args) => { pictureBox.Top -=   140; };
        panLeft.Click += (sender, args) => { pictureBox.Left +=  140; };
        panRight.Click += (sender, args) => { pictureBox.Left -= 140; };
        Controls.Add(panUp);
        Controls.Add(panDown);
        Controls.Add(panRight);
        Controls.Add(panLeft);

        string processedFolderPath = "./processed";
        filePaths = Directory.GetFiles(processedFolderPath).ToList();
        filePaths = filePaths.OrderBy(x => x).ToList();
        listView.Items.AddRange(filePaths.Select(
            x => new ListViewItem(x.Split("/")[^1].Split("\\")[^1])
        ).ToArray());
        loadedImages = filePaths.Select(filePath =>
        {
            using (var stream = File.OpenRead(filePath))
            {
                return Image.Load<Rgba32>(stream);
            }
        }).ToList();

        this.KeyDown += MainForm_KeyDown;
        pictureBox.SendToBack();
    }

    private void UpdateDisplayImageFromAllSelected()
    {
        if (selectedFileIndexes.Count == 0) return;
        
        // Build composite image efficiently
        BuildCompositeImage();
        DisplayImage();
    }

    private void BuildCompositeImage()
    {
        if (selectedFileIndexes.Count == 0) return;

        var primaryIndex = selectedFileIndexes[0];
        var primaryImage = loadedImages[primaryIndex];
        
        // Create or reuse checker pattern
        if (cachedCheckerPattern == null || 
            cachedCheckerPattern.Width != primaryImage.Width || 
            cachedCheckerPattern.Height != primaryImage.Height)
        {
            cachedCheckerPattern?.Dispose();
            cachedCheckerPattern = CreateCheckerPattern(primaryImage.Width, primaryImage.Height);
        }

        // Create composite on checker background
        pictureBoxImage?.Dispose();
        pictureBoxImage = cachedCheckerPattern.Clone();
        
        // Draw primary image
        pictureBoxImage.Mutate(x => x.DrawImage(primaryImage, 1f));
        
        // Add onion skin layers if enabled
        if (onionSkin && selectedFileIndexes.Count > 1)
        {
            for (int i = 1; i < selectedFileIndexes.Count; i++)
            {
                var overlayImage = loadedImages[selectedFileIndexes[i]];
                pictureBoxImage.Mutate(x => x.DrawImage(overlayImage, 0.4f));
            }
        }

        // Draw crop rectangle if in crop mode
        if (tool == "crop")
        {
            var red = new Rgba32(255, 0, 0, 255);
            FrameColoringHelper.DrawRectangle(pictureBoxImage, crop.X, crop.Y, crop.Width, crop.Height, red);
        }
    }

    private Image<Rgba32> CreateCheckerPattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        var lightGray = new Rgba32(153, 153, 153, 255);
        var darkGray = new Rgba32(102, 102, 102, 255);
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                image[x, y] = (x / 10) % 2 == (y / 10) % 2 ? lightGray : darkGray;
            }
        }
        return image;
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (hold)
        {
            PictureBox_MouseClick(sender, e);
        }
    }

    private void UpdateDisplayingImagesFromListSelection()
    {
        selectedFileIndexes = listView.SelectedIndices.Cast<int>().ToList();
        UpdateDisplayImageFromAllSelected();
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // Add keyboard shortcuts here if needed
    }

    private async void PictureBox_MouseClick(object? sender, MouseEventArgs e)
    {
        if (selectedFileIndexes.Count == 0) return;
        
        // Calculate mouse to image coordinates
        var (imageX, imageY) = GetImageCoordinates(e.X, e.Y);
        
        if (imageX >= 0 && imageX < pictureBoxImage.Width && imageY >= 0 && imageY < pictureBoxImage.Height)
        {
            switch (tool)
            {
                case "brush":
                    await BrushSelectedImagesOnPoint(imageX, imageY);
                    hold = true;
                    break;
                case "fill":
                    if (replaceFillCheckBox.Checked)
                    {
                        await ReplaceFillSelectedImagesOnPoint(imageX, imageY);
                    }
                    else
                    {
                        await FillSelectedImagesOnPoint(imageX, imageY);
                    }
                    break;
                case "crop":
                    UpdateCropRect(imageX, imageY);
                    hold = true;
                    break;
            }
        }
        else
        {
            MessageBox.Show("Click outside the image!", "Warning");
        }
    }

    private (int x, int y) GetImageCoordinates(int mouseX, int mouseY)
    {
        // Calculate the scale factors
        float scaleX = (float)pictureBoxImage.Width / pictureBox.ClientSize.Width;
        float scaleY = (float)pictureBoxImage.Height / pictureBox.ClientSize.Height;

        // Adjust for the PictureBox.SizeMode (assumes Zoom)
        var clientRect = pictureBox.ClientRectangle;
        float imageAspect = (float)pictureBoxImage.Width / pictureBoxImage.Height;
        float boxAspect = (float)clientRect.Width / clientRect.Height;

        int offsetX = 0, offsetY = 0;
        if (boxAspect > imageAspect)
        {
            offsetX = (clientRect.Width - (int)(clientRect.Height * imageAspect)) / 2;
        }
        else
        {
            offsetY = (clientRect.Height - (int)(clientRect.Width / imageAspect)) / 2;
        }

        int imageX = (int)((mouseX - offsetX) * scaleX);
        int imageY = (int)((mouseY - offsetY) * scaleY);
        
        return (imageX, imageY);
    }

    private void UpdateCropRect(int imageX, int imageY)
    {
        var corners = new Point[]
        {
            new Point(crop.X, crop.Y),
            new Point(crop.X + crop.Width, crop.Y + crop.Height)
        };
        
        int closestCornerIndex = 0;
        int minDistance = int.MaxValue;
        
        for (int i = 0; i < 2; i++)
        {
            int distance = (corners[i].X - imageX) * (corners[i].X - imageX) +
                          (corners[i].Y - imageY) * (corners[i].Y - imageY);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestCornerIndex = i;
            }
        }

        corners[closestCornerIndex] = new Point(imageX, imageY);
        
        crop = new Rectangle(
            Math.Min(corners[0].X, corners[1].X),
            Math.Min(corners[0].Y, corners[1].Y),
            Math.Abs(corners[0].X - corners[1].X),
            Math.Abs(corners[0].Y - corners[1].Y)
        );

        BuildCompositeImage();
        DisplayImage();
    }

    private async Task BrushSelectedImagesOnPoint(int imageX, int imageY)
    {
        foreach (var fileIndex in selectedFileIndexes)
        {
            FrameColoringHelper.Brush(loadedImages[fileIndex], imageX, imageY, fillColor, 4);
        }
        
        // Update display once after all operations
        BuildCompositeImage();
        DisplayImage();
    }

    private async Task FillSelectedImagesOnPoint(int imageX, int imageY)
    {
        Console.WriteLine($"Filling at {imageX}, {imageY} for {selectedFileIndexes.Count} files");
        
        foreach (var fileIndex in selectedFileIndexes)
        {
            var visited = new HashSet<(int, int)>();
            FrameColoringHelper.Fill(loadedImages[fileIndex], imageX, imageY, fillColor, 2, visited);
        }
        
        // Update display once after all operations
        BuildCompositeImage();
        DisplayImage();
    }
    
    private async Task ReplaceFillSelectedImagesOnPoint(int imageX, int imageY)
    {
        Console.WriteLine($"Filling at {imageX}, {imageY} for {selectedFileIndexes.Count} files");
        
        foreach (var fileIndex in selectedFileIndexes)
        {
            var visited = new HashSet<(int, int)>();
            FrameColoringHelper.ReplaceFill(loadedImages[fileIndex], imageX, imageY, fillColor, visited);
        }
        
        // Update display once after all operations
        BuildCompositeImage();
        DisplayImage();
    }


    private void DisplayImageOnPictureBox(int imageIndex, bool additive = false)
    {
        // This method is now replaced by BuildCompositeImage + DisplayImage
        // Keeping for backward compatibility but redirecting to new optimized method
        if (!additive)
        {
            BuildCompositeImage();
        }
        DisplayImage();
    }

    private void DisplayImage()
    {
        if (pictureBoxImage == null) return;
        
        int targetWidth = (int)(pictureBoxImage.Width * zoom);
        int targetHeight = (int)(pictureBoxImage.Height * zoom);
        
        // Only recreate bitmap if size changed
        if (cachedDisplayBitmap == null || 
            cachedDisplayBitmap.Width != targetWidth || 
            cachedDisplayBitmap.Height != targetHeight)
        {
            cachedDisplayBitmap?.Dispose();
            
            // Use more efficient conversion
            cachedDisplayBitmap = ConvertToBitmapEfficiently(pictureBoxImage, targetWidth, targetHeight);
        }
        else
        {
            // Update existing bitmap
            UpdateBitmapFromImage(cachedDisplayBitmap, pictureBoxImage, targetWidth, targetHeight);
        }
        
        pictureBox.Size = new Size(targetWidth, targetHeight);
        
        // Dispose old image and set new one
        var oldImage = pictureBox.Image;
        pictureBox.Image = new Bitmap(cachedDisplayBitmap);
        oldImage?.Dispose();
    }

    private Bitmap ConvertToBitmapEfficiently(Image<Rgba32> sourceImage, int targetWidth, int targetHeight)
    {
        var bitmap = new Bitmap(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        
        // Use LockBits for faster pixel access
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, targetWidth, targetHeight),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        unsafe
        {
            byte* ptr = (byte*)bitmapData.Scan0;
            int stride = bitmapData.Stride;
            
            float scaleX = (float)sourceImage.Width / targetWidth;
            float scaleY = (float)sourceImage.Height / targetHeight;
            
            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    int srcX = (int)(x * scaleX);
                    int srcY = (int)(y * scaleY);
                    
                    if (srcX < sourceImage.Width && srcY < sourceImage.Height)
                    {
                        var pixel = sourceImage[srcX, srcY];
                        int offset = y * stride + x * 4;
                        
                        ptr[offset] = pixel.B;     // Blue
                        ptr[offset + 1] = pixel.G; // Green
                        ptr[offset + 2] = pixel.R; // Red
                        ptr[offset + 3] = pixel.A; // Alpha
                    }
                }
            }
        }
        
        bitmap.UnlockBits(bitmapData);
        return bitmap;
    }

    private void UpdateBitmapFromImage(Bitmap bitmap, Image<Rgba32> sourceImage, int targetWidth, int targetHeight)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, targetWidth, targetHeight),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        unsafe
        {
            byte* ptr = (byte*)bitmapData.Scan0;
            int stride = bitmapData.Stride;
            
            float scaleX = (float)sourceImage.Width / targetWidth;
            float scaleY = (float)sourceImage.Height / targetHeight;
            
            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    int srcX = (int)(x * scaleX);
                    int srcY = (int)(y * scaleY);
                    
                    if (srcX < sourceImage.Width && srcY < sourceImage.Height)
                    {
                        var pixel = sourceImage[srcX, srcY];
                        int offset = y * stride + x * 4;
                        
                        ptr[offset] = pixel.B;
                        ptr[offset + 1] = pixel.G;
                        ptr[offset + 2] = pixel.R;
                        ptr[offset + 3] = pixel.A;
                    }
                }
            }
        }
        
        bitmap.UnlockBits(bitmapData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Clean up resources
            cachedCheckerPattern?.Dispose();
            cachedDisplayBitmap?.Dispose();
            pictureBoxImage?.Dispose();
            
            foreach (var image in loadedImages)
            {
                image?.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}