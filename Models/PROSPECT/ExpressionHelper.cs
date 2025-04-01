using System;
using System.Collections.Generic;
using APSIM.Shared.Utilities;
using Models.Core;

namespace Models.Prospect
{
    /// <summary>
    /// 表达式解析助手
    /// </summary>
    public static class ExpressionHelper
    {
        /// <summary>
        /// 计算表达式值
        /// </summary>
        public static double Evaluate(string expression, IModel model, Dictionary<string, double> additionalVars = null)
        {
            try
            {
                // Create a dictionary that can handle both double and object values
                var variables = new Dictionary<string, object>();

                if (additionalVars != null)
                {
                    foreach (var item in additionalVars)
                    {
                        variables[item.Key] = item.Value;
                    }
                }

                // Use extension method to call Evaluate
                object result = expression.Evaluate(model, variables);

                // Convert result to double
                return Convert.ToDouble(result);
            }
            catch (Exception ex)
            {
                throw new Exception($"表达式计算错误: {expression}\n{ex.Message}");
            }
        }

        /// <summary>
        /// 验证表达式是否有效
        /// </summary>
        public static bool Validate(string expression, Model model, out string error)
        {
            try
            {
                Evaluate(expression, model);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Extension methods for expression evaluation
    /// </summary>
    public static class ExpressionExtensions
    {
        /// <summary>
        /// Extension method to evaluate expressions
        /// </summary>
        public static object Evaluate(this string expression, object contextObject, Dictionary<string, object> localVariables = null)
        {
            // This method uses reflection to call the internal Evaluate method
            var evaluatorType = typeof(ExpressionEvaluator);
            var method = evaluatorType.GetMethod("Evaluate",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);

            if (method == null)
                throw new MissingMethodException("Could not find Evaluate method");

            return method.Invoke(null, new object[] { expression, contextObject, localVariables ?? new Dictionary<string, object>() });
        }
    }
}