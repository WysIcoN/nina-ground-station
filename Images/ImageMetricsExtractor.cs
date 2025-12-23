#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using System;
using System.Reflection;

namespace DaleGhent.NINA.GroundStation.Images {
    /// <summary>
    /// Extracts image metrics (HFR, FWHM, Eccentricity) and guiding RMS values from image data.
    /// This class acts as a centralized adapter for safely accessing metrics from NINA's
    /// IStarDetectionAnalysis and ImageMetaData. Reflection is only used for FWHM and Eccentricity
    /// as the interface IStarDetectionAnalysis does not expose them directly.
    /// </summary>
    internal class ImageMetricsExtractor {
        private readonly ImageData imageData;
        private const BindingFlags PropertyLookupFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        public ImageMetricsExtractor(ImageData imageData) {
            this.imageData = imageData;
        }

        /// <summary>
        /// Attempts to extract HFR (Half-Flux Radius) value from the star detection analysis.
        /// </summary>
        public double? GetHFR() {
            return imageData?.StarDetectionAnalysis == null ? 0.0 : imageData.StarDetectionAnalysis.HFR;
        }

        /// <summary>
        /// Attempts to extract Eccentricity value from the star detection analysis.
        /// NOTE that the Hocus Focus plugin must be active for this value to be available.
        /// </summary>
        public double? GetEccentricity() {
            var sda = imageData?.StarDetectionAnalysis;
            
            if (sda == null) {
                // Hocus Focus is not active.
                return null;
            }

            return GetPropertyFromStarDetectionAnalysis("Eccentricity");
        }

        /// <summary>
        /// Attempts to extract FWHM (Full Width at Half Maximum) value.
        /// NOTE that the Hocus Focus plugin must be active for this value to be available.
        /// </summary>
        public double? GetFWHM() {
            var sda = imageData?.StarDetectionAnalysis;
            
            if (sda == null) {
                // Hocus Focus is not active.
                return null;
            }

            return GetPropertyFromStarDetectionAnalysis("FWHM");
        }

        /// <summary>
        /// Attempts to extract the total guiding RMS value. Tries multiple common property name
        /// variants as different guiding plugins may use different naming conventions.
        /// </summary>
        public double? GetGuidingRmsTotal() {
            return imageData?.ImageMetaData == null ? 0.0 : imageData.ImageMetaData.Image.RecordedRMS.Total * imageData.ImageMetaData.Image.RecordedRMS.Scale;
        }

        /// <summary>
        /// Attempts to extract the declination guiding RMS value. Tries multiple common property
        /// name variants as different guiding plugins may use different naming conventions.
        /// </summary>
        public double? GetGuidingRmsDec() {
            return imageData?.ImageMetaData == null ? 0.0 : imageData.ImageMetaData.Image.RecordedRMS.Dec * imageData.ImageMetaData.Image.RecordedRMS.Scale;
        }

        /// <summary>
        /// Attempts to extract the right ascension guiding RMS value. Tries multiple common property
        /// name variants as different guiding plugins may use different naming conventions.
        /// </summary>
        public double? GetGuidingRmsRa() {
            return imageData?.ImageMetaData == null ? 0.0 : imageData.ImageMetaData.Image.RecordedRMS.RA * imageData.ImageMetaData.Image.RecordedRMS.Scale;
        }

        /// <summary>
        /// Attempts to extract the exposure time in seconds from the image metadata.
        /// </summary>
        public double? GetExposureTime() {
            return imageData?.ImageMetaData == null ? 0.0 : imageData.ImageMetaData.Image.ExposureTime;
        }

        /// <summary>
        /// Safely extracts a numeric property from StarDetectionAnalysis using reflection.
        /// IStarDetectionAnalysis does not have FWHM and Eccentricity properties directly, so we use
        /// reflection to attempt to access these properties.
        /// </summary>
        private double? GetPropertyFromStarDetectionAnalysis(string propertyName) {
            if (imageData?.StarDetectionAnalysis == null) {
                return null;
            }

            var sda = imageData.StarDetectionAnalysis;
            var sdaType = sda.GetType();

            try {
                var prop = sdaType.GetProperty(propertyName, PropertyLookupFlags);
                if (prop != null && prop.CanRead) {
                    var value = prop.GetValue(sda);
                    var result = Convert.ToDouble(value);
                    if (!double.IsNaN(result)) {
                        return result;
                    }
                }
            } catch {
                // Property doesn't exist or couldn't be accessed
            }

            return null;
        }
    }
}
