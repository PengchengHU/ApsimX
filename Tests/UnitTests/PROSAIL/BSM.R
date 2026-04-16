
#' @param B: soil brightness
#' @param lat: spectral shape latitude (range = 20 - 40 deg)
#' @param lon: spectral shape longitude (range = 45 - 65 deg)
#' @param SMp: soil moisture volume percentage (5 - 55)
# #' @param SMC: soil moisture capacity (recommended 0.25)
#' @references http://dx.doi.org/10.1016/j.rse.2020.111870;
#' https://github.com/Christiaanvandertol/SCOPE/blob/master/src/RTMs/BSM.m
BSM <- function(B, lat, lon, SMp, quiet = FALSE) {
  
  # testing
  # Soil parameters
  # B <- 0.5
  # lat <- 25
  # lon <- 45
  SMC <- 25 # SMC: soil moisture capacity, which is an indicator of a soil's ability to retain water 
  #                and is 25 (percentage) in the BSM model.
  # SMp <- 25
  
  if (quiet == FALSE) {
    warning('\n ***** SMp must be percentage (e.g. 25) in the BSM model !!! ***** \n')
  }
  
  # Spectral parameters
  if (!requireNamespace("jsonlite", quietly = TRUE)) library(jsonlite)
  bsm_json_path <- normalizePath(file.path(scriptDir, '..', '..', '..',
      'Models', 'PROSAIL', 'InputProperties', 'SpectralData', 'BSM_GSV.json'))
  bsm_data <- jsonlite::fromJSON(bsm_json_path)
  GSV_1 <- bsm_data$GSV_1 # GSV: Global Soil Vectors spectra of dry soil
  GSV_2 <- bsm_data$GSV_2
  GSV_3 <- bsm_data$GSV_3
  nw <- bsm_data$nw # nw: water refraction index spectrum
  kw <- bsm_data$kw # kw: water absorption coefficient
  
  # Empirical parameters
  deleff <- 0.015 # deleff: effective optical thickness of single water film (recommended 0.015)
  
  # Dry soil reflectance model ####
  f1 <- B * sin(lat * pi / 180)
  f2 <- B * cos(lat * pi / 180) * sin(lon * pi / 180)
  f3 <- B * cos(lat * pi / 180) * cos(lon * pi / 180)
  
  rdry <- f1 * GSV_1 + f2 * GSV_2 + f3 * GSV_3
  # plot(rdry)
  
  # Wet soil ####
  # In this model it is assumed that the water film area is built up  
  # according to a Poisson process. The fractional areas are as follows:
  
  # P(0): dry soil area
  # P(1): single water film area
  # P(2): double water film area
  # ...
  # et cetera
  
  # The fractional areas are given by P(k) = mu^k * exp(-mu) / k! 
  
  # For water films of multiple thickness only the transmission loss due
  # to water absorption is modified, since surface reflectance effects 
  # are not influenced by the thickness of the film
  
  k <- 0:6 # number of water film, '0' refers to dry soil
  nk <- length(k) # the number of occurrences
  mu <- (SMp - 5) / SMC # mu-parameter of Poisson distribution
  if (mu <=  0) {# the reason for adding this: if mu<0, fry>1.
    rwet <- rdry # we need to check SMC in other parts of SCOPE. soil fluxes routine.
  } else {
    
    # Lekner & Dorf (1988) modified soil background reflectance for soil refraction index = 2.0; 
    # uses the tav-function of PROSPECT
    rbac <- 1 - (1 - rdry) * (rdry * TAV(90, 2.0 / nw) / TAV(90, 2.0) + 1 - rdry) # Rbac: background reflectance
    
    # total reflectance at bottom of water film surface
    p <- 1 - TAV(90, nw) / nw^2   # rho21, water to air, diffuse
    
    # reflectance of water film top surface, use 40 degrees incidence angle, like in PROSPECT
    Rw <- 1 - TAV(40, nw) # rho12, air to water, direct
    
    # fraction of areas
    # P(0) <- dry soil area            fmul(1)
    # P(1) <- single water film area   fmul(2)
    # P(2) <- double water film area   fmul(3)
    fmul <- Conj(t(dpois(k, mu))) # Probability 
    tw <- lapply(k, function(x) {exp(-2 * kw * deleff * x)}) # two-way transmittance, exp(-2*kw*k Delta)
    tw <- do.call(cbind, tw) 
    
    Rwet_k <- apply(tw, MARGIN = 2, function(x) {
      Rw + (1 - Rw) * (1 - p) * x * rbac / (1 - p * x * rbac)})
    # Rwet_k <- do.call(cbind, Rwet_k)
    rwet <- rdry * fmul[1] + as.matrix(Rwet_k[, 2:nk]) %*% fmul[2:nk]
    # plot(seq_along(rwet), rwet, col = 'red')
  }
  res <- data.frame(wavelength = bsm_data$wavelength, reflectance = rwet)
  complementary <- tail(res, 100)
  complementary$wavelength <- seq(2401, 2500)
  res <- rbind(res, complementary)
  
  return(res)
}

#' @param alfa angle
#' @param nr reflectance
#' @details modified soil background reflectance for soil refraction index = 2.0
#'  uses the tav-function of PROSPECT
#'  @note Lekner & Dorf (1988) 
TAV <- function(alfa, nr) {
  
  # alfa <- 90
  # nr <- nw
  
  n2 <- nr^2
  np <- n2 + 1
  nm <- n2 - 1
  
  a <- +((nr + 1)^2) / 2
  k <- -((n2 - 1)^2) / 4
  sin_a <- sin(alfa * pi / 180)
  
  if (alfa != 0 ) {
    B2 <- sin_a^2 - np / 2
    B2_k <- B2^2 + k
    B2_k[B2_k < 0] <- 0
    B1 <- (alfa != 90) * sqrt(B2_k)
    
    b  <- B1 - B2
    b3 <- b^3
    a3 <- a^3
    
    ts <- (k^2 / (6 * b3) + k / b - b / 2) - (k^2 / (6 * a3) + k / a - a / 2)
    
    tp1 <- -2 * n2 * (b  -  a   ) / (np^2)
    tp2 <- -2 * n2 * np * (log(b / a)  ) / (nm^2)
    tp3 <- n2 * (1 / b - 1 / a ) / 2 
    
    tp4 <- 16 * n2^2 * (n2^2 + 1) * (log((2 * np * b - nm^2) / (2 * np * a - nm^2))) / (np^3 * nm^2)
    tp5 <- 16 * n2^2 * (n2) * (1 / (2 * np * b - nm^2) - 1 / (2 * np * a - nm^2)) / (np^3)							 
    tp <- tp1 + tp2 + tp3 + tp4 + tp5
    Tav <- (ts + tp) / (2 * sin_a^2)
  } else {
    
    Tav <- 4 * nr / ((nr + 1) * (nr + 1))
  }
  return(Tav)
}
