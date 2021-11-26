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
using SharpDX.DirectWrite;
using SharpDX.Samples;
using TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode;
using System.Collections.Generic;
using DxgiLatencyShare;
using System.Threading;

namespace DxgiLatencyProvider
{

    /// <summary>
    /// Shows how to use DirectWrite to render simple text.
    /// Port of DirectWrite sample SimpleHelloWorld from Windows 7 SDK samples
    /// http://msdn.microsoft.com/en-us/library/dd742738%28v=VS.85%29.aspx
    /// </summary>
    public class Program :  Direct2D1WinFormDemoApp
    {
        enum DrawState { FreeDraw, DrawMeasure, Redraw };

        public TextFormat TextFormat { get; private set; }
        public SharpDX.RectangleF ClientRectangle { get; private set; }

        private InterProcessCommunication _ipc = null;

        private string _folderForSavingImage = string.Empty;

        private readonly List<(long timestamp, System.Drawing.Bitmap image)> _renderedImages 
            = new List<(long timestamp, System.Drawing.Bitmap image)>();

        private DrawState _drawState = DrawState.FreeDraw;

        private int _drawCount = 0;
        private int _redrawCount = 0;
        private int _recaptureCount = 0;

        public Program(string[] args)
        {
            if (args.Length > 1)
            {
                _folderForSavingImage = args[1];
            }
        }

        protected override void Initialize(DemoConfiguration demoConfiguration)
        {
            base.Initialize(demoConfiguration);

            // Initialize a TextFormat
            TextFormat = new TextFormat(FactoryDWrite, "Gabriola", 96) { TextAlignment = TextAlignment.Center, ParagraphAlignment = ParagraphAlignment.Center };

            RenderTarget2D.TextAntialiasMode = TextAntialiasMode.Cleartype;

            ClientRectangle = new SharpDX.RectangleF(0, 0, demoConfiguration.Width, demoConfiguration.Height);

            SceneColorBrush.Color = SharpDX.Color.Black;

            // initialize inter-process communication
            _ipc = new InterProcessCommunication();
        }

        private void DrawText(long timestamp)
        {
            RenderTarget2D.Clear(SharpDX.Color.White);

            var text = timestamp.ToString();

            RenderTarget2D.DrawText(text, TextFormat, ClientRectangle, SceneColorBrush);
        }

        protected override void Draw(DemoTime time)
        {
            base.Draw(time);

            string text = string.Empty;

            if (_drawState == DrawState.FreeDraw)
            {
                DrawText(Stopwatch.GetTimestamp());

                if (_ipc.StartRenderEvent.WaitOne(0))
                {
                    _drawState = DrawState.DrawMeasure;
                }
            }
            else if (_drawState == DrawState.DrawMeasure)
            {
                var timestamp = Stopwatch.GetTimestamp();

                DrawText(timestamp);

                _renderedImages.Add((timestamp, null));

                ++_drawCount;

                if (_drawCount >= 50)
                {
                    _drawState = DrawState.Redraw;

                    _ipc.StopRenderEvent.Set();
                }
            }
            else if (_drawState == DrawState.Redraw)
            {
                if (_recaptureCount < _redrawCount)
                {
                    var image = SystemHelper.CaptureScreen(DisplayHandle, SystemHelper.GetClientRect(DisplayHandle));
                    _renderedImages[_recaptureCount] = (_renderedImages[_recaptureCount].timestamp, image);

                    ++_recaptureCount;
                }
                else
                {
                    if (_redrawCount >= _renderedImages.Count)
                    {
                        SaveImages();

                        _ipc.SavedImageEvent.Set();

                        _drawState = DrawState.FreeDraw;
                    }
                    else
                    {
                        DrawText(_renderedImages[_redrawCount].timestamp);

                        ++_redrawCount;
                    }
                }
            }
        }

        private void SaveImages()
        {
            if (string.IsNullOrEmpty(_folderForSavingImage))
            {
                return;
            }

            ImageProcessor.SaveImages(_folderForSavingImage, _renderedImages);

            foreach (var (_, image) in _renderedImages)
            {
                image.Dispose();
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var cmdArgs = Environment.GetCommandLineArgs();

            Program program = new Program(cmdArgs);
            program.Run(new DemoConfiguration(InterProcessCommunication.DxgiLatencyProviderTitle));
        }
    }
}
