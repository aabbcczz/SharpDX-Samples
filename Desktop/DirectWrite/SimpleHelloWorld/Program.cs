// Copyright (c) 2010-2013 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Diagnostics;
using System.Linq;
using System.Drawing;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.Samples;
using TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode;
using Vanara.PInvoke;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace SimpleHelloWorld
{

    /// <summary>
    /// Shows how to use DirectWrite to render simple text.
    /// Port of DirectWrite sample SimpleHelloWorld from Windows 7 SDK samples
    /// http://msdn.microsoft.com/en-us/library/dd742738%28v=VS.85%29.aspx
    /// </summary>
    public class Program :  Direct2D1WinFormDemoApp
    {
        enum DrawState { Draw, ReDraw, DrawLatency };

        public TextFormat TextFormat { get; private set; }
        public SharpDX.RectangleF ClientRectangle { get; private set; }

        private readonly ScreenStateLogger _ssl = new ScreenStateLogger();
        private readonly List<(long timestamp, Image<Bgr, byte> image)> _renderedImages = new List<(long timestamp, Image<Bgr, byte> image)>();
        private readonly List<(long timestamp, Image<Bgr, byte> image)> _capturedImages = new List<(long timestamp, Image<Bgr, byte> image)>();

        private DrawState _drawState = DrawState.Draw;

        private bool _renderStarted = false;
        private bool _renderStopped = false;

        private int _drawCount = 0;
        private int _redrawCount = 0;
        private int _recaptureCount = 0;

        private double _latencyInMillisecond = 0.0;

        private Task _captureTask;

        protected override void Initialize(DemoConfiguration demoConfiguration)
        {
            base.Initialize(demoConfiguration);

            // Initialize a TextFormat
            TextFormat = new TextFormat(FactoryDWrite, "Gabriola", 96) { TextAlignment = TextAlignment.Center, ParagraphAlignment = ParagraphAlignment.Center };

            RenderTarget2D.TextAntialiasMode = TextAntialiasMode.Cleartype;

            ClientRectangle = new SharpDX.RectangleF(0, 0, demoConfiguration.Width, demoConfiguration.Height);

            SceneColorBrush.Color = SharpDX.Color.Black;

            HWND handle = (HWND)DisplayHandle;

            var clientRect = SystemHelper.GetClientRect(handle);

            var screenRect = SystemHelper.ClientToScreen(handle, clientRect);

            System.Drawing.Rectangle[] screenAreas = new System.Drawing.Rectangle[]
            {
                screenRect,
            };

            _captureTask = Task.Run(() => { CaptureTask(screenAreas); });
        }

        private void CaptureTask(System.Drawing.Rectangle[] screenAreas)
        {
            this._ssl.Run(
                screenAreas, 
                (System.Drawing.Bitmap[] capturedPictures, long timestamp) =>
                {
                    if (_renderStarted)
                    {
                        var image = capturedPictures[0];

                        var clonedImage = image.ToImage<Bgr, byte>();
                        _capturedImages.Add((timestamp, clonedImage));
                    }

                    return !_renderStopped;
                });
        }

        protected override void Draw(DemoTime time)
        {
            base.Draw(time);

            string text = string.Empty;

            if (_drawState == DrawState.Draw)
            {
                RenderTarget2D.Clear(SharpDX.Color.White);

                if (_drawCount >= 80)
                {
                    if (!_renderStopped)
                    {
                        _renderStopped = true;

                        _captureTask.Wait();

                        _drawState = DrawState.ReDraw;
                    }
                }
                else
                {
                    //string text = "Hello World using DirectWrite!";
                    var timestamp = Stopwatch.GetTimestamp();

                    text = timestamp.ToString();

                    RenderTarget2D.DrawText(text, TextFormat, ClientRectangle, SceneColorBrush);

                    if (_drawCount > 40) // warm up
                    {
                        if (!_renderStarted)
                        {
                            _renderStarted = true;
                        }

                        _renderedImages.Add((timestamp, null));
                    }

                    ++_drawCount;
                }
            }
            else if (_drawState == DrawState.ReDraw)
            {
                if (_recaptureCount < _redrawCount)
                {
                    var image = SystemHelper.CaptureScreen((HWND)DisplayHandle, SystemHelper.GetClientRect((HWND)DisplayHandle));
                    _renderedImages[_recaptureCount] = (_renderedImages[_recaptureCount].timestamp, image.ToImage<Bgr, byte>());

                    ++_recaptureCount;
                }
                else
                {
                    if (_redrawCount >= _renderedImages.Count)
                    {
#if DEBUG
                        //SaveImages();
#endif
                        UpdateLatency();
                        _drawState = DrawState.DrawLatency;
                    }
                    else
                    {
                        RenderTarget2D.Clear(SharpDX.Color.White);

                        text = _renderedImages[_redrawCount].timestamp.ToString();

                        RenderTarget2D.DrawText(text, TextFormat, ClientRectangle, SceneColorBrush);

                        ++_redrawCount;
                    }
                }
            }
            else if (_drawState == DrawState.DrawLatency)
            {
                RenderTarget2D.Clear(SharpDX.Color.White);

                text = $"{_latencyInMillisecond:0.0000} ms";

                RenderTarget2D.Clear(SharpDX.Color.White);
                RenderTarget2D.DrawText(text, TextFormat, ClientRectangle, SceneColorBrush);
            }
        }

        private void SaveImages()
        {
            foreach (var (timestamp, image) in _renderedImages)
            {
                image.Save($"{timestamp}.render.bmp");
            }

            foreach (var (timestamp, image) in _capturedImages)
            {
                image.Save($"{timestamp}.capture.bmp");
            }
        }

        private void UpdateLatency()
        {
            int maxUsableDataCount = Math.Min(_renderedImages.Count, _capturedImages.Count);

            double totalLatency = 0.0;
            int totalCount = 0;

            int lastMatchIndex = -1;

            for (int i = 0; i < maxUsableDataCount; ++i)
            {
                int startMatchIndex = lastMatchIndex + 1;
                while (startMatchIndex < _capturedImages.Count)
                {
                    if (_capturedImages[startMatchIndex].timestamp <= _renderedImages[i].timestamp)
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
                    .Select(idx => MatchTwoImages(_renderedImages[i].image, _capturedImages[idx].image))
                    .ToList();

                var maxMatchScore = matchScores.Max();
                if (maxMatchScore < 0.9) 
                {
                    // ignore it
                    continue;
                }

                int maxMatchIndex = matchScores.FindIndex(v => v == maxMatchScore);

                int actualMatchIndex = maxMatchIndex + startMatchIndex;

                var latency = (double)(_capturedImages[actualMatchIndex].timestamp - _renderedImages[i].timestamp) * 1000.0 / Stopwatch.Frequency;
                totalLatency += latency;
                totalCount++;

                lastMatchIndex = actualMatchIndex;
            }

            if (totalCount > 0)
            {
                _latencyInMillisecond = totalLatency / totalCount;
            }
            else
            {
                _latencyInMillisecond = 0.0;
            }
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

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Program program = new Program();
            program.Run(new DemoConfiguration("SharpDX DirectWrite Simple HelloWorld Demo"));
        }
    }
}
