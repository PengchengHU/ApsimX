
#!/usr/bin/env Rscript
args <- commandArgs(trailingOnly = TRUE)

library(jsonlite)

# Source the local, self-contained PROSPECT implementation instead of relying on
# the (deprecated/breaking-API) external 'prospect' package.
scriptDir <- dirname(normalizePath(sub("^--file=", "", grep("^--file=", commandArgs(), value = TRUE)[1])))
source(file.path(scriptDir, "Lib_PROSPECT.R"))

# Load the local leaf spectral constants (same file/format used by SailUtilitiesWrapper.R)
# and rename fields from the C#-facing JSON convention to what Lib_PROSPECT.R expects.
specPROSPECT <- jsonlite::fromJSON(file.path(scriptDir, "SpecPROSPECT_FullRange.json"))
specPROSPECT$lambda <- as.numeric(specPROSPECT$Wavelength); specPROSPECT$Wavelength <- NULL
specPROSPECT$nrefrac <- as.numeric(specPROSPECT$RefractiveIndex); specPROSPECT$RefractiveIndex <- NULL
specPROSPECT$calctav_40 <- as.numeric(specPROSPECT$Tav40); specPROSPECT$Tav40 <- NULL
specPROSPECT$calctav_90 <- as.numeric(specPROSPECT$Tav90); specPROSPECT$Tav90 <- NULL
specPROSPECT$SAC_CHL <- as.numeric(specPROSPECT$SAC_CAB); specPROSPECT$SAC_CAB <- NULL

input_file <- args[1]
output_file <- args[2]

`%||%` <- function(x, default) if (is.null(x)) default else x

if (!exists("run_prospect_r")) {
  run_prospect_r <- function(params) {
    res <- PROSPECT(SpecPROSPECT = specPROSPECT,
                    N = params$N, CHL = params$CAB, CAR = params$CAR,
                    ANT = params$ANT %||% 0.0, BROWN = params$BROWN %||% 0.0,
                    EWT = params$EWT, LMA = params$LMA,
                    PROT = params$PROT %||% 0.0, CBC = params$CBC %||% 0.0,
                    alpha = params$Alpha)

    list(
      Reflectance = res$Reflectance,
      Transmittance = res$Transmittance
    )
  }
}

input_data <- jsonlite::fromJSON(input_file)

results <- run_prospect_r(input_data)

jsonlite::write_json(results, output_file)