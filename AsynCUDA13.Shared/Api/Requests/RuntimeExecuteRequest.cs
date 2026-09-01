using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeExecuteRequest
    {
        public RuntimeKernelInfo? KernelInfo
        {
            get;
            set
            {
                int argumentsCount = value?.ArgumentsCount ?? 0;
                this._argumentValues = new string[argumentsCount];
                for (int i = 0; i < argumentsCount; i++)
                {
                    this._argumentValues[i] = value?.GetDefaultValue(i) ?? string.Empty;
                }

                field = value;
            }
        }

        private string[] _argumentValues = [];

        public string[] ArgumentValues
        {
            get
            {
                int argumentsCount = this.KernelInfo?.ArgumentsCount ?? 0;
                if (this._argumentValues.Length != argumentsCount)
                {
                    Array.Resize(ref this._argumentValues, argumentsCount);
                }

                for (int i = 0; i < argumentsCount; i++)
                {
                    this._argumentValues[i] ??= this.KernelInfo?.GetDefaultValue(i) ?? string.Empty;
                }

                return this._argumentValues;
            }
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

        public bool CreateResultPointerAssetReference { get; set; } = false;

        public string[] PointerArguments => this._argumentValues.Where((a, i) => this.KernelInfo?.ArgumentTypes[i].Contains('*') == true).ToArray();

        public void UpdateImageArgs(ImageInfo imageInfo) => this.UpdateMediaArgs(imageInfo);

        public void UpdateAudioArgs(AudioInfo audioInfo) => this.UpdateMediaArgs(audioInfo);

        private void UpdateMediaArgs<T>(T mediaInfo) where T : notnull
        {
            RuntimeKernelInfo? kernelInfo = this.KernelInfo;
            if (kernelInfo?.ArgumentsCount == null)
            {
                return;
            }

            // 1. Felder filtern, die in den erlaubten Typen sind
            var argFields = mediaInfo.GetType().GetFields()
                .Where(f => DataSerializer.ArgumentFieldTypes.Contains(f.FieldType))
                .ToList();
            var argProperties = mediaInfo.GetType().GetProperties()
                .Where(p => DataSerializer.ArgumentFieldTypes.Contains(p.PropertyType))
                .ToList();

            // 2. Namen in ein HashSet für schnellen Zugriff (ohne "Pointer")
            var args = new HashSet<string>(
                argFields.Select(f => f.Name).Concat(argProperties.Select(p => p.Name)).Where(n => !string.Equals(n, "Pointer", StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase
            );

            // 3. Über die Kernel-Argumente iterieren
            for (int i = 0; i < kernelInfo.ArgumentsCount.Value; i++)
            {
                string argName = kernelInfo.ArgumentNames[i];

                // Prüfen: Ist der Name im DTO vorhanden UND ist der aktuelle Wert der Default-Wert?
                if (args.Contains(argName) && string.Equals(this.ArgumentValues[i], kernelInfo.GetDefaultValue(i), StringComparison.Ordinal))
                {
                    // Das Feld finden, das exakt diesen Namen hat
                    var field = argFields.FirstOrDefault(f => string.Equals(f.Name, argName, StringComparison.OrdinalIgnoreCase));
                    var property = argProperties.FirstOrDefault(p => string.Equals(p.Name, argName, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        var value = field.GetValue(mediaInfo)?.ToString();
                        this.ArgumentValues[i] = value ?? kernelInfo.GetDefaultValue(i) ?? string.Empty;
                    }
                    else if (property != null)
                    {
                        var value = property.GetValue(mediaInfo)?.ToString();
                        this.ArgumentValues[i] = value ?? kernelInfo.GetDefaultValue(i) ?? string.Empty;
                    }
                }
            }
        }
    }
}