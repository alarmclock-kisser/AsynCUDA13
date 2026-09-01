using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Globalization;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AsynCUDA13.Media
{
    public class AudioObj : IDisposable, IMediaObj
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;


        public float[] Data { get; set; } = [];
        public long Length { get; set; } = 0;
        public int SampleRate { get; set; } = 0;
        public int Channels { get; set; } = 0;
        public int BitDepth { get; set; } = 0;
        public TimeSpan Duration => (this.SampleRate > 0 && this.Channels > 0) ? TimeSpan.FromSeconds((double) this.Length / this.Channels / this.SampleRate) : TimeSpan.Zero;


        public int ChunkSize { get; set; } = 0;
        public float Overlap { get; set; } = 0.5f;
        public long Pointer { get; set; } = IntPtr.Zero;

        public AudioObj()
        {

        }

        public AudioObj(string filePath)
        {
            if (File.Exists(filePath))
            {
                this.LoadFromFile(filePath);
            }
            else
            {
                this.Dispose();
            }
        }

        public AudioObj(float[] data, int sampleRate, int channels, int bitDepth, string name = "")
        {
            this.Data = data;
            this.SampleRate = sampleRate;
            this.Channels = channels;
            this.BitDepth = bitDepth;
            this.Name = name;
        }



        public void Dispose()
        {
            // Clear all data and reset fields
            this.Data = [];
            this.FilePath = string.Empty;
            this.Name = string.Empty;
            this.SampleRate = 0;
            this.Channels = 0;
            this.BitDepth = 0;

            GC.SuppressFinalize(this);
        }



        public bool LoadFromFile(string filePath)
        {
            // Load using NAudio AudioFileReader and set all Fields
            try
            {
                this.FilePath = filePath;
                using (var reader = new AudioFileReader(filePath))
                {
                    this.SampleRate = reader.WaveFormat.SampleRate;
                    this.Channels = reader.WaveFormat.Channels;
                    this.BitDepth = reader.WaveFormat.BitsPerSample;
                    var totalSamples = (int) (reader.Length / (reader.WaveFormat.BitsPerSample / 8));
                    // Ensure we don't allocate absurdly large arrays
                    if (totalSamples < 0 || totalSamples > 1_000_000_000)
                    {
                        totalSamples = 0;
                    }

                    var buffer = new float[totalSamples];
                    int samplesRead = reader.Read(buffer, 0, totalSamples);
                    if (samplesRead < 0)
                    {
                        samplesRead = 0;
                    }

                    this.Length = samplesRead;
                    this.Data = buffer[..samplesRead];
                }
                this.Name = Path.GetFileNameWithoutExtension(filePath);
            }
            catch (Exception ex)
            {
                this.FilePath = string.Empty;
                StaticLogger.Log($"Failed to load audio file: ");
                StaticLogger.Log(ex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Decodes an encoded audio payload (MP3, WAV or FLAC) supplied as a byte array. The bytes are written to a
        /// temporary file so NAudio's <see cref="AudioFileReader"/> can decode them, and the temp file is removed afterwards.
        /// </summary>
        /// <param name="data">The encoded audio bytes.</param>
        /// <param name="extension">The file extension indicating the container format (e.g. ".mp3", ".wav", ".flac").</param>
        /// <param name="name">An optional display name; falls back to a generated name when empty.</param>
        /// <returns><c>true</c> when the payload was decoded successfully; otherwise <c>false</c>.</returns>
        public bool LoadFromBytes(Byte[] data, string extension, string name = "")
        {
            if (data == null || data.Length == 0)
            {
                StaticLogger.Log("Cannot load audio from an empty byte array.");
                return false;
            }

            string ext = string.IsNullOrWhiteSpace(extension) ? ".wav" : extension.StartsWith('.') ? extension : "." + extension;
            string tempPath = Path.Combine(Path.GetTempPath(), $"asyncuda_audio_{Guid.NewGuid():N}{ext}");
            try
            {
                File.WriteAllBytes(tempPath, data);
                if (!this.LoadFromFile(tempPath))
                {
                    return false;
                }

                // The temp path is not a meaningful persistent location for the caller.
                this.FilePath = string.Empty;
                this.Name = string.IsNullOrWhiteSpace(name) ? this.Name : name;
                return true;
            }
            catch (Exception ex)
            {
                StaticLogger.Log("Failed to load audio from bytes", ex);
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignore cleanup failures for the temporary file.
                }
            }
        }

        /// <summary>
        /// Encodes the current samples into an in-memory WAV byte array without touching the file system.
        /// </summary>
        /// <param name="bits">The target bit depth of the WAV output (16 or 32). Defaults to 16-bit PCM.</param>
        /// <returns>The WAV-encoded bytes, or an empty array when there is no data.</returns>
        public Byte[] GetWavBytes(int bits = 16)
        {
            if (this.Data.Length == 0 || this.SampleRate <= 0 || this.Channels <= 0)
            {
                return [];
            }

            try
            {
                using var ms = new MemoryStream();
                WaveFormat format = bits == 32
                    ? WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels)
                    : new WaveFormat(this.SampleRate, 16, this.Channels);

                using (var writer = new WaveFileWriter(ms, format))
                {
                    if (bits == 32)
                    {
                        writer.WriteSamples(this.Data, 0, this.Data.Length);
                    }
                    else
                    {
                        // Convert the float samples to 16-bit PCM.
                        var pcm = new Byte[this.Data.Length * 2];
                        for (int i = 0; i < this.Data.Length; i++)
                        {
                            Int16 value = (Int16) Math.Clamp(this.Data[i] * short.MaxValue, short.MinValue, short.MaxValue);
                            pcm[i * 2] = (Byte) (value & 0xFF);
                            pcm[(i * 2) + 1] = (Byte) ((value >> 8) & 0xFF);
                        }

                        writer.Write(pcm, 0, pcm.Length);
                    }

                    writer.Flush();
                }

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                StaticLogger.Log("Failed to encode audio to WAV bytes", ex);
                return [];
            }
        }

        public async Task<bool> ResampleAsync(int targetSampleRate, int? targetBitDepth = null)
        {
            if (targetSampleRate == this.SampleRate)
            {
                // If only bit depth should change, update it and return
                if (targetBitDepth.HasValue)
                {
                    this.BitDepth = targetBitDepth.Value;
                }
                return true; // Already at target sample rate
            }

            try
            {
                return await Task.Run(() =>
                {
                    // Create a wave format for the current data
                    var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels);
                    var byteData = new Byte[this.Data.Length * sizeof(float)];
                    Buffer.BlockCopy(this.Data, 0, byteData, 0, byteData.Length);

                    using var ms = new MemoryStream(byteData);
                    var sampleProvider = new RawSourceWaveStream(ms, sourceFormat).ToSampleProvider();
                    var resampler = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);

                    // Read resampled data
                    var resampledList = new List<float>();
                    float[] buffer = new float[8192];
                    int samplesRead;
                    while ((samplesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        // AddRange for performance and to avoid multiple resizes
                        if (samplesRead == buffer.Length)
                        {
                            resampledList.AddRange(buffer);
                        }
                        else
                        {
                            for (int i = 0; i < samplesRead; i++)
                            {
                                resampledList.Add(buffer[i]);
                            }
                        }
                    }

                    // Update the AudioObj with resampled data
                    this.Data = resampledList.ToArray();
                    this.Length = this.Data.LongLength;
                    this.SampleRate = targetSampleRate;
                    // Update bit depth if requested, otherwise keep existing
                    if (targetBitDepth.HasValue)
                    {
                        this.BitDepth = targetBitDepth.Value;
                    }

                    return true;
                });
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to resample audio:");
                StaticLogger.Log(ex);
                return false;
            }
        }

        public async Task<bool> RechannelAsync(int targetChannels)
        {
            if (targetChannels == this.Channels)
            {
                return true;
            }

            try
            {
                return await Task.Run(async () =>
                {
                    var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels);
                    Byte[] byteData = new Byte[this.Data.Length * sizeof(float)];
                    var provider = new BufferedWaveProvider(sourceFormat)
                    {
                        BufferLength = byteData.Length,
                        ReadFully = false
                    };
                    Buffer.BlockCopy(this.Data, 0, byteData, 0, byteData.Length);
                    provider.AddSamples(byteData, 0, byteData.Length);

                    var sampleProvider = provider.ToSampleProvider();


                    if (targetChannels != 1 && targetChannels != 2)
                    {
                        // Use the exact other than set channels, if it's not mono or stereo
                        targetChannels = this.Channels == 1 ? 2 : 1;
                        await StaticLogger.LogAsync($"Invalid bitdepth detected ({targetChannels}). Using {targetChannels} since audio has {this.Channels} channels.");
                    }

                    ISampleProvider rechanneledProvider;
                    if (targetChannels == 1)
                    {
                        rechanneledProvider = new StereoToMonoSampleProvider(sampleProvider);
                    }
                    else if (targetChannels == 2)
                    {
                        rechanneledProvider = new MonoToStereoSampleProvider(sampleProvider);
                    }
                    else
                    {
                        // This should never happen due to the check above, but just in case
                        StaticLogger.Log($"Unexpected target channel count: {targetChannels}. No rechanneling applied.");
                        return false;
                    }

                    var rechanneledList = new List<float>();
                    float[] buffer = new float[8192];
                    int samplesRead;
                    while ((samplesRead = rechanneledProvider.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (samplesRead == buffer.Length)
                        {
                            rechanneledList.AddRange(buffer);
                        }
                        else
                        {
                            for (int i = 0; i < samplesRead; i++)
                            {
                                rechanneledList.Add(buffer[i]);
                            }
                        }
                    }

                    this.Data = rechanneledList.ToArray();
                    this.Length = this.Data.LongLength;
                    this.Channels = targetChannels;

                    return true;
                });
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to rechannel audio:");
                StaticLogger.Log(ex);
                return false;
            }
        }


        public float[][] GetChunks(int chunkSize, float overlap = 0.5f, bool keepData = true)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));
            }
            if (overlap < 0 || overlap >= 1)
            {
                throw new ArgumentException("Overlap must be between 0 (inclusive) and 1 (exclusive).", nameof(overlap));
            }

            int stepSize = (int) (chunkSize * (1 - overlap));
            if (stepSize <= 0)
            {
                throw new ArgumentException("Step size must be greater than zero. Adjust chunk size or overlap.", nameof(overlap));
            }

            List<float[]> chunks = [];
            for (int start = 0; start < this.Data.Length; start += stepSize)
            {
                int end = Math.Min(start + chunkSize, this.Data.Length);
                float[] chunk = new float[chunkSize];
                Array.Copy(this.Data, start, chunk, 0, end - start);
                // Padding am letzten Chunk füllen mit Wert 0
                if (end - start < chunkSize)
                {
                    for (int i = end - start; i < chunkSize; i++)
                    {
                        chunk[i] = 0f;
                    }
                }
                chunks.Add(chunk);
                if (end == this.Data.Length)
                {
                    break; // Reached the end of the data
                }
            }

            if (!keepData)
            {
                this.Data = [];
            }

            this.ChunkSize = chunkSize;
            this.Overlap = overlap;

            return chunks.ToArray();
        }

        public async Task AggregateChunksAsync(IEnumerable<IEnumerable<float>> chunks, int? chunkSize = null, float? overlap = null, bool keepPointer = false)
        {
            chunkSize ??= this.ChunkSize;
            overlap ??= this.Overlap;

            if (chunkSize <= 0)
            {
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));
            }

            if (overlap < 0 || overlap >= 1)
            {
                throw new ArgumentException("Overlap must be between 0 (inclusive) and 1 (exclusive).", nameof(overlap));
            }

            await Task.Run(() =>
            {
                List<float> aggregatedData = [];
                int stepSize = (int) (chunkSize.Value * (1 - overlap.Value));
                foreach (var chunk in chunks)
                {
                    float[] chunkArray = chunk.ToArray();
                    if (aggregatedData.Count == 0)
                    {
                        aggregatedData.AddRange(chunkArray);
                    }
                    else
                    {
                        // Overlap handling
                        int overlapStartIndex = Math.Max(0, aggregatedData.Count - stepSize);
                        for (int i = 0; i < chunkArray.Length; i++)
                        {
                            if (overlapStartIndex + i < aggregatedData.Count)
                            {
                                // Average overlapping samples
                                aggregatedData[overlapStartIndex + i] = (aggregatedData[overlapStartIndex + i] + chunkArray[i]) / 2f;
                            }
                            else
                            {
                                aggregatedData.Add(chunkArray[i]);
                            }
                        }
                    }
                }
                this.Data = aggregatedData.ToArray();
                this.Length = this.Data.LongLength;
            });

            if (!keepPointer)
            {
                this.Pointer = IntPtr.Zero;
            }
        }

        public async Task NormalizeAsync(float targetLevel = 1.0f)
        {
            await Task.Run(() =>
            {
                if (this.Data.Length == 0)
                {
                    return;
                }
                float maxAmplitude = this.Data.Max(Math.Abs);
                if (maxAmplitude == 0)
                {
                    return; // Avoid division by zero
                }
                float normalizationFactor = targetLevel / maxAmplitude;
                for (int i = 0; i < this.Data.Length; i++)
                {
                    this.Data[i] *= normalizationFactor;
                }
            });
        }



        public string? ExportWav(string? outputDirectory = null, string? fileName = null, int bits = 16)
        {
            outputDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "SharpAI_AudioExports");
            if (string.IsNullOrEmpty(outputDirectory))
            {
                StaticLogger.Log("Export directory is not set.");
                return null;
            }

            if (!Directory.Exists(outputDirectory))
            {
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    StaticLogger.Log($"Audio output directory '{outputDirectory}' created.");
                }
                catch (Exception ex)
                {
                    StaticLogger.Log($"Failed to create export directory: {outputDirectory}");
                    StaticLogger.Log(ex);
                    return null;
                }
            }

            if (this.Data.LongLength <= 0 || this.SampleRate <= 0 || this.Channels <= 0)
            {
                StaticLogger.Log("Audio data is empty or invalid. Cannot export.");
                return null;
            }

            // Dateinamen bestimmen (Name, Id oder Fallback)
            string baseName = fileName ?? (!string.IsNullOrEmpty(this.Name) ? this.Name : this.Id.ToString());
            string outputPath = Path.Combine(outputDirectory, $"{baseName}.wav");

            // Falls Datei existiert, Index anhängen (z.B. "Aufnahme (1).wav")
            int copyIndex = 1;
            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(outputDirectory, $"{baseName} ({copyIndex++}).wav");
            }

            string? outFile;
            try
            {
                // Bestimme die Ausgabebit-Tiefe: falls der Caller den Standard (16) übergeben hat,
                // aber dieses AudioObj eine eigene BitDepth gesetzt hat, benutze diese.
                int outputBits = bits;
                if (this.BitDepth > 0 && bits == 16)
                {
                    outputBits = this.BitDepth;
                }

                // NAudio WaveFormat definieren. Für 32 Bit nutzen wir das IEEE-Float-Format.
                WaveFormat format;
                if (outputBits == 32)
                {
                    format = WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels);
                }
                else
                {
                    format = new WaveFormat(this.SampleRate, outputBits, this.Channels);
                }

                using (var writer = new WaveFileWriter(outputPath, format))
                {
                    // Die float-Daten in den Writer schreiben
                    // WriteSamples bei NAudio konvertiert automatisch basierend auf dem 'format'
                    writer.WriteSamples(this.Data, 0, this.Data.Length);
                }

                StaticLogger.Log($"Audio exported successfully: {outputPath}");
                outFile = outputPath;
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to export audio to WAV: {outputPath}");
                StaticLogger.Log(ex);
                outFile = null;
            }

            return outFile;
        }

        public async Task<string?> ExportWavAsync(string? outputDirectory = null, string? fileName = null, int bits = 16)
        {
            return await Task.Run(() => this.ExportWav(outputDirectory, fileName, bits));
        }

        public async Task<string?> SerializeAsBase64Async(int? sampleRate = null, int? channels = null, int? bitDepth = null)
        {
            if (sampleRate.HasValue)
            {
                bool success = await this.ResampleAsync(sampleRate.Value, bitDepth);
                if (!success)
                {
                    await StaticLogger.LogAsync($"Failed to resample audio for Base64 serialization. Aborting.");
                    return null;
                }
            }

            if (channels.HasValue)
            {
                bool success = await this.RechannelAsync(channels.Value);
                if (!success)
                {
                    await StaticLogger.LogAsync("Failed to rechannel audio for Base64 serialization. Aborting.");
                    return null;
                }
            }

            return await Task.Run(() =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        // NAudio WaveFormat definieren
                        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels);
                        using (var writer = new WaveFileWriter(ms, format))
                        {
                            // Die float-Daten in den Writer schreiben
                            writer.WriteSamples(this.Data, 0, this.Data.Length);
                            writer.Flush();
                        }
                        // Konvertiere den MemoryStream in ein Base64-string
                        string base64string = Convert.ToBase64String(ms.ToArray());
                        return base64string;
                    }
                }
                catch (Exception ex)
                {
                    StaticLogger.Log($"Failed to serialize audio as Base64:");
                    StaticLogger.Log(ex);
                    return null;
                }
            });
        }

        public ImageObj GenerateWaveform(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return new ImageObj(0, 0);
            }

            var waveformImage = new Image<Rgba32>(width, height);

            if (this.Data.Length == 0)
            {
                var emptyImageObj = new ImageObj(width, height, "#00000000");
                return emptyImageObj;
            }

            // Farben für die Wellenform definieren
            var lineColor = SixLabors.ImageSharp.Color.FromRgb(0, 123, 255);

            // Audio-Daten für die Wellenform normalisieren
            float maxAmplitude = this.Data.Length > 0 ? this.Data.Max(Math.Abs) : 1f;
            if (maxAmplitude == 0)
            {
                maxAmplitude = 1f;
            }

            // Wellenform generieren
            int samples = this.Data.Length;
            float samplesPerPixel = samples / (float) width;

            for (int x = 0; x < width; x++)
            {
                // Mittelwert der Samples für diesen Pixelbereich berechnen
                int startSample = (int) (x * samplesPerPixel);
                int endSample = (int) ((x + 1) * samplesPerPixel);
                endSample = Math.Min(endSample, samples);

                if (endSample <= startSample)
                {
                    continue;
                }

                float sum = 0f;
                for (int i = startSample; i < endSample; i++)
                {
                    sum += Math.Abs(this.Data[i]);
                }
                float avgAmplitude = sum / (endSample - startSample);

                // Y-Position basierend auf Amplitude berechnen
                int y = (int) (height / 2.0f - (avgAmplitude * height / 2.0f / maxAmplitude));
                y = Math.Max(0, Math.Min(height - 1, y));

                // Linie zeichnen (ein Pixel breit)
                if (y >= 0 && y < height)
                {
                    waveformImage[x, y] = lineColor;
                }

                // Optional: zwei Linien für die Wellenform (positiv und negativ)
                int yPositive = (int) (height / 2.0f + (avgAmplitude * height / 2.0f / maxAmplitude));
                int yNegative = (int) (height / 2.0f - (avgAmplitude * height / 2.0f / maxAmplitude));

                yPositive = Math.Max(0, Math.Min(height - 1, yPositive));
                yNegative = Math.Max(0, Math.Min(height - 1, yNegative));

                if (yPositive >= 0 && yPositive < height)
                {
                    waveformImage[x, yPositive] = lineColor;
                }
                if (yNegative >= 0 && yNegative < height)
                {
                    waveformImage[x, yNegative] = lineColor;
                }
            }

            // Ergebnis-ImageObj erstellen
            var imageObj = new ImageObj(width, height)
            {
                Img = waveformImage,
                Width = width,
                Height = height
            };

            return imageObj;
        }
    }
}