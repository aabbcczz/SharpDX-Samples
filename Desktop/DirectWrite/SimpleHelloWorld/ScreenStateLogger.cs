using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

// copy from https://github.com/sharpdx/SharpDX-Samples/tree/master/Desktop/Direct3D11.1/ScreenCapture
// and also refer to https://github.com/google/latency-benchmark/blob/master/src/win/screenscraper.cpp
namespace SimpleHelloWorld
{
    internal sealed class ScreenStateLogger
    {
        public ScreenStateLogger()
        {
        }

        public void Run(Func<Bitmap[], long, bool> screenRefreshFunction)
        {
            var rects = new System.Drawing.Rectangle[] { new System.Drawing.Rectangle(0, 0, 0, 0) };

            Run(rects, screenRefreshFunction);
        }

        public bool Run(HWND hwnd, System.Drawing.Rectangle[] clientAreas, Func<Bitmap[], long, bool> screenRefreshFunction)
        {
            var rects = clientAreas.Select(clientArea => SystemHelper.ClientToScreen(hwnd, clientArea)).ToArray();

            if (rects.Any(r => r.Left < 0 || r.Right < 0 || r.Top < 0 || r.Bottom < 0))
            {
                return false;
            }

            Run(rects, screenRefreshFunction);

            return true;
        }

        public void Run(System.Drawing.Rectangle[] screenAreas, Func<Bitmap[], long, bool> screenRefreshFunction)
        {
            if (screenRefreshFunction == null)
            {
                throw new ArgumentNullException(nameof(screenRefreshFunction));
            }

            Factory1 factory = null;
            Adapter1 adapter = null;
            SharpDX.Direct3D11.Device device = null;
            Output output = null;
            Output1 output1 = null;

            try
            {
                factory = new Factory1();
                //Get first adapter
                adapter = factory.GetAdapter1(0);
                //Get device from adapter
                device = new SharpDX.Direct3D11.Device(adapter);
                //Get front buffer of the adapter
                output = adapter.GetOutput(0);
                output1 = output.QueryInterface<Output1>();

                // double check input screen capture area data and correct it if possible
                screenAreas = screenAreas.Select(
                    area =>
                    {
                        // Width/Height of desktop to capture
                        int desktopWidth = output.Description.DesktopBounds.Right;
                        int desktopHeight = output.Description.DesktopBounds.Bottom;

                        int x = area.Left;
                        int y = area.Top;
                        int width = Math.Min(area.Width, desktopWidth - x);
                        int height = Math.Min(area.Height, desktopHeight - y);

                        if (x < 0 || y < 0 || width <= 0 || height <= 0)
                        {
                            throw new ArgumentOutOfRangeException("invalid screen capture rectangle");
                        }

                        return new System.Drawing.Rectangle(x, y, width, height);
                    }).ToArray();

                // function stop flag
                bool stopped = false;

                // Duplicate the output
                using (var duplicatedOutput = output1.DuplicateOutput(device))
                {
                    var workItemQueue
                        = new ConcurrentQueue<(List<(Texture2D texture, DataBox mapSource)> resources, long timestamp)>();

                    var finishedWorkItemQueue
                        = new ConcurrentQueue<(List<(Texture2D texture, DataBox mapSource)> resources, long timestamp)>();

                    var callbackTask = Task.Run(() =>
                    {
                        while (!stopped)
                        {
                            if (!workItemQueue.TryDequeue(out var workItem))
                            {
                                continue;
                            }

                            var bitmaps = new Bitmap[workItem.resources.Count];
                            for (int i = 0; i < workItem.resources.Count; ++i)
                            {
                                var (texture, mapSource) = workItem.resources[i];

                                var bitmap = new Bitmap(screenAreas[i].Width, screenAreas[i].Height, PixelFormat.Format32bppArgb);

                                // Copy pixels from screen capture Texture to GDI bitmap
                                var rect = new System.Drawing.Rectangle(0, 0, screenAreas[i].Width, screenAreas[i].Height);

                                var mapDest = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
                                var sourcePtr = IntPtr.Add(mapSource.DataPointer, 0);
                                var destPtr = mapDest.Scan0;

                                for (int y = 0; y < rect.Height; y++)
                                {
                                    // Copy a single line 
                                    Utilities.CopyMemory(destPtr, sourcePtr, rect.Width * 4);

                                    // Advance pointers
                                    sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
                                    destPtr = IntPtr.Add(destPtr, mapDest.Stride);
                                }

                                // Release source and dest locks
                                bitmap.UnlockBits(mapDest);

                                bitmaps[i] = bitmap;
                            }

                            finishedWorkItemQueue.Enqueue(workItem);

                            // callback
                            if (!screenRefreshFunction(bitmaps, workItem.timestamp))
                            {
                                stopped = true;
                            }

                            // release bitmaps
                            foreach (var bitmap in bitmaps)
                            {
                                if (bitmap != null)
                                {
                                    bitmap.Dispose();
                                }
                            }
                        }

                        // clean queued work items
                        while (workItemQueue.TryDequeue(out var workItem))
                        {
                            finishedWorkItemQueue.Enqueue(workItem);
                        }
                    });

                    while (!stopped)
                    {
                        try
                        {
                            // Try to get duplicated frame within given time is ms
                            duplicatedOutput.AcquireNextFrame(Timeout.Infinite, out var duplicateFrameInformation, out var screenResource);

                            if (duplicateFrameInformation.AccumulatedFrames == 0) // actual desktop image not changed
                            {
                                screenResource.Dispose();
                                duplicatedOutput.ReleaseFrame();
                                continue;
                            }

                            // get timestamp
                            var frameAvailableTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

                            // get data for each area
                            var associatedDXResources = new List<(Texture2D texture, DataBox mapSource)>(screenAreas.Length);
                            foreach (var area in screenAreas)
                            {
                                // Create Staging texture CPU-accessible
                                var textureDesc = new Texture2DDescription
                                {
                                    CpuAccessFlags = CpuAccessFlags.Read,
                                    BindFlags = BindFlags.None,
                                    Format = Format.B8G8R8A8_UNorm,
                                    Width = area.Width,
                                    Height = area.Height,
                                    OptionFlags = ResourceOptionFlags.None,
                                    MipLevels = 1,
                                    ArraySize = 1,
                                    SampleDescription = { Count = 1, Quality = 0 },
                                    Usage = ResourceUsage.Staging
                                };

                                var screenTexture = new Texture2D(device, textureDesc);
                                // copy resource into memory that can be accessed by the CPU
                                using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
                                {
                                    var region = new ResourceRegion()
                                    {
                                        Left = area.X,
                                        Top = area.Top,
                                        Right = area.Right,
                                        Bottom = area.Bottom,
                                        Front = 0,
                                        Back = 1,
                                    };

                                    device.ImmediateContext.CopySubresourceRegion(screenTexture2D, 0, region, screenTexture, 0);
                                }

                                // Get the desktop capture texture
                                var mapSource = device.ImmediateContext.MapSubresource(screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                                associatedDXResources.Add((screenTexture, mapSource));
                            }

                            workItemQueue.Enqueue((associatedDXResources, frameAvailableTimestamp));

                            // release screen resource
                            screenResource.Dispose();

                            // release frame
                            duplicatedOutput.ReleaseFrame();

                            // release at most 3 finished work items to avoid block next frame.
                            for (int i = 0; i < 3; ++i)
                            {
                                if (finishedWorkItemQueue.TryDequeue(out var finishedWorkItem))
                                {
                                    foreach (var (texture, mapSource) in finishedWorkItem.resources)
                                    {
                                        device.ImmediateContext.UnmapSubresource(texture, 0);
                                        texture.Dispose();
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        catch (SharpDXException e)
                        {
                            if (e.ResultCode.Code != SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                            {
                                throw;
                            }
                        }
                    }

                    callbackTask.Wait();

                    while (finishedWorkItemQueue.TryDequeue(out var finishedWorkItem))
                    {
                        foreach (var (texture, mapSource) in finishedWorkItem.resources)
                        {
                            device.ImmediateContext.UnmapSubresource(texture, 0);
                            texture.Dispose();
                        }
                    }
                }
            }
            finally
            {
                if (output1 != null)
                {
                    output1.Dispose();
                }

                if (output != null)
                {
                    output.Dispose();
                }

                if (device != null)
                {
                    device.Dispose();
                }

                if (adapter != null)
                {
                    adapter.Dispose();
                }

                if (factory != null)
                {
                    factory.Dispose();
                }
            }
        }
    }
}
