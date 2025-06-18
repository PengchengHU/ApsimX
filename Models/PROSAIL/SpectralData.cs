using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

/// <summary>
/// Interface for spectral data types that support wavelength subsetting
/// </summary>
public interface ISpectralData
{
    /// <summary>Gets the wavelengths in nanometers (nm)</summary>
    double[] GetWavelengths();

    /// <summary>Gets the wavelength-to-index mapping dictionary</summary>
    Dictionary<double, int> GetWavelengthToIndex();

    /// <summary>Indicates whether this object contains valid spectral data</summary>
    bool HasValue { get; }
}

/// <summary>
/// Generic wavelength subsetting utility
/// </summary>
public static class SpectralDataUtils
{
    /// <summary>
    /// Subsets spectral data to specified wavelengths using a generic approach
    /// </summary>
    /// <typeparam name="T">Type implementing ISpectralData</typeparam>
    /// <param name="source">Source spectral data object</param>
    /// <param name="targetWavelengths">Array of target wavelengths to extract (nm)</param>
    /// <param name="subsetFunction">Function to create subset of the original data</param>
    /// <returns>Subset spectral data object</returns>
    /// <exception cref="ArgumentNullException">Thrown when source or targetWavelengths is null</exception>
    /// <exception cref="ArgumentException">Thrown when target wavelength is not found in source data</exception>
    public static T SubsetByWavelengths<T>(T source, double[] targetWavelengths,
        Func<T, int[], T> subsetFunction) where T : ISpectralData
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (targetWavelengths == null || targetWavelengths.Length == 0)
            throw new ArgumentNullException(nameof(targetWavelengths));
        if (!source.HasValue)
            throw new ArgumentException("Source spectral data is invalid or empty", nameof(source));

        var wavelengthToIndex = source.GetWavelengthToIndex();
        if (wavelengthToIndex == null)
            throw new ArgumentException("Source data does not have wavelength-to-index mapping initialized", nameof(source));

        // Validate that all target wavelengths exist in source data
        var missingWavelengths = targetWavelengths.Where(w => !wavelengthToIndex.ContainsKey(w)).ToArray();
        if (missingWavelengths.Any())
        {
            throw new ArgumentException($"Wavelengths not found in source data: {string.Join(", ", missingWavelengths)} nm");
        }

        // Get indices for target wavelengths
        var indices = targetWavelengths.Select(w => wavelengthToIndex[w]).ToArray();

        return subsetFunction(source, indices);
    }
}

/// <summary>
/// Contains leaf optical constants required for PROSPECT calculations
/// </summary>
public struct LeafOpticalConsts : ISpectralData
{
    /// <summary>Wavelength array in nanometers (nm)</summary>
    public Vector<double> Wavelength { get; set; }

    /// <summary>Refractive index (unitless)</summary>
    public Vector<double> RefractiveIndex { get; set; }

    /// <summary>Specific absorption coefficient for chlorophyll a + b (m²/g)</summary>
    public Vector<double> SAC_CAB { get; set; }

    /// <summary>Specific absorption coefficient for carotenoids (m²/g)</summary>
    public Vector<double> SAC_CAR { get; set; }

    /// <summary>Specific absorption coefficient for water (m²/g)</summary>
    public Vector<double> SAC_EWT { get; set; }

    /// <summary>Specific absorption coefficient for dry matter (m²/g)</summary>
    public Vector<double> SAC_LMA { get; set; }

    /// <summary>Transmissivity at 40° incidence angle (unitless)</summary>
    public Vector<double> Tav40 { get; set; }

    /// <summary>Transmissivity at 90° incidence angle (unitless)</summary>
    public Vector<double> Tav90 { get; set; }

    /// <summary>Specific absorption coefficient for anthocyanins (m²/g)</summary>
    public Vector<double> SAC_ANT { get; set; }

    /// <summary>Specific absorption coefficient for brown pigments (m²/g)</summary>
    public Vector<double> SAC_BROWN { get; set; }

    /// <summary>Specific absorption coefficient for proteins (m²/g)</summary>
    public Vector<double> SAC_PROT { get; set; }

    /// <summary>Specific absorption coefficient for non-protein carbon-based constituents (m²/g)</summary>
    public Vector<double> SAC_CBC { get; set; }

    /// <summary>Dictionary mapping wavelengths to their indices in the Wavelength array (for optimized filtering)</summary>
    public Dictionary<double, int> WavelengthToIndex { get; set; }

    /// <summary>
    /// Indicates whether this LeafOpticalConsts object contains valid data
    /// </summary>
    public readonly bool HasValue => Wavelength != null && RefractiveIndex != null &&
                                   WavelengthToIndex != null && Wavelength.Count > 0;

    /// <summary>
    /// Constructor that initializes all properties and creates WavelengthToIndex mapping
    /// </summary>
    /// <param name="wavelength">Wavelength vector (nm)</param>
    /// <param name="refractiveIndex">Refractive index vector</param>
    /// <param name="sacCAB">Chlorophyll absorption coefficient vector</param>
    /// <param name="sacCAR">Carotenoid absorption coefficient vector</param>
    /// <param name="sacEWT">Water absorption coefficient vector</param>
    /// <param name="sacLMA">Dry matter absorption coefficient vector</param>
    /// <param name="tav40">Transmissivity at 40° vector</param>
    /// <param name="tav90">Transmissivity at 90° vector</param>
    /// <param name="sacANT">Anthocyanin absorption coefficient vector</param>
    /// <param name="sacBROWN">Brown pigment absorption coefficient vector</param>
    /// <param name="sacPROT">Protein absorption coefficient vector</param>
    /// <param name="sacCBC">Carbon-based constituent absorption coefficient vector</param>
    public LeafOpticalConsts(Vector<double> wavelength, Vector<double> refractiveIndex,
        Vector<double> sacCAB, Vector<double> sacCAR, Vector<double> sacEWT, Vector<double> sacLMA,
        Vector<double> tav40, Vector<double> tav90, Vector<double> sacANT, Vector<double> sacBROWN,
        Vector<double> sacPROT, Vector<double> sacCBC)
    {
        Wavelength = wavelength;
        RefractiveIndex = refractiveIndex;
        SAC_CAB = sacCAB;
        SAC_CAR = sacCAR;
        SAC_EWT = sacEWT;
        SAC_LMA = sacLMA;
        Tav40 = tav40;
        Tav90 = tav90;
        SAC_ANT = sacANT;
        SAC_BROWN = sacBROWN;
        SAC_PROT = sacPROT;
        SAC_CBC = sacCBC;

        WavelengthToIndex = wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                     .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength?.ToArray() ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of LeafOpticalConsts for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New LeafOpticalConsts with subset data</returns>
    public LeafOpticalConsts SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            var subsetWavelength = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.Wavelength[i]));
            return new LeafOpticalConsts(
                subsetWavelength,
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.RefractiveIndex[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_CAB[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_CAR[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_EWT[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_LMA[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.Tav40[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.Tav90[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_ANT[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_BROWN[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_PROT[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.SAC_CBC[i]))
            );
        });
    }
}

/// <summary>
/// Holds leaf optical properties (reflectance and transmittance spectra).
/// Typically output from the PROSPECT model.
/// </summary>
public struct LeafOptics : ISpectralData
{
    /// <summary>Wavelength array in nanometers (nm)</summary>
    public double[] Wavelength { get; set; }

    /// <summary>Leaf reflectance spectrum (unitless fraction, 0-1)</summary>
    public double[] Reflectance { get; set; }

    /// <summary>Leaf transmittance spectrum (unitless fraction, 0-1)</summary>
    public double[] Transmittance { get; set; }

    /// <summary>Dictionary mapping wavelengths to their indices for optimized access</summary>
    public Dictionary<double, int> WavelengthToIndex { get; set; }

    /// <summary>
    /// Indicates whether this LeafOptics object contains valid data.
    /// Returns false if Wavelength, Reflectance, or Transmittance is null or empty.
    /// </summary>
    public readonly bool HasValue => Wavelength != null && Reflectance != null && Transmittance != null &&
                                   Wavelength.Length > 0 && Wavelength.Length == Reflectance.Length &&
                                   Wavelength.Length == Transmittance.Length;

    /// <summary>
    /// Constructor that automatically creates WavelengthToIndex mapping
    /// </summary>
    /// <param name="wavelength">Array of wavelengths (nm)</param>
    /// <param name="reflectance">Array of reflectance values (0-1)</param>
    /// <param name="transmittance">Array of transmittance values (0-1)</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths</exception>
    public LeafOptics(double[] wavelength, double[] reflectance, double[] transmittance)
    {
        if (wavelength?.Length != reflectance?.Length || wavelength?.Length != transmittance?.Length)
            throw new ArgumentException("Wavelength, reflectance, and transmittance arrays must have the same length");

        Wavelength = wavelength;
        Reflectance = reflectance;
        Transmittance = transmittance;
        WavelengthToIndex = wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                      .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of LeafOptics for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New LeafOptics with subset data</returns>
    public LeafOptics SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            return new LeafOptics(
                indices.Select(i => source.Wavelength[i]).ToArray(),
                indices.Select(i => source.Reflectance[i]).ToArray(),
                indices.Select(i => source.Transmittance[i]).ToArray()
            );
        });
    }
}

/// <summary>
/// Holds atmospheric sensor spectral data (wavelengths, direct/diffuse irradiance).
/// Used for atmospheric correction and illumination modeling.
/// </summary>
public class SpecAtmSensor : ISpectralData
{
    /// <summary>
    /// Wavelengths in nanometers (nm)
    /// </summary>
    public double[] Wavelength { get; set; }

    /// <summary>
    /// Direct solar radiation spectrum (W/m²/nm)
    /// </summary>
    public double[] DirectLight { get; set; }

    /// <summary>
    /// Diffuse sky radiation spectrum (W/m²/nm)
    /// </summary>
    public double[] DiffuseLight { get; set; }

    /// <summary>
    /// Dictionary mapping wavelengths to their indices for optimized access
    /// </summary>
    public Dictionary<double, int> WavelengthToIndex { get; set; }

    /// <summary>
    /// Indicates whether this object contains valid spectral data
    /// </summary>
    public bool HasValue => Wavelength != null && DirectLight != null && DiffuseLight != null &&
                           Wavelength.Length > 0 && Wavelength.Length == DirectLight.Length &&
                           Wavelength.Length == DiffuseLight.Length;

    /// <summary>
    /// Default constructor
    /// </summary>
    public SpecAtmSensor()
    {
        WavelengthToIndex = new Dictionary<double, int>();
    }

    /// <summary>
    /// Constructor with automatic WavelengthToIndex initialization
    /// </summary>
    /// <param name="wavelength">Array of wavelengths (nm)</param>
    /// <param name="directLight">Array of direct light values (W/m²/nm)</param>
    /// <param name="diffuseLight">Array of diffuse light values (W/m²/nm)</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths</exception>
    public SpecAtmSensor(double[] wavelength, double[] directLight, double[] diffuseLight)
    {
        if (wavelength?.Length != directLight?.Length || wavelength?.Length != diffuseLight?.Length)
            throw new ArgumentException("All arrays must have the same length");

        Wavelength = wavelength;
        DirectLight = directLight;
        DiffuseLight = diffuseLight;
        InitializeWavelengthToIndex();
    }

    /// <summary>
    /// Initialize or refresh the WavelengthToIndex dictionary
    /// </summary>
    public void InitializeWavelengthToIndex()
    {
        WavelengthToIndex = Wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                     .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of SpecAtmSensor for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New SpecAtmSensor with subset data</returns>
    public SpecAtmSensor SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            return new SpecAtmSensor(
                indices.Select(i => source.Wavelength[i]).ToArray(),
                indices.Select(i => source.DirectLight[i]).ToArray(),
                indices.Select(i => source.DiffuseLight[i]).ToArray()
            );
        });
    }
}

/// <summary>
/// Helper class for JSON deserialization of wet/dry soil reflectance data
/// </summary>
public class WetDrySoilReflectanceDataJason : ISpectralData
{
    /// <summary>Wavelengths in nanometers (nm)</summary>
    public double[] Wavelength { get; set; }

    /// <summary>Soil reflectance spectrum of dry soil (unitless fraction, 0-1)</summary>
    public double[] Dry_Soil { get; set; }

    /// <summary>Soil reflectance spectrum of wet soil (unitless fraction, 0-1)</summary>
    public double[] Wet_Soil { get; set; }

    /// <summary>Dictionary mapping wavelengths to their indices for optimized access</summary>
    public Dictionary<double, int> WavelengthToIndex { get; set; }

    /// <summary>Indicates whether this object contains valid spectral data</summary>
    public bool HasValue => Wavelength != null && Dry_Soil != null && Wet_Soil != null &&
                           Wavelength.Length > 0 && Wavelength.Length == Dry_Soil.Length &&
                           Wavelength.Length == Wet_Soil.Length;

    /// <summary>
    /// Default constructor
    /// </summary>
    public WetDrySoilReflectanceDataJason()
    {
        WavelengthToIndex = new Dictionary<double, int>();
    }

    /// <summary>
    /// Constructor with automatic WavelengthToIndex initialization
    /// </summary>
    /// <param name="wavelength">Array of wavelengths (nm)</param>
    /// <param name="drySoil">Array of dry soil reflectance values (0-1)</param>
    /// <param name="wetSoil">Array of wet soil reflectance values (0-1)</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths</exception>
    public WetDrySoilReflectanceDataJason(double[] wavelength, double[] drySoil, double[] wetSoil)
    {
        if (wavelength?.Length != drySoil?.Length || wavelength?.Length != wetSoil?.Length)
            throw new ArgumentException("All arrays must have the same length");

        Wavelength = wavelength;
        Dry_Soil = drySoil;
        Wet_Soil = wetSoil;
        InitializeWavelengthToIndex();
    }

    /// <summary>Initialize or refresh the WavelengthToIndex dictionary</summary>
    public void InitializeWavelengthToIndex()
    {
        WavelengthToIndex = Wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                     .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of WetDrySoilReflectanceData for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New WetDrySoilReflectanceData with subset data</returns>
    public WetDrySoilReflectanceDataJason SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            return new WetDrySoilReflectanceDataJason(
                indices.Select(i => source.Wavelength[i]).ToArray(),
                indices.Select(i => source.Dry_Soil[i]).ToArray(),
                indices.Select(i => source.Wet_Soil[i]).ToArray()
            );
        });
    }
}
/// <summary>
/// Holds wet/dry soil reflectance data (wavelengths and reflectance)
/// </summary>
public struct WetDrySoilReflectance : ISpectralData
{
    
    /// <summary>Wavelength array in nanometers (nm)</summary>
    public Vector<double> Wavelength { get; set; }
    /// <summary>Reflectance of dry soil</summary>
    public Vector<double> DrySoilReflectance{ get; set; }
    /// <summary>Reflectance of wet soil</summary>
    public Vector<double> WetSoilReflectance{ get; set; }
    /// <summary> Index of wavelength</summary>
    public Dictionary<double, int> WavelengthToIndex{ get; set; } 

    /// <summary>Indicates whether this object contains valid spectral data</summary>
    public bool HasValue => Wavelength != null && DrySoilReflectance != null && WetSoilReflectance != null &&
                           Wavelength.Count > 0 && Wavelength.Count == DrySoilReflectance.Count &&
                           Wavelength.Count == WetSoilReflectance.Count;

    /// <summary>
    /// Constructor that initializes all properties and creates WavelengthToIndex mapping
    /// </summary>
    /// <param name="wavelength">Wavelength vector (nm)</param>
    /// <param name="drySoilReflectance">Dry soil reflectance vector</param>
    /// <param name="wetSoilReflectance">Wet soil reflectance vector</param>
    public WetDrySoilReflectance(Vector<double> wavelength, Vector<double> drySoilReflectance,
        Vector<double> wetSoilReflectance)
    {
        Wavelength = wavelength;
        DrySoilReflectance = drySoilReflectance;
        WetSoilReflectance = wetSoilReflectance;

        WavelengthToIndex = wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                     .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength?.ToArray() ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of WetDrySoilReflectance for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New WetDrySoilReflectance with subset data</returns>
    public WetDrySoilReflectance SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            return new WetDrySoilReflectance(
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.Wavelength[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.DrySoilReflectance[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.WetSoilReflectance[i]))
            );
        });
    }
}


/// <summary>
/// Holds soil spectral data (wavelengths and reflectance) for radiative transfer modeling
/// </summary>
public struct SoilOptics : ISpectralData
{
    /// <summary>Wavelengths in nanometers (nm)</summary>
    public Vector<double> Wavelength { get; set; }

    /// <summary>Soil reflectance spectrum (unitless fraction, 0-1)</summary>
    public Vector<double> Reflectance { get; set; }

    /// <summary>Dictionary mapping wavelengths to their indices in the Wavelength array for optimized access</summary>
    public Dictionary<double, int> WavelengthToIndex { get; set; }

    /// <summary>
    /// Initializes a new instance of SoilOptics with automatic WavelengthToIndex creation
    /// </summary>
    /// <param name="wavelength">Wavelength vector (nm)</param>
    /// <param name="reflectance">Reflectance vector (0-1)</param>
    /// <exception cref="ArgumentException">Thrown when vectors have different lengths</exception>
    public SoilOptics(Vector<double> wavelength, Vector<double> reflectance)
    {
        if (wavelength?.Count != reflectance?.Count)
            throw new ArgumentException("Wavelength and reflectance vectors must have the same length");

        Wavelength = wavelength;
        Reflectance = reflectance;
        WavelengthToIndex = wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                     .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Indicates whether this SoilOptics object contains valid data.
    /// Returns false if Wavelength or Reflectance is null or empty.
    /// </summary>
    public readonly bool HasValue => Wavelength != null && Reflectance != null &&
                                   WavelengthToIndex != null && Wavelength.Count > 0 &&
                                   Wavelength.Count == Reflectance.Count;

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength?.ToArray() ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of SoilOptics for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New SoilOptics with subset data</returns>
    public SoilOptics SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            return new SoilOptics(
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.Wavelength[i])),
                Vector<double>.Build.DenseOfEnumerable(indices.Select(i => source.Reflectance[i]))
            );
        });
    }
}


/// <summary>
/// Represents the output of the SAIL models (FourSAIL, FourSAIL2).
/// Contains various reflectance factors and derived quantities like fCover and absorptance.
/// </summary>
public class CanopyOptics : ISpectralData
{
    /// <summary>Wavelength array in nanometers (nm)</summary>
    public double[] Wavelength { get; set; }
    /// <summary>Hemispherical-directional reflectance factor in viewing direction (R_o).</summary>
    public double[] Rdot { get; set; }
    /// <summary>Bi-directional reflectance factor (R_so).</summary>
    public double[] Rsot { get; set; }
    /// <summary>Bi-hemispherical reflectance factor (R_dd).</summary>
    public double[] Rddt { get; set; }
    /// <summary>Directional-hemispherical reflectance factor for solar incident flux (R_sd).</summary>
    public double[] Rsdt { get; set; }
    /// <summary>Fraction of Vegetation Cover (fCover = 1 - gap fraction in view direction).</summary>
    public double[] FCover { get; set; }
    /// <summary>Canopy absorptance for direct solar incident flux (fraction absorbed by canopy+soil system).</summary>
    public double[] Abs_dir { get; set; }
    /// <summary>Canopy absorptance for hemispherical diffuse incident flux (fraction absorbed by canopy+soil system).</summary>
    public double[] Abs_hem { get; set; }
    /// <summary>Contribution of direct solar incident flux to albedo (Hemispherical reflectance for direct incidence, Rsd*).</summary>
    public double[] Rsdstar { get; set; }
    /// <summary>Contribution of hemispherical diffuse incident flux to albedo (Hemispherical reflectance for diffuse incidence, Rdd*).</summary>
    public double[] Rddstar { get; set; }

    /// <summary>Dictionary mapping wavelengths to their indices for optimized access</summary>
    public Dictionary<double, int> WavelengthToIndex { get; set; }

    /// <summary>
    /// Default constructor for object initializer syntax
    /// </summary>
    public CanopyOptics()
    {
        WavelengthToIndex = new Dictionary<double, int>();
    }

    /// <summary>
    /// Indicates whether this LeafOptics object contains valid data.
    /// Returns false if Wavelength, Reflectance, or Transmittance is null or empty.
    /// </summary>
    public bool HasValue => Wavelength != null && Rdot != null && Rsot != null &&
                                      Rddt != null && Rsdt != null && FCover != null &&
                                        Abs_dir != null && Abs_hem != null && Rsdstar != null &&
                                        Rddstar != null && Wavelength.Length > 0 && Wavelength.Length == Rdot.Length && 
        Wavelength.Length == Rsot.Length && Wavelength.Length == Rddt.Length && Wavelength.Length == Rsdt.Length &&
        Wavelength.Length == FCover.Length && Wavelength.Length == Abs_dir.Length &&
        Wavelength.Length == Abs_hem.Length && Wavelength.Length == Rsdstar.Length &&
        Wavelength.Length == Rddstar.Length;

    /// <summary>
    /// Constructor that automatically creates WavelengthToIndex mapping
    /// </summary>
    /// <param name="wavelength">Array of wavelengths (nm)</param>
    /// <param name="rdot">Array of hemispherical-directional reflectance factor in viewing direction (R_o)</param>
    /// <param name="rsot">Array of bi-directional reflectance factor (R_so)</param>
    /// <param name="rddt">Array of bi-hemispherical reflectance factor (R_dd).</param>
    /// <param name="rsdt">Array of directional-hemispherical reflectance factor for solar incident flux (R_sd).</param>
    /// <param name="fCover">Array of fraction of Vegetation Cover (fCover = 1 - gap fraction in view direction).</param>
    /// <param name="abs_dir">Array of canopy absorptance for direct solar incident flux (fraction absorbed by canopy+soil system).</param>
    /// <param name="abs_hem">Array of canopy absorptance for hemispherical diffuse incident flux (fraction absorbed by canopy+soil system).</param>
    /// <param name="rsdstar">Array of contribution of direct solar incident flux to albedo (Hemispherical reflectance for direct incidence, Rsd*).</param>
    /// <param name="rddstar">Array of contribution of hemispherical diffuse incident flux to albedo (Hemispherical reflectance for diffuse incidence, Rdd*).</param>
    /// <exception cref="ArgumentException">Thrown when arrays have different lengths</exception>
    public CanopyOptics(double[] wavelength, double[] rdot, double[] rsot, double[] rddt, double[] rsdt, double[] fCover,
        double[] abs_dir, double[] abs_hem, double[] rsdstar, double[] rddstar)
    {
        if (wavelength?.Length != rdot?.Length || wavelength?.Length != rsot?.Length || wavelength?.Length != rddt?.Length ||
            wavelength?.Length != rsdt?.Length || wavelength?.Length != fCover?.Length ||
            wavelength?.Length != abs_dir?.Length || wavelength?.Length != abs_hem?.Length ||
            wavelength?.Length != rsdstar?.Length || wavelength?.Length != rddstar?.Length
            )
            throw new ArgumentException("Wavelength and canopy properties arrays must have the same length");

        Wavelength = wavelength;
        Rdot = rdot;
        Rsot = rsot;
        Rddt = rddt;
        Rsdt = rsdt;
        FCover = fCover;
        Abs_dir = abs_dir;
        Abs_hem = abs_hem;
        Rsdstar = rsdstar;
        Rddstar = rddstar;
        WavelengthToIndex = wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                      .ToDictionary(x => x.Wavelength, x => x.Index) ?? new Dictionary<double, int>();
    }

    /// <summary>
    /// Gets wavelengths as double array (implements ISpectralData)
    /// </summary>
    /// <returns>Array of wavelengths in nm</returns>
    public double[] GetWavelengths() => Wavelength ?? Array.Empty<double>();

    /// <summary>
    /// Gets wavelength-to-index mapping (implements ISpectralData)
    /// </summary>
    /// <returns>Dictionary mapping wavelengths to indices</returns>
    public Dictionary<double, int> GetWavelengthToIndex() => WavelengthToIndex;

    /// <summary>
    /// Creates a subset of CanopyOptics for specified wavelengths
    /// </summary>
    /// <param name="targetWavelengths">Array of wavelengths to extract (nm)</param>
    /// <returns>New CanopyOptics with subset data</returns>
    public CanopyOptics SubsetByWavelengths(double[] targetWavelengths)
    {
        return SpectralDataUtils.SubsetByWavelengths(this, targetWavelengths, (source, indices) =>
        {
            return new CanopyOptics(
                indices.Select(i => source.Wavelength[i]).ToArray(),
                indices.Select(i => source.Rdot[i]).ToArray(),
                indices.Select(i => source.Rsot[i]).ToArray(),
                indices.Select(i => source.Rddt[i]).ToArray(),
                indices.Select(i => source.Rsdt[i]).ToArray(),
                indices.Select(i => source.FCover[i]).ToArray(),
                indices.Select(i => source.Abs_dir[i]).ToArray(),
                indices.Select(i => source.Abs_hem[i]).ToArray(),
                indices.Select(i => source.Rsdstar[i]).ToArray(),
                indices.Select(i => source.Rddstar[i]).ToArray()
            );
        });
    }
}


// Example usage:
/*
public class ExampleUsage
{
    public static void DemonstrateWavelengthSubsetting()
    {
        // Example with LeafOptics
        var wavelengths = new double[] { 400, 500, 600, 700, 800 };
        var reflectance = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5 };
        var transmittance = new double[] { 0.05, 0.1, 0.15, 0.2, 0.25 };
        
        var leafOptics = new LeafOptics(wavelengths, reflectance, transmittance);
        
        // Subset to specific wavelengths
        var targetWavelengths = new double[] { 500, 700 };
        var subset = leafOptics.SubsetByWavelengths(targetWavelengths);
        
        // subset will contain only data for 500nm and 700nm wavelengths
        Console.WriteLine($"Original wavelengths: {wavelengths.Length}");
        Console.WriteLine($"Subset wavelengths: {subset.Wavelength.Length}");
    }
}
*/