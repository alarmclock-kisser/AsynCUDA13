using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AsynCUDA13.Shared.CudaDtos
{
    public class CudaMemInfo
    {
        public string Id { get; set; } = string.Empty;
        public string IndexPointer => this.Pointers.FirstOrDefault() ?? "0";


        public string ElementType { get; set; } = string.Empty;
        public string IndexLength => this.Lengths.Length == 0 ? "null" : (this.Lengths.All(l => l == this.Lengths[0]) ? this.Lengths[0] : "N/A");

        public Int32 ElementSize
        {
            get
            {
                if (string.IsNullOrEmpty(this.ElementType))
                {
                    return 0;
                }

                var match = System.Text.RegularExpressions.Regex.Match(this.ElementType, @"^(.+?)(\d+)$");
                if (match.Success)
                {
                    var baseTypeName = match.Groups[1].Value;
                    var multiplier = int.Parse(match.Groups[2].Value);
                    var baseTypeSimpleName = baseTypeName.Contains('.') ? baseTypeName.Substring(baseTypeName.LastIndexOf('.') + 1) : baseTypeName;
                    baseTypeSimpleName = baseTypeSimpleName.Replace("float", "float", StringComparison.OrdinalIgnoreCase);
                    var type = Type.GetType(baseTypeName) ??
                                 Type.GetType($"System.{baseTypeSimpleName}") ??
                                 Type.GetType(baseTypeSimpleName) ??
                                 typeof(void);
                    return Marshal.SizeOf(type) * multiplier;
                }

                return Marshal.SizeOf(Type.GetType(this.ElementType) ?? typeof(void));
            }
        }
        public Int32? Count => (this.Pointers.LongLength == this.Lengths.LongLength) ? (global::System.Int32) this.Pointers.LongLength : null;
        public string LongCount => this.Pointers.LongLength == this.Lengths.LongLength ? this.Pointers.LongLength.ToString() : "N/A";
        public string TotalLength => this.Lengths.Sum(l => long.TryParse(l, out Int64 len) ? len : 0).ToString();
        public string TotalSize => (long.TryParse(this.TotalLength, out Int64 totalLen) ? totalLen : 0 * this.ElementSize).ToString();


        public string[] Lengths { get; set; } = [];
        public string[] Pointers { get; set; } = [];

        public Boolean IsValid => this.Pointers.Length == this.Lengths.Length && this.Pointers.Length > 0 && !string.IsNullOrEmpty(this.ElementType);

        public string Message { get; set; } = string.Empty;
    }
}
