using APSIM.Shared.Utilities;
using MathNet.Numerics.LinearAlgebra;
using Models.Core;
using Models.Functions;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace Models.PROSAIL
{
    /// <summary>
    /// Handles file loading, path resolution, and parameter resolution for ProsailModel.
    /// Includes CSV loaders for observation data, custom SRF, and wet/dry soil reflectance,
    /// as well as wavelength parsing and per-date parameter resolution.
    /// </summary>
    public static class ProsailInputLoader
    {
        /// <summary>
        /// Resolves a potentially relative file path against the simulation file directory.
        /// </summary>
        /// <param name="path">File path (may be relative).</param>
        /// <param name="simulationFileName">Full path to the simulation file.</param>
        /// <returns>Absolute file path.</returns>
        public static string ResolvePath(string path, string simulationFileName)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(Path.GetDirectoryName(simulationFileName), path);
        }

        /// <summary>
        /// Parses the wavelength range from the specified string and returns the list of wavelengths.
        /// Supports ranges (e.g., "400-500"), lists (e.g., "400, 500, 600"),
        /// and mixed formats (e.g., "400, 500-600, 700").
        /// </summary>
        /// <param name="inputWavelengthRange">The wavelength range string to parse.</param>
        /// <param name="writeMessage">Logging callback.</param>
        /// <returns>A sorted list of wavelengths (in nm). Returns default 400-2500 if input is empty.</returns>
        public static List<double> ParseWavelengthRange(string inputWavelengthRange,
            Action<LogLevel, string> writeMessage)
        {
            List<double> wavelengths = new List<double>();

            if (string.IsNullOrWhiteSpace(inputWavelengthRange))
            {
                writeMessage(LogLevel.Info, "ProsailModel: InputWavelengthRange is empty, using default range 400-2500 nm.");
                for (int wl = 400; wl <= 2500; wl++)
                    wavelengths.Add(wl);
                return wavelengths;
            }

            string[] parts = inputWavelengthRange.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                writeMessage(LogLevel.Warning, "ProsailModel: InputWavelengthRange is empty after splitting.");
                return wavelengths;
            }

            foreach (string part in parts)
            {
                if (part.Contains('-'))
                {
                    string[] rangeParts = part.Split('-', StringSplitOptions.TrimEntries);
                    if (rangeParts.Length != 2)
                    {
                        writeMessage(LogLevel.Warning, $"ProsailModel: Invalid wavelength range format: {part}.");
                        continue;
                    }
                    if (!double.TryParse(rangeParts[0], out double startWavelength) || !double.TryParse(rangeParts[1], out double endWavelength))
                    {
                        writeMessage(LogLevel.Warning, $"ProsailModel: Failed to parse wavelength range values: {part}.");
                        continue;
                    }
                    if (startWavelength < 0 || endWavelength < startWavelength)
                    {
                        writeMessage(LogLevel.Warning, $"ProsailModel: Invalid wavelength range values (start < 0 or end < start): {part}.");
                        continue;
                    }
                    for (int wl = (int)Math.Ceiling(startWavelength); wl <= (int)Math.Floor(endWavelength); wl++)
                        wavelengths.Add(wl);
                    writeMessage(LogLevel.Info, $"ProsailModel: Parsed wavelength range: {startWavelength}-{endWavelength} nm.");
                }
                else
                {
                    if (!double.TryParse(part, out double wavelength))
                    {
                        writeMessage(LogLevel.Warning, $"ProsailModel: Failed to parse wavelength value: {part}.");
                        continue;
                    }
                    if (wavelength < 0)
                    {
                        writeMessage(LogLevel.Warning, $"ProsailModel: Invalid wavelength value (wavelength < 0): {part}.");
                        continue;
                    }
                    wavelengths.Add(wavelength);
                    writeMessage(LogLevel.Info, $"ProsailModel: Parsed single wavelength: {wavelength} nm.");
                }
            }

            wavelengths = wavelengths.Distinct().OrderBy(w => w).ToList();

            if (wavelengths.Count == 0)
                writeMessage(LogLevel.Warning, $"ProsailModel: No valid wavelengths parsed from: {inputWavelengthRange}.");
            else
                writeMessage(LogLevel.Info, $"ProsailModel: Total wavelengths parsed: {wavelengths.Count}.");

            return wavelengths;
        }

        /// <summary>
        /// Result of loading observation data from a CSV file.
        /// </summary>
        public class ObservationData
        {
            /// <summary>Observation dates loaded from the CSV.</summary>
            public DateTime[] Dates { get; set; }
            /// <summary>Per-date Psoil values (null if column not present).</summary>
            public double[] PsoilValues { get; set; }
            /// <summary>Per-date sun zenith angle values (null if column not present).</summary>
            public double[] SunZenithAngleValues { get; set; }
            /// <summary>Per-date observer zenith angle values (null if column not present).</summary>
            public double[] ObserverZenithAngleValues { get; set; }
            /// <summary>Per-date relative azimuth angle values (null if column not present).</summary>
            public double[] RelativeAzimuthAngleValues { get; set; }
        }

        /// <summary>
        /// Loads observation dates and per-date parameters from a CSV file.
        /// Expected columns: Date (required), Psoil, SunZenithAngle, ObserverZenithAngle, RelativeAzimuthAngle (all optional).
        /// Uses APSIM's ApsimTextFile.ToTable() for CSV parsing.
        /// </summary>
        /// <param name="filePath">Resolved path to the CSV file.</param>
        /// <returns>ObservationData containing dates and optional per-date parameter arrays.</returns>
        public static ObservationData LoadObservationDataFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"ProsailModel: Observation data file not found: {filePath}");

            DataTable table = ApsimTextFile.ToTable(filePath);

            if (!table.Columns.Contains("Date"))
                throw new InvalidOperationException("ProsailModel: Observation data CSV must have a 'Date' column.");

            int nRows = table.Rows.Count;
            var dates = new DateTime[nRows];
            var psoil = new List<double>();
            var tts = new List<double>();
            var tto = new List<double>();
            var psi = new List<double>();

            bool hasPsoil = table.Columns.Contains("Psoil");
            bool hasTTS = table.Columns.Contains("SunZenithAngle");
            bool hasTTO = table.Columns.Contains("ObserverZenithAngle");
            bool hasPSI = table.Columns.Contains("RelativeAzimuthAngle");

            for (int i = 0; i < nRows; i++)
            {
                DataRow row = table.Rows[i];
                dates[i] = Convert.ToDateTime(row["Date"]).Date;
                if (hasPsoil) psoil.Add(Convert.ToDouble(row["Psoil"]));
                if (hasTTS) tts.Add(Convert.ToDouble(row["SunZenithAngle"]));
                if (hasTTO) tto.Add(Convert.ToDouble(row["ObserverZenithAngle"]));
                if (hasPSI) psi.Add(Convert.ToDouble(row["RelativeAzimuthAngle"]));
            }

            return new ObservationData
            {
                Dates = dates,
                PsoilValues = hasPsoil ? psoil.ToArray() : null,
                SunZenithAngleValues = hasTTS ? tts.ToArray() : null,
                ObserverZenithAngleValues = hasTTO ? tto.ToArray() : null,
                RelativeAzimuthAngleValues = hasPSI ? psi.ToArray() : null
            };
        }

        /// <summary>
        /// Loads a SpectralResponseFunction from a CSV file.
        /// First column = wavelength (nm). Remaining columns = band SRF values (column headers = band names).
        /// </summary>
        /// <param name="filePath">Resolved path to the CSV file.</param>
        /// <returns>SpectralResponseFunction populated from the CSV data.</returns>
        public static SpectralResponseFunction LoadSRFFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"ProsailModel: Custom SRF file not found: {filePath}");

            DataTable table = ApsimTextFile.ToTable(filePath);
            if (table.Columns.Count < 2)
                throw new InvalidOperationException("SRF CSV must have at least 2 columns (wavelength + 1 band).");

            int nWavelengths = table.Rows.Count;
            int nBands = table.Columns.Count - 1;

            double[] wavelengths = new double[nWavelengths];
            var spectralResponse = new List<double[]>();
            var bandNames = new object[nBands];
            var centralWavelengths = new double[nBands];

            for (int i = 0; i < nWavelengths; i++)
                wavelengths[i] = Convert.ToDouble(table.Rows[i][0]);

            for (int b = 0; b < nBands; b++)
            {
                bandNames[b] = table.Columns[b + 1].ColumnName;
                double[] bandSrf = new double[nWavelengths];
                double weightedSum = 0, totalWeight = 0;
                for (int i = 0; i < nWavelengths; i++)
                {
                    bandSrf[i] = Convert.ToDouble(table.Rows[i][b + 1]);
                    weightedSum += wavelengths[i] * bandSrf[i];
                    totalWeight += bandSrf[i];
                }
                spectralResponse.Add(bandSrf);
                centralWavelengths[b] = totalWeight > 0 ? weightedSum / totalWeight : 0;
            }

            return new SpectralResponseFunction
            {
                SpectralResponse = spectralResponse,
                OriginalBandWavelength = wavelengths,
                CentralWavelength = centralWavelengths,
                SpectralBandName = bandNames
            };
        }

        /// <summary>
        /// Loads wet/dry soil reflectance from a CSV file.
        /// Expected columns: Wavelength, Dry_Soil, Wet_Soil.
        /// </summary>
        /// <param name="filePath">Resolved path to the CSV file.</param>
        /// <returns>WetDrySoilReflectance populated from the CSV data.</returns>
        public static WetDrySoilReflectance LoadWetDrySoilReflectanceFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"ProsailModel: Soil reflectance CSV not found: {filePath}");

            DataTable table = ApsimTextFile.ToTable(filePath);

            if (!table.Columns.Contains("Wavelength") || !table.Columns.Contains("Dry_Soil") || !table.Columns.Contains("Wet_Soil"))
                throw new InvalidOperationException("Soil reflectance CSV must have columns: Wavelength, Dry_Soil, Wet_Soil.");

            int n = table.Rows.Count;
            double[] wavelength = new double[n];
            double[] drySoil = new double[n];
            double[] wetSoil = new double[n];

            for (int i = 0; i < n; i++)
            {
                wavelength[i] = Convert.ToDouble(table.Rows[i]["Wavelength"]);
                drySoil[i] = Convert.ToDouble(table.Rows[i]["Dry_Soil"]);
                wetSoil[i] = Convert.ToDouble(table.Rows[i]["Wet_Soil"]);
            }

            return new WetDrySoilReflectance(
                Vector<double>.Build.DenseOfArray(wavelength),
                Vector<double>.Build.DenseOfArray(drySoil),
                Vector<double>.Build.DenseOfArray(wetSoil)
            );
        }

        /// <summary>
        /// Resolves a parameter value for the current date.
        /// Priority: per-date array > expression > hardcoded default > auto-calc (null).
        /// </summary>
        /// <param name="perDateValues">Per-date array (one per observation date, or single value for all).</param>
        /// <param name="expression">Expression string to evaluate as fallback.</param>
        /// <param name="paramName">Parameter name for error messages.</param>
        /// <param name="today">Current simulation date.</param>
        /// <param name="observationDateLookup">Lookup from date to index (null in daily mode).</param>
        /// <param name="evaluateExpression">Function to evaluate APSIM expressions.</param>
        /// <param name="allowAutoCalc">If true, returns null when no value can be resolved (for Psoil auto-calc).</param>
        /// <param name="defaultValue">Hardcoded default value if expression is empty.</param>
        /// <param name="writeMessage">Logging callback.</param>
        /// <returns>Resolved value, or null if allowAutoCalc and no value available.</returns>
        public static double? ResolveObservationParameter(double[] perDateValues, string expression,
            string paramName, DateTime today, Dictionary<DateTime, int> observationDateLookup,
            Func<string, double> evaluateExpression,
            bool allowAutoCalc = false, double? defaultValue = null,
            Action<LogLevel, string> writeMessage = null)
        {
            // Per-date array (single value or indexed by date)
            if (perDateValues != null && perDateValues.Length > 0)
            {
                if (perDateValues.Length == 1)
                    return perDateValues[0];

                if (observationDateLookup != null && observationDateLookup.TryGetValue(today.Date, out int idx)
                    && idx < perDateValues.Length)
                    return perDateValues[idx];

                throw new InvalidOperationException(
                    $"ProsailModel: Cannot resolve {paramName} for {today:yyyy-MM-dd}.");
            }

            // Expression fallback
            if (!string.IsNullOrWhiteSpace(expression))
                return evaluateExpression(expression);

            // Hardcoded default
            if (defaultValue.HasValue)
            {
                writeMessage?.Invoke(LogLevel.Info, $"ProsailModel: {paramName} not specified, using default {defaultValue.Value}.");
                return defaultValue.Value;
            }

            // Auto-calc signal (Psoil)
            if (allowAutoCalc) return null;

            throw new InvalidOperationException($"ProsailModel: {paramName} must be specified.");
        }

        /// <summary>
        /// Validates that a per-date array has a compatible length with the observation dates.
        /// Valid lengths: null/empty, 1 (broadcast), or exactly matching the number of observation dates.
        /// </summary>
        /// <param name="values">The per-date value array to validate.</param>
        /// <param name="name">Parameter name for error messages.</param>
        /// <param name="observationDateCount">Number of observation dates.</param>
        public static void ValidatePerDateArray(double[] values, string name, int observationDateCount)
        {
            if (values != null && values.Length > 1 && values.Length != observationDateCount)
                throw new InvalidOperationException(
                    $"ProsailModel: {name} has {values.Length} values but there are {observationDateCount} observation dates. "
                    + "Provide empty, a single value, or one value per date.");
        }
    }
}
