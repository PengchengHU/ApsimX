


#!/usr/bin/env Rscript
args <- commandArgs(trailingOnly = TRUE)

# source('D:/ApsimX/Tests/UnitTests/Lib_PROSPECT.R')
library(jsonlite)

# 输入输出文件路径
input_file <- args[1]
output_file <- args[2]

# 加载PROSPECT R实现 (这里需要替换为实际的R实现)
# 假设有一个名为run_prospect_r的函数可用
if (!exists("run_prospect_r")) {
  # 如果没有现成实现，可以使用以下简化版本
  run_prospect_r <- function(params) {
    # 这是示例实现 - 替换为实际的PROSPECT R代码
    wavelengths <- 400:2500
    reflectance <- rep(0.1, length(wavelengths))
    transmittance <- rep(0.1, length(wavelengths))
    
    res <- prospect::PROSPECT(N = params$N, CHL = params$CHL, CAR = params$CAR, 
                              EWT = params$EWT, LMA = params$LMA, alpha = params$Alpha)
    
    # # 简单模拟参数影响
    # reflectance <- reflectance + params$CHL / 1000
    # transmittance <- transmittance - params$EWT / 100
    
    list(
      Reflectance = res$Reflectance,
      Transmittance = res$Transmittance
    )
  }
}


# 读取输入参数
input_data <- jsonlite::fromJSON(input_file)

# 运行PROSPECT模型
results <- run_prospect_r(input_data)

# 保存结果
jsonlite::write_json(results, output_file)

