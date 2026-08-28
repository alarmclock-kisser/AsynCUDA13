using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AsynCUDA13.Shared.Api.Payloads;

namespace AsynCUDA13.Shared.Serialization
{
    public static class DataSerializer
    {
        public static readonly Type[] ArgumentFieldTypes = [typeof(int), typeof(float), typeof(string)];


        public static int ParallelThreads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

        // --------------------------------------------------------------------------------
        // 1D Serialization (Sektor-Aufteilung über Threads)
        // --------------------------------------------------------------------------------
        public static async Task<ISimdPayload?> SerializeAsync<T>(IEnumerable<T> data, Boolean asyncCall = true) where T : unmanaged
        {
            T[] items = data as T[] ?? data.ToArray();

            if (items.Length == 0)
            {
                return new SimdPayload1D
                {
                    AsyncCall = asyncCall,
                    ElementType = typeof(T).Name,
                    Data = string.Empty
                };
            }

            int degreeOfParallelism = Math.Max(1, ParallelThreads);
            int totalItems = items.Length;

            if (totalItems < degreeOfParallelism)
            {
                degreeOfParallelism = 1;
            }

            // Ausrichtung berechnen: Byte-Länge pro Sektor muss durch 3 teilbar sein,
            // damit beim Zusammensetzen der Base64-strings keine Padding-Fehler '=' entstehen.
            int elementSize = Unsafe.SizeOf<T>();
            int alignment = 3 / GreatCommonDivisor(3, elementSize); // Z. B. 3 Elemente bei float/int, 3 bei double

            int itemsPerChunk = (Int32) Math.Ceiling((double) totalItems / degreeOfParallelism);
            itemsPerChunk = ((itemsPerChunk + alignment - 1) / alignment) * alignment;

            int actualChunkCount = (Int32) Math.Ceiling((double) totalItems / itemsPerChunk);
            string[] sectorResults = new string[actualChunkCount];

            // Non-blocking Ausführung auf dem ThreadPool
            await Task.Run(() =>
            {
                Parallel.For(0, actualChunkCount, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, sectorIdx =>
                {
                    int start = sectorIdx * itemsPerChunk;
                    int count = Math.Min(itemsPerChunk, totalItems - start);

                    if (count <= 0)
                    {
                        return;
                    }

                    // Zero-Allocation Memory-View auf das unmanaged Array
                    ReadOnlySpan<T> slice = items.AsSpan(start, count);
                    ReadOnlySpan<Byte> byteSlice = MemoryMarshal.AsBytes(slice);

                    // Sektor-Array ist thread-safe, da jeder Index eindeutig zugewiesen ist
                    sectorResults[sectorIdx] = Convert.ToBase64String(byteSlice);
                });
            });

            return new SimdPayload1D
            {
                AsyncCall = asyncCall,
                ElementType = typeof(T).Name,
                Data = string.Concat(sectorResults)
            };
        }

        // --------------------------------------------------------------------------------
        // 2D Serialization (Chunk-Workerpool mit Parallel.ForAsync)
        // --------------------------------------------------------------------------------
        public static async Task<ISimdPayload?> SerializeAsync<T>(IEnumerable<IEnumerable<T>> data, Boolean asyncCall = true) where T : unmanaged
        {
            // Materialisieren der äußeren Chunks als Arrays für schnellen Span-Zugriff
            List<T[]> chunkList = data.Select(c => c as T[] ?? c.ToArray()).ToList();

            if (chunkList.Count == 0)
            {
                return new SimdPayload2D
                {
                    AsyncCall = asyncCall,
                    ElementType = typeof(T).Name,
                    DataChunks = []
                };
            }

            string[] results = new string[chunkList.Count];

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, ParallelThreads)
            };

            // Echter async Worker-Pool: Begrenzt auf ParallelThreads, non-blocking & thread-safe
            await Parallel.ForAsync(0, chunkList.Count, options, (i, ct) =>
            {
                ReadOnlySpan<T> span = chunkList[i].AsSpan();
                ReadOnlySpan<Byte> byteSpan = MemoryMarshal.AsBytes(span);

                results[i] = Convert.ToBase64String(byteSpan);
                return ValueTask.CompletedTask;
            });

            return new SimdPayload2D
            {
                AsyncCall = asyncCall,
                ElementType = typeof(T).Name,
                DataChunks = results
            };
        }

        public static int GreatCommonDivisor(Int32 a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

    }
}