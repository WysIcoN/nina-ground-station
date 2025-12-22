using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaleGhent.NINA.GroundStation.Images {
    public static class ImageProcessing {
        public static MemoryStream ConvertToJpegReduced(Stream source, int scaleFactor = 8, int jpegQuality = 90) {
            if (source == null) return null;

            source.Position = 0;

            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            // Reduce bit depth to 8-bit grayscale
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);

            // Downscale by scaleFactor
            BitmapSource outputSource;
            if (scaleFactor > 1) {
                var scale = 1.0 / scaleFactor;
                var transform = new ScaleTransform(scale, scale);
                var transformed = new TransformedBitmap(converted, transform);
                outputSource = transformed;
            } else {
                outputSource = converted;
            }

            var ms = new MemoryStream();
            var encoder = new JpegBitmapEncoder() { QualityLevel = jpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(outputSource));
            encoder.Save(ms);
            ms.Position = 0;
            return ms;
        }
    }
}
