using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeExecuteRequest
    {
        public RuntimeKernelInfo? KernelInfo { get; set; } = null;

        private string[] _argumentValues = [];

        public string[] ArgumentValues
        {
            get => this._argumentValues.Length == (this.KernelInfo?.ArgumentsCount ?? 0) ? this._argumentValues : new string[this.KernelInfo?.ArgumentsCount ?? 0];
            set
            {
                if (value.Length == (this.KernelInfo?.ArgumentsCount ?? 0))
                {
                    this._argumentValues = value;
                }
            }
        }

        public string this[int index]
        {
            get
            {
                // 1. Validierung gegen KernelInfo
                int maxArgs = this.KernelInfo?.ArgumentsCount ?? -1;
                if (maxArgs < 0 || index < 0 || index >= maxArgs)
                {
                    throw new IndexOutOfRangeException($"Index {index} is out of the defined kernel arguments (Max: {maxArgs - 1}, Names: {string.Join(", ", this.KernelInfo?.ArgumentNames ?? [])}, Types: {string.Join(", ", this.KernelInfo?.ArgumentTypes ?? [])}).");
                }

                // 2. Automatically expand to the full length of the KernelInfo
                // If the array does not yet have the target size, we expand it now
                if (this._argumentValues.Length != maxArgs)
                {
                    Array.Resize(ref this._argumentValues, maxArgs);

                    // Initialize all new elements with string.Empty
                    for (int i = this._argumentValues.Length - 1; i >= 0; i--)
                    {
                        this._argumentValues[i] ??= this.KernelInfo?.GetDefaultValue(i) ?? string.Empty;
                    }
                }

                return this._argumentValues[index];
            }
            set
            {
                // 1. Validierung gegen KernelInfo
                int maxArgs = this.KernelInfo?.ArgumentsCount ?? -1;
                if (maxArgs < 0 || index < 0 || index >= maxArgs)
                {
                    throw new IndexOutOfRangeException($"Index {index} is out of the defined kernel arguments (Max: {maxArgs - 1}, Names: {string.Join(", ", this.KernelInfo?.ArgumentNames ?? [])}, Types: {string.Join(", ", this.KernelInfo?.ArgumentTypes ?? [])}).");
                }

                // 2. Ensure that the array has the target size
                if (this._argumentValues.Length != maxArgs)
                {
                    Array.Resize(ref this._argumentValues, maxArgs);
                    for (int i = this._argumentValues.Length - 1; i >= 0; i--)
                    {
                        this._argumentValues[i] ??= this.KernelInfo?.GetDefaultValue(i) ?? string.Empty;
                    }
                }

                this._argumentValues[index] = value;
            }
        }

        public bool AsyncCall { get; set; } = true;
        public bool UnloadAfterExecution { get; set; } = false;
    }
}