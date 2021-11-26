using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;

namespace DxgiLatencyShare
{
    public static class ImageProcessor
    {
        private const string RenderImageSuffix = ".render.bmp";

        public static string GenerateUniqueFolderNameForImages()
        {
            string folder = Guid.NewGuid().ToString("D");

            var fullPath = GetFullPathForImages(folder);

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return folder;
        }

        public static string GetFullPathForImages(string folder)
        {
            return Path.GetFullPath(Path.Combine(".", folder));
        }

        public static void SaveImages(string folder, List<(long timestamp, Bitmap image)> images)
        {
            var fullPath = GetFullPathForImages(folder);

            foreach (var (timestamp, image) in images)
            {
                image.Save(Path.Combine(fullPath, $"{timestamp}{RenderImageSuffix}"));
            }
        }

        public static void RemoveImages(string folder)
        {
            var fullPath = GetFullPathForImages(folder);

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        }

        public static List<(long timestamp, Bitmap image)> LoadImages(string folder)
        {
            List<(long timestamp, Bitmap image)> images = new List<(long timestamp, Bitmap image)>();

            var fullPath = GetFullPathForImages(folder);

            var files = Directory.EnumerateFiles(fullPath);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                if (fileName.EndsWith(RenderImageSuffix))
                {
                    var timestampString = fileName.Substring(0, fileName.Length - RenderImageSuffix.Length);

                    if (long.TryParse(timestampString, out long timestamp))
                    {
                        Bitmap image = (Bitmap)Image.FromFile(file);

                        images.Add((timestamp, image));
                    }
                }
            }

            return images;
        }
    }
}
