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
    /// IStarDetectionAnalysis and ImageMetaData. While some reflection is necessary for
    /// plugin-specific properties, this class encapsulates that logic in focused methods
    /// rather than spreading it throughout the codebase.
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
            return GetPropertyFromStarDetectionAnalysis("HFR");
        }

        /// <summary>
        /// Attempts to extract Eccentricity value from the star detection analysis.
        /// </summary>
        public double? GetEccentricity() {
            return GetPropertyFromStarDetectionAnalysis("Eccentricity");
        }

        /// <summary>
        /// Attempts to extract FWHM (Full Width at Half Maximum) value. FWHM typically comes from
        /// the Hocus Focus plugin which stores it in ImageMetaData or in a nested HocusFocus property.
        /// Tries multiple common property name variants and nested locations.
        /// </summary>
        public double? GetFWHM() {
            // Try common FWHM property names, including Hocus Focus variants
            return GetPropertyFromImageMetadata("FWHM", "FocuserFWHM", "HocusFocusFWHM", "StarFWHM", "FocusValue", "Focus_Value");
        }

        /// <summary>
        /// Attempts to extract the total guiding RMS value. Tries multiple common property name
        /// variants as different guiding plugins may use different naming conventions.
        /// </summary>
        public double? GetGuidingRmsTotal() {
            return GetPropertyFromImageMetadata("GuidingRmsTotal", "GuidingRms_Total", "GuidingRmsTot", "GuidingRMS_Tot", "GuidingRMS", "GuidingRms");
        }

        /// <summary>
        /// Attempts to extract the declination guiding RMS value. Tries multiple common property
        /// name variants as different guiding plugins may use different naming conventions.
        /// </summary>
        public double? GetGuidingRmsDec() {
            return GetPropertyFromImageMetadata("GuidingRmsDec", "GuidingRMS_Dec", "GuidingDecRms", "GuidingRms_Dec", "GuidingRMSDec");
        }

        /// <summary>
        /// Attempts to extract the right ascension guiding RMS value. Tries multiple common property
        /// name variants as different guiding plugins may use different naming conventions.
        /// </summary>
        public double? GetGuidingRmsRa() {
            return GetPropertyFromImageMetadata("GuidingRmsRa", "GuidingRMS_RA", "GuidingRaRms", "GuidingRms_RA", "GuidingRMSRa");
        }

        /// <summary>
        /// Safely extracts a numeric property from StarDetectionAnalysis using reflection.
        /// IStarDetectionAnalysis may have HFR directly accessible, or it may be in a nested
        /// structure depending on which plugins are active.
        /// </summary>
        private double? GetPropertyFromStarDetectionAnalysis(params string[] propertyNames) {
            if (imageData?.StarDetectionAnalysis == null) {
                return null;
            }

            var sda = imageData.StarDetectionAnalysis;
            var sdaType = sda.GetType();

            // First, try direct properties on the IStarDetectionAnalysis object
            foreach (var propName in propertyNames) {
                try {
                    var prop = sdaType.GetProperty(propName, PropertyLookupFlags);
                    if (prop != null && prop.CanRead) {
                        var value = prop.GetValue(sda);
                        if (TryConvertToDouble(value, out var result) && !double.IsNaN(result)) {
                            return result;
                        }
                    }
                } catch {
                    // Property doesn't exist or couldn't be accessed, try next
                }
            }

            // If not found directly, search nested properties (some plugins wrap analysis data)
            try {
                foreach (var prop in sdaType.GetProperties(PropertyLookupFlags)) {
                    try {
                        if (!prop.CanRead) continue;

                        var containerObj = prop.GetValue(sda);
                        if (containerObj == null) continue;

                        var containerType = containerObj.GetType();
                        foreach (var propName in propertyNames) {
                            var nestedProp = containerType.GetProperty(propName, PropertyLookupFlags);
                            if (nestedProp != null && nestedProp.CanRead) {
                                var value = nestedProp.GetValue(containerObj);
                                if (TryConvertToDouble(value, out var result) && !double.IsNaN(result)) {
                                    return result;
                                }
                            }
                        }
                    } catch {
                        // Continue to next property
                    }
                }
            } catch {
                // Ignore any errors during nested search
            }

            return null;
        }

        /// <summary>
        /// Safely extracts a numeric property from ImageMetaData using reflection.
        /// Tries direct property access first, then searches one level deep in nested
        /// properties (for plugin containers like Hocus Focus).
        /// </summary>
        private double? GetPropertyFromImageMetadata(params string[] propertyNames) {
            if (imageData?.ImageMetaData == null) {
                return null;
            }

            // Try direct property access first
            foreach (var propName in propertyNames) {
                try {
                    var prop = imageData.ImageMetaData.GetType().GetProperty(
                        propName,
                        PropertyLookupFlags);

                    if (prop != null && prop.CanRead) {
                        var value = prop.GetValue(imageData.ImageMetaData);
                        if (TryConvertToDouble(value, out var result) && !double.IsNaN(result)) {
                            return result;
                        }
                    }
                } catch {
                    // Property doesn't exist or couldn't be accessed, continue to next variant
                }
            }

            // Search one level deep in nested properties (e.g., plugin containers)
            try {
                foreach (var prop in imageData.ImageMetaData.GetType().GetProperties(PropertyLookupFlags)) {
                    try {
                        if (!prop.CanRead) continue;

                        var containerObj = prop.GetValue(imageData.ImageMetaData);
                        if (containerObj == null) continue;

                        // Search for the property within this container
                        var containerType = containerObj.GetType();
                        foreach (var propName in propertyNames) {
                            var nestedProp = containerType.GetProperty(
                                propName,
                                PropertyLookupFlags);

                            if (nestedProp != null && nestedProp.CanRead) {
                                var value = nestedProp.GetValue(containerObj);
                                if (TryConvertToDouble(value, out var result) && !double.IsNaN(result)) {
                                    return result;
                                }
                            }
                        }

                        // For Hocus Focus specifically, also search two levels deep if this looks like a plugin container
                        if (containerType.Name.Contains("Focus") || containerType.Name.Contains("Hocus") || containerType.Namespace?.Contains("Focus") == true) {
                            foreach (var subProp in containerType.GetProperties(PropertyLookupFlags)) {
                                try {
                                    if (!subProp.CanRead) continue;
                                    var subObj = subProp.GetValue(containerObj);
                                    if (subObj == null) continue;

                                    var subType = subObj.GetType();
                                    foreach (var propName in propertyNames) {
                                        var deepProp = subType.GetProperty(propName, PropertyLookupFlags);
                                        if (deepProp != null && deepProp.CanRead) {
                                            var value = deepProp.GetValue(subObj);
                                            if (TryConvertToDouble(value, out var result) && !double.IsNaN(result)) {
                                                return result;
                                            }
                                        }
                                    }
                                } catch {
                                    // Continue searching
                                }
                            }
                        }
                    } catch {
                        // Continue to next container property
                    }
                }
            } catch {
                // Ignore any errors during nested property search
            }

            return null;
        }

        /// <summary>
        /// Safely converts a value to double, handling various numeric types.
        /// </summary>
        private static bool TryConvertToDouble(object value, out double result) {
            result = double.NaN;
            
            if (value == null) {
                return false;
            }

            try {
                result = value switch {
                    double d => d,
                    float f => (double)f,
                    int i => (double)i,
                    decimal m => (double)m,
                    long l => (double)l,
                    short s => (double)s,
                    byte b => (double)b,
                    _ => double.NaN
                };
                return !double.IsNaN(result);
            } catch {
                return false;
            }
        }
    }
}
