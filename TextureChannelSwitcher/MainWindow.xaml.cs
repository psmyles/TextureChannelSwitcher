using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using ImageMagick;

namespace TextureChannelSwitcher
{
    public partial class MainWindow : FluentWindow
    {
        private CancellationTokenSource? _cts;
        private bool _isProcessing = false;
        private bool _isInitialized = false; 
        
        private readonly HashSet<string> _validExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".bmp", ".exr"
        };

        public MainWindow()
        {
            InitializeComponent();
            
            ApplicationThemeManager.Apply(
                ApplicationTheme.Dark, 
                WindowBackdropType.Mica, 
                true 
            );

            _isInitialized = true;
        }

        // --- Event Handlers ---

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new();
            
            if (dialog.ShowDialog() == true)
            {
                TxtSourcePath.Text = dialog.FolderName;
                GeneratePreviewList();
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new();

            if (dialog.ShowDialog() == true)
            {
                TxtOutputPath.Text = dialog.FolderName;
                GeneratePreviewList();
            }
        }

        private void ToggleOutput_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            bool useSource = ChkUseSourceAsOutput.IsChecked == true;

            if (TxtOutputPath != null)
            {
                TxtOutputPath.IsEnabled = !useSource;
                BtnBrowseOutput.IsEnabled = !useSource;
                GeneratePreviewList();
            }
        }

        private void Filter_TextChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            GeneratePreviewList();
        }

        private void CmbAlphaSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            bool isNone = CmbAlphaSource.SelectedIndex == 0;

            if (CmbAlphaInvert != null)
            {
                CmbAlphaInvert.IsEnabled = !isNone;
            }

            UpdateChannelUI();
        }

        private void ChannelInvert_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            UpdateChannelUI();
        }

        private void UpdateChannelUI()
        {
            void UpdatePair(System.Windows.Controls.ComboBox? invert, System.Windows.Controls.ComboBox? source)
            {
                if (invert == null || source == null)
                {
                    return;
                }

                if (source == CmbAlphaSource && source.SelectedIndex == 0)
                {
                    source.IsEnabled = true;
                    return;
                }

                bool isFixedColor = invert.SelectedIndex == 2 || invert.SelectedIndex == 3;
                source.IsEnabled = !isFixedColor;
            }

            UpdatePair(CmbRedInvert, CmbRedSource);
            UpdatePair(CmbGreenInvert, CmbGreenSource);
            UpdatePair(CmbBlueInvert, CmbBlueSource);
            UpdatePair(CmbAlphaInvert, CmbAlphaSource);
        }
        
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => GeneratePreviewList();

        // --- Core Logic ---

        public class PreviewItem
        {
            public required string SourceName { get; set; }
            public required string TargetName { get; set; }
            public required string SourcePath { get; set; } 
            public required string TargetPath { get; set; } 
        }

        public class ProcessSettings
        {
            public int RedSource { get; set; }
            public int RedInvert { get; set; }
            public int GreenSource { get; set; }
            public int GreenInvert { get; set; }
            public int BlueSource { get; set; }
            public int BlueInvert { get; set; }
            public int AlphaSource { get; set; }
            public int AlphaInvert { get; set; }
        }

        private void GeneratePreviewList()
        {
            if (!_isInitialized || TxtSourcePath == null)
            {
                return;
            }

            string sourcePath = TxtSourcePath.Text;

            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
            {
                if (LstPreview != null) 
                {
                    LstPreview.ItemsSource = null;
                    LstPreview.Visibility = Visibility.Collapsed;

                    if (TxtPreviewPlaceholder != null)
                    {
                        TxtPreviewPlaceholder.Visibility = Visibility.Visible;
                    }
                }

                return;
            }

            List<PreviewItem> items = GetProcessTasks();

            if (LstPreview != null)
            {
                LstPreview.ItemsSource = items;
                
                if (items.Count > 0)
                {
                    LstPreview.Visibility = Visibility.Visible;

                    if (TxtPreviewPlaceholder != null)
                    {
                        TxtPreviewPlaceholder.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    LstPreview.Visibility = Visibility.Collapsed;

                    if (TxtPreviewPlaceholder != null)
                    {
                        TxtPreviewPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
            
            UpdateStatus($"Found {items.Count} matching images.");
        }

        private List<PreviewItem> GetProcessTasks()
        {
            List<PreviewItem> list = new();
            
            if (TxtSourcePath == null)
            {
                return list;
            }

            string sourcePath = TxtSourcePath.Text;

            if (!Directory.Exists(sourcePath))
            {
                return list;
            }

            string destFolder = (ChkUseSourceAsOutput.IsChecked == true) ? sourcePath : TxtOutputPath.Text;

            if (string.IsNullOrEmpty(destFolder))
            {
                destFolder = sourcePath;
            }

            string srcPre = TxtSrcPrefix.Text;
            string srcSuf = TxtSrcSuffix.Text;
            string genPre = TxtGenPrefix.Text;
            string genSuf = TxtGenSuffix.Text;
            
            if (string.IsNullOrEmpty(srcPre) && string.IsNullOrEmpty(srcSuf))
            {
                return list;
            }

            string ext = "png";

            if (CmbFormat != null && CmbFormat.SelectedItem is ComboBoxItem cbi)
            {
                ext = cbi.Content.ToString().ToLower();
            }

            try
            {
                string[] files = Directory.GetFiles(sourcePath);

                foreach (string file in files)
                {
                    string fileExt = System.IO.Path.GetExtension(file);

                    if (!_validExtensions.Contains(fileExt))
                    {
                        continue;
                    }

                    string filenameNoExt = System.IO.Path.GetFileNameWithoutExtension(file);
                    
                    if (!filenameNoExt.StartsWith(srcPre, StringComparison.OrdinalIgnoreCase) || 
                        !filenameNoExt.EndsWith(srcSuf, StringComparison.OrdinalIgnoreCase)) 
                    {
                        continue;
                    }

                    string filename = System.IO.Path.GetFileName(file);
                    string cleanName = filenameNoExt;
                    
                    if (!string.IsNullOrEmpty(srcPre))
                    {
                        cleanName = cleanName.Replace(srcPre, "");
                    }

                    if (!string.IsNullOrEmpty(srcSuf)) 
                    {
                        if (cleanName.EndsWith(srcSuf, StringComparison.OrdinalIgnoreCase))
                        {
                            cleanName = cleanName.Substring(0, cleanName.Length - srcSuf.Length);
                        }
                    }

                    string outputName = $"{genPre}{cleanName}{genSuf}.{ext}";
                    string outputPath = System.IO.Path.Combine(destFolder, outputName);

                    list.Add(new PreviewItem 
                    { 
                        SourceName = filename, 
                        TargetName = outputName, 
                        SourcePath = file,
                        TargetPath = outputPath
                    });
                }
            }
            catch
            {
                // Ignored
            }

            return list;
        }

        private async void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                UpdateStatus("Cancelling...");
                BtnProcess.IsEnabled = false;
                return;
            }

            List<PreviewItem>? items = LstPreview.ItemsSource as List<PreviewItem>;

            if (items == null || items.Count == 0)
            {
                UpdateStatus("No files to process.");
                return;
            }

            string destFolder = (ChkUseSourceAsOutput.IsChecked == true) ? TxtSourcePath.Text : TxtOutputPath.Text;

            if (!Directory.Exists(destFolder))
            {
                try 
                { 
                    Directory.CreateDirectory(destFolder); 
                }
                catch 
                { 
                    UpdateStatus("Error: Cannot create output folder."); 
                    return; 
                }
            }

            _isProcessing = true;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            BtnProcess.Content = "Cancel Operation";
            
            ProgressBarMain.Value = 0;
            ProgressBarMain.Maximum = items.Count;
            ProgressBarMain.Visibility = Visibility.Visible; 
            
            UpdateStatus($"Starting batch process on {Environment.ProcessorCount} threads...");

            ToggleInputs(false);

            int success = 0;
            int errors = 0;
            int processedCount = 0;

            List<PreviewItem> itemsToProcess = items!;

            ProcessSettings settings = new ProcessSettings
            {
                RedSource = CmbRedSource!.SelectedIndex,
                RedInvert = CmbRedInvert!.SelectedIndex,
                GreenSource = CmbGreenSource!.SelectedIndex,
                GreenInvert = CmbGreenInvert!.SelectedIndex,
                BlueSource = CmbBlueSource!.SelectedIndex,
                BlueInvert = CmbBlueInvert!.SelectedIndex,
                AlphaSource = CmbAlphaSource!.SelectedIndex,
                AlphaInvert = CmbAlphaInvert!.SelectedIndex
            };

            try
            {
                await Task.Run(() =>
                {
                    ParallelOptions options = new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = Environment.ProcessorCount 
                    };

                    try
                    {
                        Parallel.ForEach(itemsToProcess, options, item =>
                        {
                            bool ok = RunImageMagick(item.SourcePath, item.TargetPath, settings);

                            if (ok)
                            {
                                Interlocked.Increment(ref success);
                            }
                            else
                            {
                                Interlocked.Increment(ref errors);
                            }

                            int current = Interlocked.Increment(ref processedCount);

                            Dispatcher.Invoke(() => 
                            {
                                ProgressBarMain.Value = current;
                                UpdateStatus($"Processed {current}/{itemsToProcess.Count}: {item.SourceName}");
                            });
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                }, token);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Critical Error: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null; 
                
                BtnProcess.Content = "Process Textures";
                
                BtnProcess.IsEnabled = true;
                ToggleInputs(true);
                
                ProgressBarMain.Visibility = Visibility.Collapsed;
                UpdateStatus($"Done. Processed: {success}, Errors: {errors}");
            }
        }

        private bool RunImageMagick(string input, string output, ProcessSettings settings)
        {
            try
            {
                string fullInput = Path.GetFullPath(input);
                string fullOutput = Path.GetFullPath(output);

                if (!string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(fullOutput))
                    {
                        File.Delete(fullOutput);
                    }
                }

                using (MagickImage image = new MagickImage(input))
                {
                    if (image.ColorSpace == ColorSpace.Gray)
                    {
                        image.ColorSpace = ColorSpace.sRGB;
                    }

                    image.Alpha(AlphaOption.Set);

                    using (MagickImageCollection sourceChannels = new MagickImageCollection())
                    {
                        sourceChannels.AddRange(image.Separate());

                        IMagickImage<byte> PrepareChannel(int srcIndex, int invertIndex)
                        {
                            if (invertIndex == 2) return new MagickImage(MagickColors.White, image.Width, image.Height);
                            if (invertIndex == 3) return new MagickImage(MagickColors.Black, image.Width, image.Height);

                            IMagickImage<byte> ch = sourceChannels[srcIndex].Clone();

                            if (invertIndex == 1) ch.Negate();
                            
                            return ch;
                        }

                        using (MagickImageCollection outputCollection = new MagickImageCollection())
                        {
                            outputCollection.Add(PrepareChannel(settings.RedSource, settings.RedInvert));
                            outputCollection.Add(PrepareChannel(settings.GreenSource, settings.GreenInvert));
                            outputCollection.Add(PrepareChannel(settings.BlueSource, settings.BlueInvert));

                            if (settings.AlphaSource > 0)
                            {
                                int actualSrcIndex = settings.AlphaSource - 1;
                                outputCollection.Add(PrepareChannel(actualSrcIndex, settings.AlphaInvert));
                            }

                            using (IMagickImage<byte> result = outputCollection.Combine())
                            {
                                if (settings.AlphaSource == 0)
                                {
                                    result.Alpha(AlphaOption.Off);
                                }

                                result.Write(output);
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => UpdateStatus($"Error: {ex.Message}"));
                return false;
            }
        }

        private void ToggleInputs(bool enable)
        {
            if (GrpSettings != null)
            {
                GrpSettings.IsEnabled = enable;
            }
        }

        private void UpdateStatus(string msg)
        {
            if (LblStatus == null)
            {
                return;
            }

            LblStatus.Text = msg;
        }
    }
}