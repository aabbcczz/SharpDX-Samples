using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using DxgiLatencyShare;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DxgiLatencyMeasure
{
    class Program
    {
        private List<(long timestamp, Image<Bgr, byte> image)> _capturedImages = new List<(long timestamp, Image<Bgr, byte> image)>();
        private InterProcessCommunication _ipc = null;
        private string _imageFolder = string.Empty;

        private bool _stopped = false;

        private bool _renderStarted = false;
        private bool _renderStopped = false;
        private bool _imageSaved = false;

        static void Main(string[] args)
        {
            var program = new Program();

            program.Initialize();

            program.Run();

            program.CleanUp();
        }

        private void Initialize()
        {
            _ipc = new InterProcessCommunication();
            _imageFolder = ImageProcessor.GenerateUniqueFolderNameForImages();
        }

        private void Run()
        {
            // start DxgiLatencyProvider.exe
            // Prepare the process to run
            ProcessStartInfo start = new ProcessStartInfo();
            // Enter in the command line arguments, everything you would enter after the executable name itself
            start.Arguments = _imageFolder;
            // Enter the executable to run, including the complete path
            start.FileName = Path.Combine(Environment.CurrentDirectory, "DxgiLatencyProvider.exe");
            // Do you want to show a console window?
            start.WindowStyle = ProcessWindowStyle.Normal;
            start.CreateNoWindow = false;
            int exitCode;

            Console.WriteLine("Start DxgiLatencyProvider.exe");
            
            // Run the external process & wait for it to finish
            using (Process proc = Process.Start(start))
            {
                Console.WriteLine("wait for stablization...");
                Thread.Sleep(3000);

                var handle = GetDxgiLatencyProviderMainWindow();

                if (handle == IntPtr.Zero)
                {
                    Console.WriteLine($"Failed to find {InterProcessCommunication.DxgiLatencyProviderTitle} main window");
                    return;
                }

                SystemHelper.ActivateWindow(handle);
                Thread.Sleep(1000);

                var screenAreas = GetDxgiLatencyProviderScreenRect(handle);
                if (screenAreas == null)
                {
                    return;
                }

                Task.Run(() =>
                {
                    while (!_stopped)
                    {
                        if (_ipc.StopRenderEvent.WaitOne(100))
                        {
                            _renderStopped = true;
                            Console.WriteLine("render stopped");
                        }
                    }
                });

                Task.Run(() =>
                {
                    while (!_stopped)
                    {
                        if (_ipc.SavedImageEvent.WaitOne(100))
                        {
                            _imageSaved = true;
                            Console.WriteLine("image saved");
                        }
                    }
                });


                var captureTask = Task.Run(() => { CaptureTask(screenAreas); });

                Console.WriteLine("render started");

                _renderStarted = true;
                _ipc.StartRenderEvent.Set();

                while (!proc.HasExited && !_imageSaved)
                {
                    Thread.Sleep(100);
                }

                if (_imageSaved)
                {
                    Console.WriteLine("Calculating latency...");

                    var images = ImageProcessor.LoadImages(_imageFolder);
                    var latency = CalculateLatency(images);

                    foreach (var (_, image) in images)
                    {
                        image.Dispose();
                    }

                    Console.WriteLine($"Succeeded. Latency: {latency:0.0000}ms");
                }

                while (!proc.HasExited)
                {
                    SystemHelper.CloseWindow(handle);

                    proc.WaitForExit(1000);
                }

                // Retrieve the app's exit code
                exitCode = proc.ExitCode;

                Console.WriteLine($"{InterProcessCommunication.DxgiLatencyProviderTitle} exit with {exitCode}");
            }
        }

        private void CleanUp()
        {
            _ipc.Dispose();

            ImageProcessor.RemoveImages(_imageFolder);
        }

        private IntPtr GetDxgiLatencyProviderMainWindow()
        {
            var windows = SystemHelper.FindWindows("WindowsForms", InterProcessCommunication.DxgiLatencyProviderTitle, true);

            if (windows.Count() == 0)
            {
                Console.WriteLine($"Failed to find any '{InterProcessCommunication.DxgiLatencyProviderTitle}' window");
                return IntPtr.Zero;
            }

            if (windows.Count() > 1)
            {
                Console.WriteLine($"More than one '{InterProcessCommunication.DxgiLatencyProviderTitle}' windows are found, failed to measure latency");
                return IntPtr.Zero;
            }

            var handle = windows.First();

            return handle.DangerousGetHandle();
        }

        private Rectangle[] GetDxgiLatencyProviderScreenRect(IntPtr handle)
        {
            var screenAreas = new Rectangle[] { SystemHelper.ClientToScreen(handle, SystemHelper.GetClientRect(handle)) };

            return screenAreas;
        }

        private void CaptureTask(Rectangle[] screenAreas)
        {
            var ssl = new ScreenStateLogger();

            ssl.Run(
                screenAreas,
                (Bitmap[] capturedPictures, long timestamp) =>
                {
                    if (_renderStarted)
                    {
                        var image = capturedPictures[0];

                        var clonedImage = image.ToImage<Bgr, byte>();
                        _capturedImages.Add((timestamp, clonedImage));
                    }

                    return !_renderStopped && !_stopped;
                });
        }


        private double CalculateLatency(List<(long timestamp, Bitmap image)> renderedBitmapImages)
        {
            List<(long timestamp, Image<Bgr, byte> image)> renderedImages = renderedBitmapImages.Select(t => (t.timestamp, t.image.ToImage<Bgr, byte>())).ToList();

            int maxUsableDataCount = Math.Min(renderedImages.Count, _capturedImages.Count);

            double totalLatency = 0.0;
            int totalCount = 0;

            int lastMatchIndex = -1;

            for (int i = 0; i < maxUsableDataCount; ++i)
            {
                int startMatchIndex = lastMatchIndex + 1;
                while (startMatchIndex < _capturedImages.Count)
                {
                    if (_capturedImages[startMatchIndex].timestamp <= renderedImages[i].timestamp)
                    {
                        ++startMatchIndex;
                    }
                    else
                    {
                        break;
                    }
                }

                if (startMatchIndex >= _capturedImages.Count)
                {
                    break;
                }

                var matchScores = Enumerable.Range(startMatchIndex, Math.Min(_capturedImages.Count - startMatchIndex, 10))
                    .Select(idx => MatchTwoImages(renderedImages[i].image, _capturedImages[idx].image))
                    .ToList();

                var maxMatchScore = matchScores.Max();
                if (maxMatchScore < 0.9)
                {
                    // ignore it
                    continue;
                }

                int maxMatchIndex = matchScores.FindIndex(v => v == maxMatchScore);

                int actualMatchIndex = maxMatchIndex + startMatchIndex;

                var latency = (double)(_capturedImages[actualMatchIndex].timestamp - renderedImages[i].timestamp) * 1000.0 / Stopwatch.Frequency;
                totalLatency += latency;
                totalCount++;

                lastMatchIndex = actualMatchIndex;
            }

            return totalCount > 0 ? totalLatency / totalCount : 0.0;
        }

        private static double MatchTwoImages(Image<Bgr, byte> image1, Image<Bgr, byte> image2)
        {
            if (image1.Width < image2.Width && image1.Height < image2.Height)
            {
                var temp = image1;
                image1 = image2;
                image2 = temp;
            }

            if (image1.Width < image2.Width || image1.Height < image2.Height)
            {
                throw new ArgumentException("image size is not proper for matching");
            }

            using (var result = image1.MatchTemplate(image2, TemplateMatchingType.CcoeffNormed))
            {
                result.MinMax(out double[] minValues, out double[] maxValues, out var minValueLocs, out var maxValueLocs);

                return maxValues[0];
            }
        }
    }
}
