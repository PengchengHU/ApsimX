#--------------------------------------------------------------------------------------------------#
# Author: Pengcheng Hu (hupc23@gmail.com)
# Objective: R wrapper script to call BSM() for C# unit testing of BsmCore.
# Usage from command line (called by C# Process):
#   Rscript BSMWrapper.R <InputJsonPath> <OutputJsonPath>
#
# Input JSON keys: B, lat, lon, SMp
# Output JSON keys: wavelength, reflectance
#--------------------------------------------------------------------------------------------------#

library(jsonlite)

args <- commandArgs(trailingOnly = TRUE)
if (length(args) != 2) {
  stop("Usage: Rscript BSMWrapper.R <InputJsonPath> <OutputJsonPath>", call. = FALSE)
}
inputJsonPath  <- args[1]
outputJsonPath <- args[2]

# Locate BSM.R in the same directory as this script
scriptDir <- dirname(normalizePath(sub("^--file=", "", grep("^--file=", commandArgs(), value = TRUE)[1])))
bsmScriptPath <- file.path(scriptDir, "BSM.R")
if (!file.exists(bsmScriptPath)) {
  stop(paste("BSM.R not found at:", bsmScriptPath), call. = FALSE)
}
source(bsmScriptPath)
cat("Sourced:", bsmScriptPath, "\n")

# Read input parameters
params <- fromJSON(inputJsonPath, simplifyVector = TRUE)

# Call BSM with quiet = TRUE to suppress the SMp-unit warning
result <- BSM(
  B   = params$B,
  lat = params$lat,
  lon = params$lon,
  SMp = params$SMp,
  quiet = TRUE
)

# result is a data.frame with columns: wavelength, reflectance
outputList <- list(
  wavelength  = result$wavelength,
  reflectance = result$reflectance
)

outputJson <- toJSON(outputList, auto_unbox = FALSE, pretty = TRUE, digits = 10)
write(outputJson, file = outputJsonPath)
cat("Results written to:", outputJsonPath, "\n")
