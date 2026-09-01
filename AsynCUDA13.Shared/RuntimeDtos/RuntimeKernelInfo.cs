using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.RuntimeDtos
{
    public class RuntimeKernelInfo
    {
        public static readonly HashSet<string> BooleanTypes = new(["bool", "boolean", "bit", "Boolean"], StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> IntegerTypes = new(["byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint", "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "IntPtr", "UIntPtr"], StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> DecimalTypes = new(["float", "double", "decimal", "Single", "Double", "Decimal"], StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> StructTypes = new(["struct", "ValueType"], StringComparer.OrdinalIgnoreCase);

        public string FunctionName { get; set; } = string.Empty;    // Empty if not compiled yet
        public string SourcePath { get; set; } = string.Empty;  // Empty if not saved as .cu
        public string? PtxPath { get; set; } = null;    // Null if not compiled

        public string KernelCode { get; set; } = string.Empty;  // Empty if .cu file exists + is readable

        public string[] ArgumentNames { get; set; } = [];
        public string[] ArgumentTypes { get; set; } = [];

        public int? ArgumentsCount => this.ArgumentNames.Length == this.ArgumentTypes.Length ? this.ArgumentNames.Length : null;
        public int? PointerArgumentsCount => this.PointerArgumentTypes.Keys.Any(k => k >= this.ArgumentsCount) ? null : this.PointerArgumentTypes.Count;

        public Dictionary<int, string> PointerArgumentTypes
        {
            get
            {
                Dictionary<int, string> pointerArgTypes = [];
                for (int i = 0; i < this.ArgumentTypes.Length; i++)
                {
                    if (this.ArgumentTypes[i].EndsWith("*"))
                    {
                        pointerArgTypes[i] = this.ArgumentTypes[i];
                    }
                }
                return pointerArgTypes;
            }
        }


        public string? IsPointerArgument(int index, bool returnPointerType = true)
        {
            if (index < 0 || index >= this.ArgumentTypes.Length)
            {
                return null;
            }
            string type = this.ArgumentTypes[index];
            return type.EndsWith("*") ? returnPointerType ? type : type.Replace("*", "").Trim() :  null;
        }

        public string? IsPointerArgument(string? argumentName, bool returnPointerType = true)
        {
            return this.IsPointerArgument(this.ArgumentNames.IndexOf(argumentName), returnPointerType);
        }

        public bool? IsBooleanArgument(int index)
        {
            return index < 0 ? null : BooleanTypes.Contains(this.ArgumentTypes[index]);
        }

        public bool? IsBooleanArgument(string? argumentName)
        {
            return this.IsBooleanArgument(this.ArgumentNames.IndexOf(argumentName));
        }

        public bool? IsIntegerArgument(int index)
        {
            return (index < 0) ? null : IntegerTypes.Contains(this.ArgumentTypes[index]);
        }

        public bool? IsIntegerArgument(string? argumentName)
        {
            return this.IsIntegerArgument(this.ArgumentNames.IndexOf(argumentName));
        }

        public bool? IsDecimalArgument(int index)
        {
            return (index < 0) ? null : DecimalTypes.Contains(this.ArgumentTypes[index]);
        }

        public bool? IsDecimalArgument(string? argumentName)
        {
            return this.IsDecimalArgument(this.ArgumentNames.IndexOf(argumentName));
        }

        public bool? IsStructArgument(int index)
        {
            return (index < 0) ? null : StructTypes.Contains(this.ArgumentTypes[index]);
        }

        public bool? IsStructArgument(string? argumentName)
        {
            return this.IsStructArgument(this.ArgumentNames.IndexOf(argumentName));
        }

        public string? GetStepSize(int index)
        {
            if (index < 0 || index >= this.ArgumentTypes.Length)
            {
                return null;
            }
            string type = this.ArgumentTypes[index];
            if (BooleanTypes.Contains(type))
            {
                return null;
            }
            else if (IntegerTypes.Contains(type))
            {
                return 1.ToString(); // Step size for integer types
            }
            else if (DecimalTypes.Contains(type))
            {
                return type.Contains("double", StringComparison.OrdinalIgnoreCase) ? 0.001m.ToString() : 0.1m.ToString();
            }
            else if (StructTypes.Contains(type))
            {
                return null; // No step size for struct types
            }
            else
            {
                return null; // Unknown type
            }
        }

        public string? GetStepSize(string? argumentName)
        {
            return this.GetStepSize(this.ArgumentNames.IndexOf(argumentName))?.ToString() ?? null;
        }

        public int? GetDecimalPlaces(int index)
        {
            if (index < 0 || index >= this.ArgumentTypes.Length)
            {
                return null;
            }
            string type = this.ArgumentTypes[index];
            if (BooleanTypes.Contains(type))
            {
                return null;
            }
            else if (IntegerTypes.Contains(type))
            {
                return 0; // No decimal places for integer types
            }
            else if (DecimalTypes.Contains(type))
            {
                return type.Contains("double", StringComparison.OrdinalIgnoreCase) ? 6 : 12; // 6 decimal places for double, 12 for float/decimal
            }
            else if (StructTypes.Contains(type))
            {
                return null; // No decimal places for struct types
            }
            else
            {
                return null; // Unknown type
            }
        }

        public int? GetDecimalPlaces(string? argumentName)
        {
            return this.GetDecimalPlaces(this.ArgumentNames.IndexOf(argumentName)) ?? null;
        }

        public decimal? GetMaximumValue(int index)
        {
            if (index < 0 || index >= this.ArgumentTypes.Length)
            {
                return null;
            }
            string type = this.ArgumentTypes[index];
            if (BooleanTypes.Contains(type))
            {
                return 1; // Max value for boolean types
            }
            else if (IntegerTypes.Contains(type))
            {
                return type switch
                {
                    "byte" or "Byte" => byte.MaxValue,
                    "sbyte" or "SByte" => sbyte.MaxValue,
                    "short" or "Int16" => short.MaxValue,
                    "ushort" or "UInt16" => ushort.MaxValue,
                    "int" or "Int32" => int.MaxValue,
                    "uint" or "UInt32" => uint.MaxValue,
                    "long" or "Int64" => long.MaxValue,
                    "ulong" or "UInt64" => ulong.MaxValue,
                    _ => null,
                };
            }
            else if (DecimalTypes.Contains(type))
            {
                return type switch
                {
                    "float" or "Single" => decimal.MaxValue,
                    "double" or "Double" => decimal.MaxValue,
                    "decimal" or "Decimal" => decimal.MaxValue,
                    _ => null,
                };
            }
            else if (StructTypes.Contains(type))
            {
                return null; // No maximum value for struct types
            }
            else
            {
                return null; // Unknown type
            }
        }

        public decimal? GetMaximumValue(string? argumentName)
        {
            return this.GetMaximumValue(this.ArgumentNames.IndexOf(argumentName));
        }

        public decimal? GetMinimumValue(int index)
        {
            if (index < 0 || index >= this.ArgumentTypes.Length)
            {
                return null;
            }
            string type = this.ArgumentTypes[index];
            if (BooleanTypes.Contains(type))
            {
                return 0m; // Min value for boolean types
            }
            else if (IntegerTypes.Contains(type))
            {
                return type switch
                {
                    "byte" or "Byte" => byte.MinValue,
                    "sbyte" or "SByte" => sbyte.MinValue,
                    "short" or "Int16" => short.MinValue,
                    "ushort" or "UInt16" => ushort.MinValue,
                    "int" or "Int32" => int.MinValue,
                    "uint" or "UInt32" => uint.MinValue,
                    "long" or "Int64" => long.MinValue,
                    "ulong" or "UInt64" => ulong.MinValue,
                    _ => null,
                };
            }
            else if (DecimalTypes.Contains(type))
            {
                return type switch
                {
                    "float" or "Single" => decimal.MinValue,
                    "double" or "Double" => decimal.MinValue,
                    "decimal" or "Decimal" => decimal.MinValue,
                    _ => null,
                };
            }
            else if (StructTypes.Contains(type))
            {
                return null; // No minimum value for struct types
            }
            else
            {
                return null; // Unknown type
            }
        }

        public decimal? GetMinimumValue(string? argumentName)
        {
            return this.GetMinimumValue(this.ArgumentNames.IndexOf(argumentName));
        }

        public string GetDefaultValue(string? argumentName)
        {
            return this.GetDefaultValue(this.ArgumentNames.IndexOf(argumentName));
        }

        public string GetDefaultValue(int index)
        {
            if (index < 0 || index >= this.ArgumentTypes.Length)
            {
                throw new IndexOutOfRangeException($"Index {index} is out of the defined kernel arguments (Max: {this.ArgumentTypes.Length - 1}, Names: {string.Join(", ", this.ArgumentNames)}, Types: {string.Join(", ", this.ArgumentTypes)}).");
            }
            string type = this.ArgumentTypes[index];
            if (BooleanTypes.Contains(type))
            {
                return "false"; // Default value for boolean types
            }
            else if (IntegerTypes.Contains(type))
            {
                return "0"; // Default value for integer types
            }
            else if (DecimalTypes.Contains(type))
            {
                return "0.0"; // Default value for decimal types
            }
            else if (StructTypes.Contains(type))
            {
                return "{}"; // Default value for struct types
            }
            else
            {
                return "null"; // Unknown type, default to null
            }
        }


    }
}
