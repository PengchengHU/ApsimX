#--------------------------------------------------------------------------------------------------#
# Author: Pengcheng Hu (hupc23@gmail.com)
# Date: Tue Apr 29 15:39:25 2025
# Objective: R wrapper script to call functions from Lib_PROSAIL.R for C# unit testing.
# Usage from command line (e.g., called by C# Process):
#   Rscript SailUtilitiesWrapper.R <FunctionName> <InputJsonPath> <OutputJsonPath>
#
# Example:
#   Rscript SailUtilitiesWrapper.R Compute_BRF "C:/temp/input.json" "C:/temp/output.json"
#
# Input JSON structure:
#   A JSON object where keys are the parameter names expected by the R function.
#   Nested objects/arrays will be parsed into R lists/vectors.
#
# Output JSON structure:
#   A JSON object containing the named results returned by the R function.
#--------------------------------------------------------------------------------------------------#

# --- Initialization ---

library(jsonlite)

# Get command line arguments
args <- commandArgs(trailingOnly = TRUE)

# Check if the correct number of arguments is provided
if (length(args) != 3) {
  stop("Usage: Rscript SailUtilitiesWrapper.R <FunctionName> <InputJsonPath> <OutputJsonPath>", call. = FALSE)
}

# Assign arguments to variables
functionName <- args[1]
inputJsonPath <- args[2]
outputJsonPath <- args[3]

# Define the path to the core SAIL functions script
# Assumes Lib_PROSAIL.R is in the same directory as this wrapper script.
# Adjust the path if necessary.
# libProsailPath <- file.path(dirname(sys.frame(1)$ofile), "Lib_PROSAIL.R") # Get dir of *this* script

scriptDir <- dirname(normalizePath(sub("^--file=", "", grep("^--file=", commandArgs(), value = TRUE)[1])))
libProsailPath <- file.path(scriptDir, "Lib_PROSAIL.R")

# Check if Lib_PROSAIL.R exists
if (!file.exists(libProsailPath)) {
  stop(paste("Error: Lib_PROSAIL.R not found at:", libProsailPath), call. = FALSE)
}

# Source the core SAIL functions script
# Sourcing within tryCatch to handle potential errors during sourcing
sourceSuccessful <- FALSE
tryCatch(
  {
    source(libProsailPath)
    sourceSuccessful <- TRUE
    cat("Successfully sourced:", libProsailPath, "\n") # Log success
  },
  error = function(e) {
    stop(paste("Error sourcing Lib_PROSAIL.R:", e$message), call. = FALSE)
  }
)
if (!sourceSuccessful) {
  stop("Failed to source Lib_PROSAIL.R. Check script integrity and path.", call. = FALSE)
}


# --- Input Processing ---

# Check if the input JSON file exists
if (!file.exists(inputJsonPath)) {
  stop(paste("Error: Input JSON file not found at:", inputJsonPath), call. = FALSE)
}

# Read and parse parameters from the input JSON file
# simplifyVector = FALSE to preserve list structure for single elements if needed
# simplifyDataFrame = FALSE might be needed if data frames are passed specifically
params <- tryCatch(
  {
    fromJSON(inputJsonPath, simplifyVector = TRUE, simplifyDataFrame = FALSE)
  },
  error = function(e) {
    stop(paste("Error parsing input JSON file:", inputJsonPath, "-", e$message), call. = FALSE)
  }
)

cat("Input parameters read for function:", functionName, "\n")
# print(params)

# --- Special Parameter Handling (if necessary) ---
# Some R functions might expect specific data types (e.g., data.frame, matrices)
# Convert parameters if the default JSON parsing isn't sufficient.

# Example: Convert nested lists for spectral data into the expected list structure
# R list elements often need names (e.g., SpecATM_Sensor$Direct_Light)
if (functionName %in% c("Compute_BRF", "Compute_fAPAR", "Compute_albedo") && !is.null(params$SpecATM_Sensor)) {
  # Ensure the structure matches R's expectation (likely list(Direct_Light=..., Diffuse_Light=...))
  # fromJSON usually handles this if C# sends correct structure. Check names:
  expected_names <- c("Direct_Light", "Diffuse_Light", "Wavelength") # Wavelength needed for fAPAR/Albedo range checks
  if (!all(expected_names %in% names(params$SpecATM_Sensor))) {
    warning("SpecATM_Sensor structure in JSON might not match R expectation (Direct_Light, Diffuse_Light, Wavelength).")
  }
  # Ensure vectors are numeric
  params$SpecATM_Sensor$Direct_Light <- as.numeric(params$SpecATM_Sensor$Direct_Light)
  params$SpecATM_Sensor$Diffuse_Light <- as.numeric(params$SpecATM_Sensor$Diffuse_Light)
  if (!is.null(params$SpecATM_Sensor$Wavelength)) {
    params$SpecATM_Sensor$Wavelength <- as.numeric(params$SpecATM_Sensor$Wavelength)
    # Rename Wavelength to lambda for R internal consistency if needed by functions
    names(params$SpecATM_Sensor)[names(params$SpecATM_Sensor) == "Wavelength"] <- "lambda"
  }
}

if (functionName %in% c("check_SpectralSampling", "adjust_PROSPECT_2_SAIL") && !is.null(params$specSOIL)) {
  # Rename Wavelength to lambda if needed by R function
  if (!is.null(params$specSOIL$Wavelength)) {
    params$specSOIL$Wavelength <- as.numeric(params$specSOIL$Wavelength)
    names(params$specSOIL)[names(params$specSOIL) == "Wavelength"] <- "lambda"
  }
  if (!is.null(params$specSOIL$Reflectance)) {
    params$specSOIL$Reflectance <- as.numeric(params$specSOIL$Reflectance)
  }
}

# Special handling for adjust_PROSPECT_2_SAIL
if (functionName == "adjust_PROSPECT_2_SAIL") {
  
  # Correct parameter names
  params$SAILversion <- params$SAILversion
  #params$SAILversion <- NULL
  
  # Ensure proper NULL handling
  if (is.null(params$brownLOP)) {
    params$BrownLOP <- NULL
  } else {
    params$BrownLOP <- params$brownLOP
    params$brownLOP <- NULL
  }
  
  # ProspectConstants / Spec_Sensor: C# sends SpectralConstants, R expects list with named vectors
  # Rename 'Wavelength' from C# SpectralConstants to 'lambda' for R
  if (!is.null(params$prospectConstants$Wavelength)) {
    params$prospectConstants$lambda <- as.numeric(params$prospectConstants$Wavelength)
    params$prospectConstants$Wavelength <- NULL # Remove original
  }
  # Rename 'RefractiveIndex' from C# SpectralConstants to 'nrefrac' for R
  if (!is.null(params$prospectConstants$RefractiveIndex)) {
    params$prospectConstants$nrefrac <- as.numeric(params$prospectConstants$RefractiveIndex)
    params$prospectConstants$RefractiveIndex <- NULL # Remove original
  }
  # Rename 'Tav90' from C# SpectralConstants to 'calctav_90' for R
  if (!is.null(params$prospectConstants$Tav90)) {
    params$prospectConstants$calctav_90 <- as.numeric(params$prospectConstants$Tav90)
    params$prospectConstants$Tav90 <- NULL # Remove original
  }
  # Rename 'Tav40' from C# SpectralConstants to 'calctav_40' for R
  if (!is.null(params$prospectConstants$Tav40)) {
    params$prospectConstants$calctav_40 <- as.numeric(params$prospectConstants$Tav40)
    params$prospectConstants$Tav40 <- NULL # Remove original
  }
  # Rename 'SAC_CAB' from C# SpectralConstants to 'SAC_CHL' for R
  if (!is.null(params$prospectConstants$SAC_CAB)) {
    params$prospectConstants$SAC_CHL <- as.numeric(params$prospectConstants$SAC_CAB)
    params$prospectConstants$SAC_CAB <- NULL # Remove original
  }

  # Ensure other fields are numeric vectors (jsonlite usually handles this)
  # Rename parameter itself
  params$Spec_Sensor <- params$prospectConstants
  params$prospectConstants <- NULL

  # Input_PROSPECT: C# sends List<ProspectInput>, R expects list or dataframe
  # fromJSON should parse List<Dictionary<string,object>> into R list of lists.
  # Need to ensure names match R expectations (e.g., CHL vs CAB)
  if (!is.null(params$inputProspectList)) {
    params$Input_PROSPECT <- lapply(params$inputProspectList, function(item) {
      # Rename CAB back to CHL if Lib_PROSAIL.R expects CHL
      if (!is.null(item$CAB)) {
        item$CHL <- item$CAB
        item$CAB <- NULL
      }
      # Ensure N, CAR, EWT etc are numeric
      item$N <- as.numeric(item$N)
      item$CHL <- as.numeric(item$CHL)
      item$CAR <- as.numeric(item$CAR)
      item$ANT <- as.numeric(item$ANT)
      item$BROWN <- as.numeric(item$BROWN)
      item$EWT <- as.numeric(item$EWT)
      item$LMA <- as.numeric(item$LMA)
      item$PROT <- as.numeric(item$PROT)
      item$CBC <- as.numeric(item$CBC)
      item$alpha <- as.numeric(item$Alpha)
      item$Alpha <- NULL # Rename alpha
      return(as.data.frame(item))
    })
    
    # Bind the list of dataframes into a single dataframe as adjust_PROSPECT_2_SAIL uses index [1,], [2,], suggesting dataframe needed.
    if (length(params$Input_PROSPECT) > 0) {
      params$Input_PROSPECT <- do.call(rbind, params$Input_PROSPECT)
    } else {
      params$Input_PROSPECT <- NULL # Handle empty case
    }
    params$inputProspectList <- NULL # Remove original C# param name
  } else {
    params$Input_PROSPECT <- NULL # Ensure it's NULL if not provided
  }

  # BrownLOP: C# sends LeafOptics object or null
  if (!is.null(params$BrownLOP)) {
    # Rename Wavelength to lambda if needed
    if (!is.null(params$BrownLOP$Wavelength)) {
      params$BrownLOP$lambda <- as.numeric(params$BrownLOP$Wavelength)
      params$BrownLOP$Wavelength <- NULL
    }
    # Ensure Reflectance/Transmittance are numeric
    params$BrownLOP$Reflectance <- as.numeric(params$BrownLOP$Reflectance)
    params$BrownLOP$Transmittance <- as.numeric(params$BrownLOP$Transmittance)
    # Convert to dataframe? adjust_PROSPECT_2_SAIL seems to expect dataframe via check_BrownLOP
    params$BrownLOP <- as.data.frame(params$BrownLOP)
    params$BrownLOP <- NULL # Remove original
  } else {
    params$BrownLOP <- NULL # Ensure NULL is passed if C# sends null
  }

  # Rename fraction_brown from C#
  params$fraction_brown <- params$fractionBrown
  params$fractionBrown <- NULL
}


# --- Function Execution ---

# Check if the target function exists in the sourced environment
if (!exists(functionName, mode = "function")) {
  stop(paste("Error: Function '", functionName, "' not found in Lib_PROSAIL.R."), call. = FALSE)
}

# Call the specified function with the parsed parameters using do.call
# Wrap in tryCatch to capture errors during function execution
cat("Calling R function:", functionName, "\n")
result <- tryCatch(
  {
    do.call(functionName, params)
  },
  error = function(e) {
    stop(paste("Error executing R function '", functionName, "': ", e$message), call. = FALSE)
  }
)

cat("R function execution completed.\n")

# --- Result Formatting ---

# Format the result into a named list suitable for JSON output
outputList <- list()

# Handle different possible return types from SAIL functions
if (is.data.frame(result)) {
  # If result is a dataframe, convert columns to list elements
  outputList <- as.list(result)
  # Ensure names are preserved (e.g., for Compute_BRF returning data.frame(BRF=...))
} else if (is.list(result)) {
  # If result is already a list (e.g., campbell, volscatt, adjust_PROSPECT_2_SAIL)
  # Check if it's the special structure from adjust_PROSPECT_2_SAIL
  if (functionName == "adjust_PROSPECT_2_SAIL") {
    outputList$GreenLOP_Reflectance <- result$GreenLOP$Reflectance
    outputList$GreenLOP_Transmittance <- result$GreenLOP$Transmittance
    # Handle potentially NULL BrownLOP
    if (!is.null(result$BrownLOP)) {
      outputList$BrownLOP_Reflectance <- result$BrownLOP$Reflectance
      outputList$BrownLOP_Transmittance <- result$BrownLOP$Transmittance
    } else {
      outputList$BrownLOP_Reflectance <- NULL
      outputList$BrownLOP_Transmittance <- NULL
    }
  } else {
    # Assume other list results are directly usable (e.g., campbell, volscatt, scattering results)
    outputList <- result
  }
} else if (is.vector(result) && length(result) == 1) {
  # If result is a single scalar value (e.g., Compute_fAPAR, Compute_albedo, Dcum, Jfuncs)
  # Assign it to a named element in the list (use function name as default key)
  outputList[[functionName]] <- result
} else if (is.vector(result)) {
  # If result is a simple vector (shouldn't happen for tested functions unless BRF was vector?)
  outputList[[functionName]] <- result
} else {
  # Handle unexpected result types
  warning(paste("Unhandled result type for function '", functionName, "': ", class(result)))
  outputList$result <- result # Store raw result
}

# --- Output Generation ---

# Convert the output list to JSON format
# auto_unbox = TRUE converts single-element vectors to scalars in JSON
# pretty = TRUE makes the output file more readable (optional)
outputJson <- toJSON(outputList, auto_unbox = TRUE, pretty = TRUE, digits = 10) # Increase digits for precision

# Write the JSON result to the specified output file
# Wrap in tryCatch for file writing errors
writeSuccessful <- FALSE
tryCatch(
  {
    write(outputJson, file = outputJsonPath)
    writeSuccessful <- TRUE
    cat("Results successfully written to:", outputJsonPath, "\n")
  },
  error = function(e) {
    stop(paste("Error writing output JSON file:", outputJsonPath, "-", e$message), call. = FALSE)
  }
)

if (!writeSuccessful) {
  stop("Failed to write output JSON file.", call. = FALSE)
}

# --- Script End ---
cat("R wrapper script finished successfully.\n")
