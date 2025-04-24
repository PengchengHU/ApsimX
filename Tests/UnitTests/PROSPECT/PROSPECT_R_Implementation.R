
#!/usr/bin/env Rscript
args <- commandArgs(trailingOnly = TRUE)

# source('D:/ApsimX/Tests/UnitTests/Lib_PROSPECT.R')
library(jsonlite)

input_file <- args[1]
output_file <- args[2]

if (!exists("run_prospect_r")) {
  run_prospect_r <- function(params) {
    wavelengths <- 400:2500
    reflectance <- rep(0.1, length(wavelengths))
    transmittance <- rep(0.1, length(wavelengths))
    
    res <- prospect::PROSPECT(N = params$N, CHL = params$CAB, CAR = params$CAR, 
                              EWT = params$EWT, LMA = params$LMA, alpha = params$Alpha)
    
    list(
      Reflectance = res$Reflectance,
      Transmittance = res$Transmittance
    )
  }
}

input_data <- jsonlite::fromJSON(input_file)

results <- run_prospect_r(input_data)

jsonlite::write_json(results, output_file)