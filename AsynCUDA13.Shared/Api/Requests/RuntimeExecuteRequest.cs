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



        public void UpdateImageArgs(ImageInfo imageInfo)
        {
            if (this.KernelInfo == null || this.KernelInfo.ArgumentsCount == null)
            {
                return;
            }

            // 1. Felder filtern, die in den erlaubten Typen sind
            var argFields = imageInfo.GetType().GetFields()
                .Where(f => DataSerializer.ArgumentFieldTypes.Contains(f.FieldType))
                .ToList();
            var argProperties = imageInfo.GetType().GetProperties()
                .Where(p => DataSerializer.ArgumentFieldTypes.Contains(p.PropertyType))
                .ToList();

            // 2. Namen in ein HashSet für schnellen Zugriff (ohne "Pointer")
            var args = new HashSet<string>(
                argFields.Select(f => f.Name).Concat(argProperties.Select(p => p.Name)).Where(n => !n.Equals("Pointer")),
                StringComparer.OrdinalIgnoreCase
            );

            // 3. Über die Kernel-Argumente iterieren
            for (int i = 0; i < this.KernelInfo.ArgumentsCount; i++)
            {
                string argName = this.KernelInfo.ArgumentNames[i];

                // Prüfen: Ist der Name im DTO vorhanden UND ist der aktuelle Wert der Default-Wert?
                if (args.Contains(argName) && this.ArgumentValues[i].Equals(this.KernelInfo.GetDefaultValue(i)))
                {
                    // Das Feld finden, das exakt diesen Namen hat
                    var field = argFields.FirstOrDefault(f => string.Equals(f.Name, argName, StringComparison.OrdinalIgnoreCase));
                    var property = argProperties.FirstOrDefault(p => string.Equals(p.Name, argName, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        var value = field.GetValue(imageInfo)?.ToString();
                        this.ArgumentValues[i] = value ?? this.KernelInfo.GetDefaultValue(i);
                    }
                    else if (property != null)
                    {
                        var value = property.GetValue(imageInfo)?.ToString();
                        this.ArgumentValues[i] = value ?? this.KernelInfo.GetDefaultValue(i);
                    }
                }
            }
        }

        public void UpdateAudioArgs(AudioInfo audioInfo)
        {
            if (this.KernelInfo == null || this.KernelInfo.ArgumentsCount == null)
            {
                return;
            }

            // 1. Felder filtern, die in den erlaubten Typen sind
            var argFields = audioInfo.GetType().GetFields()
                .Where(f => DataSerializer.ArgumentFieldTypes.Contains(f.FieldType))
                .ToList();
            var argProperties = audioInfo.GetType().GetProperties()
                .Where(p => DataSerializer.ArgumentFieldTypes.Contains(p.PropertyType))
                .ToList();

            // 2. Namen in ein HashSet für schnellen Zugriff (ohne "Pointer")
            var args = new HashSet<string>(
                argFields.Select(f => f.Name).Concat(argProperties.Select(p => p.Name)).Where(n => !n.Equals("Pointer")),
                StringComparer.OrdinalIgnoreCase
            );

            // 3. Über die Kernel-Argumente iterieren
            for (int i = 0; i < this.KernelInfo.ArgumentsCount; i++)
            {
                string argName = this.KernelInfo.ArgumentNames[i];

                // Prüfen: Ist der Name im DTO vorhanden UND ist der aktuelle Wert der Default-Wert?
                if (args.Contains(argName) && this.ArgumentValues[i].Equals(this.KernelInfo.GetDefaultValue(i)))
                {
                    // Das Feld finden, das exakt diesen Namen hat
                    var field = argFields.FirstOrDefault(f => string.Equals(f.Name, argName, StringComparison.OrdinalIgnoreCase));
                    var property = argProperties.FirstOrDefault(p => string.Equals(p.Name, argName, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        var value = field.GetValue(audioInfo)?.ToString();
                        this.ArgumentValues[i] = value ?? this.KernelInfo.GetDefaultValue(i);
                    }
                    else if (property != null)
                    {
                        var value = property.GetValue(audioInfo)?.ToString();
                        this.ArgumentValues[i] = value ?? this.KernelInfo.GetDefaultValue(i);
                    }
                }
            }
        }
    }
}