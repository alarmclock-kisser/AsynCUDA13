using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AsynCUDA13.Shared.CudaDtos
{
    public class CudaMemInfo
    {
        public string Id { get; set; } = string.Empty;
        public string IndexPointer => this.Pointers.FirstOrDefault() ?? "null";


        public string ElementType { get; set; } = string.Empty;
        public string IndexLength => this.Lengths.Any(l => l != this.Lengths.FirstOrDefault()) ? "N/A" : this.Lengths.FirstOrDefault() ?? "null";


        public int ElementSize => Marshal.SizeOf(Type.GetType(this.ElementType) ?? typeof(void));
        public int? Count => (this.Pointers.LongLength == this.Lengths.LongLength) ? (int) this.Pointers.LongLength : 0;
        public string TotalLength => this.Lengths.Select(l => long.TryParse(l, out long len) ? len : 0).Sum().ToString();
        public string TotalSize => (long.TryParse(this.TotalLength, out long totalLen) ? totalLen : 0 * this.ElementSize).ToString();


        public string[] Lengths { get; set; } = [];
        public string[] Pointers { get; set; } = [];


        public string Message { get; set; } = string.Empty;
    }
}
