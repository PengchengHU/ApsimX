namespace Models.PROSAIL
{
    /// <summary>Enum for supported SAIL model versions.</summary>
    public enum SailVersionTypes
    {
        /// <summary>4SAIL - single layer canopy model</summary>
        FourSAIL,
        /// <summary>4SAIL2 - two layer canopy model (green + brown)</summary>
        FourSAIL2
    }

    /// <summary>Soil reflectance model selection.</summary>
    public enum SoilReflectanceModelTypes
    {
        /// <summary>Linear mixing of wet and dry soil reflectance spectra, weighted by Psoil.</summary>
        WetDryMixing,
        /// <summary>Brightness Soil Model (Verhoef et al. 2018).</summary>
        BSM
    }

    /// <summary>Enum for supported sensors.</summary>
    public enum SensorTypes
    {
        /// <summary>No sensor selected (must be set before running)</summary>
        None,
        /// <summary>Landsat_7</summary>
        Landsat_7,
        /// <summary>Landsat_8</summary>
        Landsat_8,
        /// <summary>Landsat_9</summary>
        Landsat_9,
        /// <summary>MODIS</summary>
        MODIS,
        /// <summary>Pleiades_1A</summary>
        Pleiades_1A,
        /// <summary>Pleiades_1B</summary>
        Pleiades_1B,
        /// <summary>Sentinel_2</summary>
        Sentinel_2,
        /// <summary>Sentinel_2A</summary>
        Sentinel_2A,
        /// <summary>Sentinel_2B</summary>
        Sentinel_2B,
        /// <summary>Sentinel_2C</summary>
        Sentinel_2C,
        /// <summary>SPOT_6_7</summary>
        SPOT_6_7,
        /// <summary>Venus</summary>
        Venus,
        /// <summary>Custom user-provided SRF file</summary>
        Custom
    }

    /// <summary>Defines the logging verbosity levels.</summary>
    public enum LogLevel
    {
        /// <summary>Log only errors</summary>
        Error,
        /// <summary>Log errors and warnings</summary>
        Warning,
        /// <summary>Log errors, warnings, and informational messages</summary>
        Info,
        /// <summary>Log all messages, including debug details</summary>
        Debug
    }
}
