# APSIM-PROSAIL framework

The **APSIM-PROSAIL** framework couples the **PROSAIL** (radiative transfer model) with the **APSIM** (agricultural systems modelling and simulation platform). **PROSAIL** combines the **PROSPECT** leaf optical properties model with the **SAIL** canopy reflectance model to simulate hyperspectral canopy reflectance from leaf biochemical and canopy structural traits.

At each simulation timestep, plant traits computed by **APSIM** (e.g. LAI, leaf nitrogen) are passed as inputs to **PROSAIL** via configurable expressions. The resulting per-wavelength canopy reflectance and associated optical variables are written to a SQLite database for further analysis or comparison with remote sensing observations.

This C# implementation of **PROSAIL** is based on the algorithms implemented in the R packages **prosail** (https://github.com/jbferet/prosail) and **prospect** (https://github.com/jbferet/prospect), and is used with the permission of the package author.The C# implementation of BSM is based on the algorithm implemented in Matlab (https://github.com/Christiaanvandertol/SCOPE/blob/master/src/RTMs/BSM.m).

## How to use

### A note on input modes

Every PROSAIL/PROSPECT/SAIL parameter accepts either a **literal value** or an **APSIM expression** (e.g. `[Wheat].Leaf.LAI`) re-evaluated fresh every simulated day — this covers all leaf (Step 1), canopy (Step 2), and BSM soil (Step 4, BsmBrightness/BsmLat/BsmLon) parameters, which are expected to continuously track a live crop or soil model.

Five parameters — **Psoil**, **SMp**, **SunZenithAngle**, **ObserverZenithAngle**, and **RelativeAzimuthAngle** — additionally accept a **comma-separated per-observation-date list** (one value per date in **ObservationDates**). This third mode exists because these particular inputs are usually tied to specific, independently-known satellite overpass events (the sun-sensor geometry and soil-moisture reading recorded at acquisition time) rather than a continuously-simulated state, so they are commonly supplied as a fixed table of per-date observations instead of a daily-evaluated expression.

### Step 1: PROSPECT leaf inputs

Set each leaf biochemical parameter either to a fixed value or to an APSIM expression that pulls the value from a crop model at runtime (e.g. `[Wheat].Leaf.LAI`). Key parameters and their typical ranges:

| Parameter | Description | Typical range |
|-----------|-------------|---------------|
| **N** | Leaf mesophyll structure (number of layers) | 1.0 – 2.6 |
| **CAB** | Chlorophyll a+b content (µg cm⁻²) | 10 – 80 |
| **CAR** | Carotenoid content (µg cm⁻²) | 1 – 24 |
| **ANT** | Anthocyanin content (µg cm⁻²) | 0 – 10 |
| **BROWN** | Brown (senescent) pigment fraction | 0 – 1 |
| **EWT** | Equivalent water thickness (cm) | 0.001 – 0.08 |
| **LMA** | Leaf dry matter per area (g cm⁻²) | 0.001 – 0.02 |
| **PROT** | Protein content (g cm⁻²) | 0 – 10 |
| **CBC** | Carbon-based constituents — cellulose + lignin (g cm⁻²) | 0 – 10 |
| **Alpha** | Incidence angle for refractive index (°) | 40 (default) |

Set **InputWavelengthRange** to the wavelengths PROSAIL should compute, e.g. `400-2500` for the full range or `650,750,800` for specific bands. Leave empty to use all wavelengths (400–2500 nm).

### Step 2: SAIL canopy properties

Choose **SailVersion**:

- `4SAIL`: single-layer homogeneous canopy. Suitable for most crops.
- `4SAIL2`: two-layer canopy (green + brown) with clumping effects. Required when **FractionBrown** > 0.

| Parameter | Description | Typical range |
|-----------|-------------|---------------|
| **LAI** | Leaf Area Index (m² m⁻²) | 0 – 10 |
| **HotSpot** | Hotspot parameter (leaf size / canopy height) | 0 – 1 |
| **TypeLidf** | Leaf angle distribution type: 1 = Verhoef (uses LIDFa + LIDFb), 2 = Campbell (uses LIDFa only) | 1 or 2 |
| **LIDFa** | LIDF parameter a: average leaf slope (TypeLidf=1, Verhoef) or mean leaf angle in degrees (TypeLidf=2, Campbell) | −1 to 1 (type 1) or −90 to 90° (type 2) |
| **LIDFb** | LIDF parameter b — bimodality (type 1 only) | −1 to 1 |
| **FractionBrown** | Fraction of brown leaves in canopy (4SAIL2 only) | 0 – 1 |
| **Dissociation** | Degree of clumping between green and brown leaves (4SAIL2 only) | 0 – 1 |
| **CrownCover** | Crown cover fraction (4SAIL2 only) | 0 – 1 |
| **TreeShape** | Tree shape factor affecting gap fraction (4SAIL2 only) | > 0 |

### Step 3: Sun–observer geometry

| Parameter | Description | Typical range |
|-----------|-------------|---------------|
| **ObservationDates** | Comma-separated dates (e.g. `2023-01-15,2023-04-01`). Leave empty to run every simulation day. | |
| **SunZenithAngle** | Solar zenith angle (°) | 0 – 90 |
| **ObserverZenithAngle** | Sensor/observer zenith angle (°) | 0 – 90 |
| **RelativeAzimuthAngle** | Relative azimuth between sun and observer (°) | 0 – 360 |

When **ObservationDates** lists multiple dates, supply a matching comma-separated list of angles for each geometry parameter, or a single value applied to all dates.

### Step 4: Soil reflectance

Toggle **UseBSM** to choose the soil model:

**Wet/dry linear mixing model** (UseBSM = false):

- Leave **WetDrySoilReflectancePath** empty to use the built-in soil spectra, or provide a path to a custom CSV file with wet and dry soil reflectance spectra.
- Set **Psoil** to a value between 0 (fully wet) and 1 (fully dry), or link it to a soil moisture expression. Accepts a single value, comma-separated per-date list, or an APSIM expression.

**BSM (brightness–soil model)** (UseBSM = true):

| Parameter | Description | Typical range |
|-----------|-------------|---------------|
| **BsmBrightness** | Soil brightness factor | 0 – 1 |
| **BsmLat** | Latitude input to BSM (°) | 20 – 40 |
| **BsmLon** | Longitude input to BSM (°) | 45 – 65 |
| **SMp** | Volumetric soil moisture (%) | 5 – 55 |

### Step 5: Output and simulation control

The .db  file will receive results (relative to the simulation `.apsimx` file, e.g. `YourSimulationName_prosail.db`).

Enable the tables you need:

| Toggle | Table | Contents |
|--------|-------|----------|
| **OutputParameters** | Parameters | Every resolved PROSAIL input value for the day (leaf, canopy, soil, geometry, and sensor selection) — the actual numbers each literal or expression evaluated to. Useful for confirming inputs are behaving as expected. |
| **OutputCanopyOpticalVariable** | CanopyOpticalVariable | Per-wavelength canopy optical variables straight from SAIL (see table below). |
| **OutputCanopyStateVariable** | CanopyStateVariable | Broadband (spectrally-integrated) canopy state variables (see table below). |
| **OutputCanopyBRF** | CanopyBRF | Per-wavelength bidirectional reflectance factor (BRF) — the reflectance an optical sensor would actually observe, combining direct and diffuse illumination (see "Direct vs. diffuse weighting" below). This is normally what you want for computing vegetation indices (e.g. NDVI), not the raw Rdot/Rsot columns. |
| **OutputReflectanceResampledToSensor** | ReflectanceResampledToSensor | BRF resampled into the discrete spectral bands of the chosen sensor (e.g. Landsat's Blue/Green/Red/NIR bands), for direct, like-for-like comparison with real satellite imagery. Requires a **SensorType** below. |

**CanopyOpticalVariable columns** (one value per simulated wavelength):

| Variable | Meaning |
|----------|---------|
| **Rdot** | Hemispherical-directional reflectance factor (diffuse illumination → directional sensor) |
| **Rsot** | Bi-directional reflectance factor (direct illumination → directional sensor) |
| **Rddt** | Bi-hemispherical reflectance (diffuse illumination → hemisphere) |
| **Rsdt** | Directional-hemispherical reflectance (direct illumination → hemisphere) |
| **FCover** | Fractional canopy cover |
| **Abs_dir** | Canopy absorptance under direct (beam) radiation |
| **Abs_hem** | Canopy absorptance under diffuse (hemispherical) radiation |
| **Rsdstar** | Canopy-layer reflectance for direct illumination (excludes soil contribution) |
| **Rddstar** | Canopy-layer reflectance for diffuse illumination (excludes soil contribution) |

**CanopyStateVariable columns** (broadband, integrated across the full simulated spectrum):

| Variable | Meaning |
|----------|---------|
| **fAPAR** | Fraction of Absorbed Photosynthetically Active Radiation (400–700 nm) |
| **fCover** | Fractional green canopy cover |
| **albedo** | Broadband canopy albedo (400–2400 nm) |

**Direct vs. diffuse weighting**: `Rdot`/`Rddt`/`Abs_hem`/`Rddstar` describe the canopy under purely diffuse (sky) illumination, while `Rsot`/`Rsdt`/`Abs_dir`/`Rsdstar` describe it under purely direct (beam) illumination — a real sensor always sees a mix of both. `CanopyBRF`, `fAPAR`, and `albedo` combine the direct and diffuse components using a solar-elevation-dependent diffuse-fraction estimate (`skyl`, from Francois et al. 2002, implemented in `ComputeBRF`/`ComputeFAPAR`/`ComputeAlbedo`), so they already represent properly weighted, sensor-comparable values. This weighting currently depends only on solar zenith angle, not on the specific simulated day's actual cloudiness.

When **OutputReflectanceResampledToSensor** is enabled, choose a **SensorType** from the built-in list (Landsat 7/8/9, MODIS, Pleiades 1A/1B, Sentinel-2/2A/2B/2C, SPOT 6/7, Venus) or select *Custom* and supply a **CustomSRFPath** pointing to your spectral response function file.

Set **LoggingLevel** to control verbosity (Debug, Info, Warning, or Error).

## References

If you use the APSIM-PROSAIL in your work, please cite the relevant papers below.

### PROSPECT

- Féret, J.-B. & de Boissieu, F. (2024). `prospect`: an R package to link leaf optical properties with their chemical and structural properties with the leaf model PROSPECT. Journal of Open Source Software, 9(94), 6027, https://doi.org/10.21105/joss.06027
- Féret, J.-B., Berger, K., de Boissieu, F. & Malenovský, Z. (2021). PROSPECT-PRO for estimating content of nitrogen-containing leaf proteins and other carbon-based constituents. Remote Sensing of Environment. 252, 112173. https://doi.org/10.1016/j.rse.2020.112173
- Féret, J.-B., Gitelson, A.A., Noble, S.D. & Jacquemoud, S. (2017). PROSPECT-D: Towards modeling leaf optical properties through a complete lifecycle. Remote Sensing of Environment. 193, 204–215. http://dx.doi.org/10.1016/j.rse.2017.03.004
### 4SAIL and 4SAIL2

- Verhoef W & Bach H, 2007. Coupled soil–leaf-canopy and atmosphere radiative transfer modeling to simulate hyperspectral multi-angular surface reflectance and TOA radiance data. Remote Sensing of Environment, 109:166-182. https://doi.org/10.1016/j.rse.2006.12.013
- Verhoef W, Jia L, Xiao Q & Su Z, 2007. Unified optical-thermal four-stream radiative transfer theory for homogeneous vegetation canopies. IEEE Transactions in Geosciences and Remote Sensing, 45:1808–1822. https://doi.org/10.1109/TGRS.2007.89584
### PROSAIL

- Jacquemoud S, Verhoef W, Baret F, Bacour C, Zarco-Tejada PJ, Asner GP, François C & Ustin SL, 2009. PROSPECT+ SAIL models: A review of use for vegetation characterization. Remote Sensing of Environment, 113:S56–S66. https://doi.org/doi:10.1016/j.rse.2008.01.026
- Berger K, Atzberger C, Danner M, D'Urso G, Mauser W, Vuolo F & Hank T 2018. Evaluation of the PROSAIL Model Capabilities for Future Hyperspectral Model Environments: A Review Study. Remote Sensing, 10:85. https://doi.org/10.3390/rs10010085
### The APSIM-PROSAIL integration 

- Holzworth, D.P., Huth, N.I., deVoil, P.G., Zurcher, E.J., Herrmann, N.I., McLean, G., Chenu, K., van Oosterom, E.J., Snow, V., Murphy, C., Moore, A.D., Brown, H., Whish, J.P.M., Verrall, S., Fainges, J., Bell, L.W., Peake, A.S., Poulton, P.L., Hochman, Z., Thorburn, P.J., Gaydon, D.S., Dalgliesh, N.P., Rodriguez, D., Cox, H., Chapman, S., Doherty, A., Teixeira, E., Sharp, J., Cichota, R., Vogeler, I., Li, F.Y., Wang, E., Hammer, G.L., Robertson, M.J., Dimes, J.P., Whitbread, A.M., Hunt, J., van Rees, H., McClelland, T., Carberry, P.S., Hargreaves, J.N.G., MacLeod, N., McDonald, C., Harsdorf, J., Wedgwood, S., Keating, B.A., 2014. APSIM – Evolution towards a new generation of agricultural systems simulation. Environmental Modelling & Software 62, 327–350. https://doi.org/10.1016/j.envsoft.2014.07.009
- Hu, P., Zheng, B., Chen, Q., Grunefeld, S., Choudhury, M.R., Fernandez, J., Potgieter, A., Chapman, S.C., 2024. Estimating aboveground biomass dynamics of wheat at small spatial scale by integrating crop growth and radiative transfer models with satellite remote sensing data. Remote Sensing of Environment 311, 114277. https://doi.org/10.1016/j.rse.2024.114277
- Chen, Q., Zheng, B., Chen, T., Chapman, S.C., 2022. Integrating a crop growth model and radiative transfer model to improve estimation of crop traits based on deep learning. Journal of Experimental Botany erac291. https://doi.org/10.1093/jxb/erac291
### BSM

- Verhoef, W., van der Tol, C., Middleton, E.M., 2018. Hyperspectral radiative transfer modeling to explore the combined retrieval of biophysical parameters and canopy fluorescence from FLEX – Sentinel-3 tandem mission multi-sensor data. Remote Sensing of Environment 204, 942–963. https://doi.org/10.1016/j.rse.2017.08.006
- Yang, P., van der Tol, C., Yin, T., Verhoef, W., 2020. The SPART model: A soil-plant-atmosphere radiative transfer model for satellite measurements in the solar spectrum. Remote Sensing of Environment 247, 111870. https://doi.org/10.1016/j.rse.2020.111870
