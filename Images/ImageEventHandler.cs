#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace DaleGhent.NINA.GroundStation.Images {
    public class ImageEventHandler(IProfileService profileService, IImageSaveMediator imageSaveMediator, IImageDataFactory imageDataFactory) {
        private readonly IProfileService profileService = profileService;
        private readonly IImageSaveMediator imageSaveMediator = imageSaveMediator;
        private readonly IImageDataFactory imageDataFactory = imageDataFactory;
        private ImageData currentImageData;

        public void Start() {
            Stop();
            imageSaveMediator.ImageSaved += ImageSaveMeditator_ImageSaved;
        }

        public void Stop() {
            imageSaveMediator.ImageSaved -= ImageSaveMeditator_ImageSaved;
            currentImageData?.Bitmap?.Dispose();
        }

        private async void ImageSaveMeditator_ImageSaved(object sender, ImageSavedEventArgs msg) {
            try {
                var profile = profileService.ActiveProfile;
                var isBayered = msg.MetaData.Camera.SensorType > SensorType.Monochrome;
                var bitDepth = (int)profile.CameraSettings.BitDepth;
                var rawConverter = profile.CameraSettings.RawConverter;

                var imageData = await imageDataFactory.CreateFromFile(msg.PathToImage.LocalPath, bitDepth, isBayered, rawConverter);
                var renderedImage = imageData.RenderImage();

                if (isBayered && profile.ImageSettings.DebayerImage) {
                    renderedImage = renderedImage.Debayer(
                        saveColorChannels: profile.ImageSettings.UnlinkedStretch,
                        bayerPattern: msg.MetaData.Camera.SensorType);
                }

                renderedImage = await renderedImage.Stretch(
                    profile.ImageSettings.AutoStretchFactor,
                    profile.ImageSettings.BlackClipping,
                    profile.ImageSettings.UnlinkedStretch);

                var bitmapFrame = CreateScaledBitmapFrame(renderedImage.Image);
                var (contentType, fileExtension) = GetImageFormatInfo();

                using var encodedStream = EncodeImage(bitmapFrame, contentType);
                currentImageData = new ImageData {
                    Bitmap = new MemoryStream(encodedStream.ToArray()),
                    ImageMetaData = msg.MetaData,
                    ImageMimeType = contentType,
                    ImageFileExtension = fileExtension,
                    ImageStatistics = msg.Statistics,
                    StarDetectionAnalysis = msg.StarDetectionAnalysis,
                    ImagePath = msg.PathToImage.LocalPath,
                };

                ImageService.Instance.Image = currentImageData;
            } catch (Exception ex) {
                Logger.Error($"ImageEventHandler: Failed to process image: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private BitmapFrame CreateScaledBitmapFrame(BitmapSource imageSource) {
            var scaling = GroundStation.GroundStationConfig.ImageServiceImageScaling / 100d;
            if (scaling < 1.0) {
                var transform = new ScaleTransform(scaling, scaling);
                var scaledBitmap = new TransformedBitmap(imageSource, transform);
                return BitmapFrame.Create(scaledBitmap);
            }

            return BitmapFrame.Create(imageSource);
        }

        private static (string contentType, string fileExtension) GetImageFormatInfo() {
            var format = (ImageFormatEnum)GroundStation.GroundStationConfig.ImageServiceFormat;
            return format switch {
                ImageFormatEnum.JPEG => ("image/jpeg", "jpg"),
                ImageFormatEnum.PNG => ("image/png", "png"),
                _ => ("image/jpeg", "jpg"),
            };
        }

        private MemoryStream EncodeImage(BitmapFrame bitmapFrame, string contentType) {
            var stream = new MemoryStream();
            var encoder = contentType switch {
                "image/jpeg" => (BitmapEncoder)new JpegBitmapEncoder {
                    QualityLevel = GroundStation.GroundStationConfig.ImageServiceJpegQuality,
                },
                "image/png" => new PngBitmapEncoder(),
                _ => new JpegBitmapEncoder {
                    QualityLevel = GroundStation.GroundStationConfig.ImageServiceJpegQuality,
                },
            };

            encoder.Frames.Add(bitmapFrame);
            encoder.Save(stream);
            stream.Position = 0;
            return stream;
        }
    }
}