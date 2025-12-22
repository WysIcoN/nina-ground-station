#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaleGhent.NINA.GroundStation.Email;
using DaleGhent.NINA.GroundStation.MetadataClient;
using MimeKit;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.GroundStation.SendToEmail {

    [ExportMetadata("Name", "Send email")]
    [ExportMetadata("Description", "Sends an email")]
    [ExportMetadata("Icon", "Email_SVG")]
    [ExportMetadata("Category", "Ground Station")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class SendToEmail : SequenceItem, IValidatable {
        private readonly EmailCommon email;
        private string recipient;
        private string subject = string.Empty;
        private string body = string.Empty;

        private readonly ICameraMediator cameraMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFlatDeviceMediator flatDeviceMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly ISwitchMediator switchMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IWeatherDataMediator weatherDataMediator;

        private readonly IMetadata metadata;
        private IWindowService windowService;

        [ImportingConstructor]
        public SendToEmail(ICameraMediator cameraMediator,
                        IDomeMediator domeMediator,
                        IFilterWheelMediator filterWheelMediator,
                        IFlatDeviceMediator flatDeviceMediator,
                        IFocuserMediator focuserMediator,
                        IGuiderMediator guiderMediator,
                        IRotatorMediator rotatorMediator,
                        ISafetyMonitorMediator safetyMonitorMediator,
                        ISwitchMediator switchMediator,
                        ITelescopeMediator telescopeMediator,
                        IWeatherDataMediator weatherDataMediator) {

            this.cameraMediator = cameraMediator;
            this.domeMediator = domeMediator;
            this.guiderMediator = guiderMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.flatDeviceMediator = flatDeviceMediator;
            this.focuserMediator = focuserMediator;
            this.guiderMediator = guiderMediator;
            this.rotatorMediator = rotatorMediator;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.switchMediator = switchMediator;
            this.telescopeMediator = telescopeMediator;
            this.weatherDataMediator = weatherDataMediator;

            metadata = new Metadata(cameraMediator,
                domeMediator, filterWheelMediator, flatDeviceMediator, focuserMediator,
                guiderMediator, rotatorMediator, safetyMonitorMediator, switchMediator,
                telescopeMediator, weatherDataMediator);

            email = new EmailCommon();
        }

        public SendToEmail() {
            email = new EmailCommon();
            Recipient = GroundStation.GroundStationConfig.SmtpDefaultRecipients;
        }

        public SendToEmail(SendToEmail copyMe) : this(
                                                    cameraMediator: copyMe.cameraMediator,
                                                    domeMediator: copyMe.domeMediator,
                                                    filterWheelMediator: copyMe.filterWheelMediator,
                                                    flatDeviceMediator: copyMe.flatDeviceMediator,
                                                    focuserMediator: copyMe.focuserMediator,
                                                    guiderMediator: copyMe.guiderMediator,
                                                    rotatorMediator: copyMe.rotatorMediator,
                                                    safetyMonitorMediator: copyMe.safetyMonitorMediator,
                                                    switchMediator: copyMe.switchMediator,
                                                    telescopeMediator: copyMe.telescopeMediator,
                                                    weatherDataMediator: copyMe.weatherDataMediator) {
            CopyMetaData(copyMe);
        }

        [JsonProperty]
        public string Recipient {
            get {
                if (string.IsNullOrEmpty(recipient)) {
                    recipient = GroundStation.GroundStationConfig.SmtpDefaultRecipients;
                }

                return recipient;
            }
            set {
                recipient = value;

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(MessagePreview));
                Validate();
            }
        }

        [JsonProperty]
        public string Subject {
            get => subject;
            set {
                subject = value;

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(MessagePreview));
                Validate();
            }
        }

        [JsonProperty]
        public string Body {
            get => body;
            set {
                body = value;

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(MessagePreview));
                Validate();
            }
        }

        public string MessagePreview {
            get {
                string text;
                byte mesgPreviewLen = 30;

                if (string.IsNullOrEmpty(Recipient)) {
                    return "No recipient specified";
                } else {
                    text = $"To: {Recipient}";
                }

                if (!string.IsNullOrEmpty(subject)) {
                    var count = subject.Length > mesgPreviewLen ? mesgPreviewLen : subject.Length;
                    text += $"; Subject: {subject[..count]}";

                    if (subject.Length > mesgPreviewLen) {
                        text += "...";
                    }
                } else {
                    text += "; No subject";
                }

                return text;
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            // Wait for ImageEventHandler to complete processing and update the image in ImageService.
            // ImageEventHandler is async void, so it returns immediately without waiting.
            // We poll for the image to change before proceeding, with a timeout for robustness.
            await WaitForImageUpdate(ct);

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(GroundStation.GroundStationConfig.SmtpFromAddress));
            message.To.AddRange(InternetAddressList.Parse(Recipient));
            message.Subject = Utilities.Utilities.ResolveTokens(Subject, this, metadata);

            var resolvedBody = Utilities.Utilities.ResolveTokens(Body, this, metadata);
            var builder = new BodyBuilder();
            
            // Only attach image if the body contains the image preview token
            if (Body.Contains("$$IMAGE_PREVIEW$$")) {
                AttachProcessedImage(builder);
                // Remove the token from the resolved body after image is attached
                resolvedBody = resolvedBody.Replace("$$IMAGE_PREVIEW$$", string.Empty);
            }

            builder.TextBody = resolvedBody;
            message.Body = builder.ToMessageBody();

            await EmailCommon.SendEmail(message, ct);
        }

        private async Task WaitForImageUpdate(CancellationToken ct) {
            const int pollIntervalMs = 20;
            const int timeoutMs = 5000;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Capture the initial state
            var imageService = DaleGhent.NINA.GroundStation.Images.ImageService.Instance;
            var initialImage = imageService.Image;
            var initialLength = initialImage?.Bitmap?.Length ?? 0;
            var initialPath = initialImage?.ImagePath ?? string.Empty;

            // Poll until the image changes or we timeout
            while (sw.ElapsedMilliseconds < timeoutMs) {
                await Task.Delay(pollIntervalMs, ct);

                var currentImage = imageService.Image;
                var currentLength = currentImage?.Bitmap?.Length ?? 0;
                var currentPath = currentImage?.ImagePath ?? string.Empty;

                // Check if image has been updated (either size or path changed)
                if (currentLength != initialLength || currentPath != initialPath) {
                    return; // Image has been updated
                }
            }

            // Timeout reached, but proceed anyway - image might still be processing
        }


        private void AttachProcessedImage(BodyBuilder builder) {
            try {
                // Get a snapshot of the image data. We create a local copy of the bitmap
                // to avoid holding a reference to the shared ImageService resource longer
                // than necessary, and to avoid modifying the stream position of a resource
                // that might be used concurrently elsewhere.
                var imageData = DaleGhent.NINA.GroundStation.Images.ImageService.Instance.Image;
                if (imageData?.Bitmap == null || imageData.Bitmap.Length == 0) {
                    return;
                }

                // Create a defensive copy of the bitmap data to process independently
                byte[] bitmapCopy;
                lock (imageData) {  // Lock if the ImageData supports it; if not, this is still safe
                    bitmapCopy = imageData.Bitmap.ToArray();
                }

                using var bitmapStream = new System.IO.MemoryStream(bitmapCopy);
                var quality = GroundStation.GroundStationConfig.ImageServiceJpegQuality;
                using var processedImage = DaleGhent.NINA.GroundStation.Images.ImageProcessing.ConvertToJpegReduced(
                    bitmapStream,
                    scaleFactor: 8,
                    jpegQuality: quality);

                if (processedImage?.Length > 0) {
                    var fileName = GetAttachmentFileName(imageData.ImagePath);
                    builder.Attachments.Add(fileName, processedImage.ToArray(), new MimeKit.ContentType("image", "jpeg"));
                }
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Error($"SendToEmail: Failed to attach processed image: {ex.Message}");
            }
        }

        private static string GetAttachmentFileName(string imagePath) {
            if (string.IsNullOrEmpty(imagePath)) {
                return "image.jpg";
            }

            return System.IO.Path.GetFileNameWithoutExtension(imagePath) + ".jpg";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>(EmailCommon.ValidateSettings());

            if (string.IsNullOrEmpty(Recipient)) {
                i.Add("Email recipient is missing");
            }

            if (string.IsNullOrEmpty(GroundStation.GroundStationConfig.SmtpFromAddress)) {
                i.Add("Email from address is missing");
            }

            if (string.IsNullOrEmpty(Subject)) {
                i.Add("Email subject is missing");
            }

            if (string.IsNullOrEmpty(Body)) {
                i.Add("Email body is missing");
            }

            if (i != Issues) {
                Issues = i;
                RaisePropertyChanged(nameof(Issues));
            }

            return i.Count == 0;
        }

        public override object Clone() {
            return new SendToEmail(this) {
                Recipient = Recipient,
                Subject = Subject,
                Body = Body,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {Name}, Recipient: {recipient}, Subject: {subject}";
        }

        public IWindowService WindowService {
            get {
                windowService ??= new WindowService();
                return windowService;
            }

            set => windowService = value;
        }

        // This attribute will auto generate a RelayCommand for the method. It is called <methodname>Command -> OpenConfigurationWindowCommand. The class has to be marked as partial for it to work.
        [RelayCommand]
        private async Task OpenConfigurationWindow(object o) {
            var conf = new SendEmailSetup() {
                Recipient = this.Recipient,
                Subject = this.Subject,
                Body = this.Body,
            };

            await WindowService.ShowDialog(conf, Name, System.Windows.ResizeMode.CanResize, System.Windows.WindowStyle.ThreeDBorderWindow);

            Recipient = conf.Recipient;
            Subject = conf.Subject;
            Body = conf.Body;
        }
    }

    public partial class SendEmailSetup : BaseINPC {
        [ObservableProperty]
        private string recipient;

        [ObservableProperty]
        private string subject;

        [ObservableProperty]
        private string body;
    }
}