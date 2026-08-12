using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.CudaDtos;

namespace AsynCUDA13.Shared.Serialization
{
    public static class DataParser
    {
        public static int ParallelThreads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

        public static async Task<object[]?> ParseAsync(CudaPayload1D payload, string elementType)
        {
            Type? t = Type.GetType(elementType, throwOnError: false, ignoreCase: true);
            if (t == null)
            {
                await StaticLogger.LogAsync("[DataParser] Error parsing element type: " + elementType);
                return null;
            }

            // Reflection to call the generic method ParseAsync<T> with the resolved type
            var method = typeof(DataParser).GetMethod(nameof(ParseAsync), new Type[] { typeof(CudaPayload1D) });
            var task = method?.MakeGenericMethod(t).Invoke(null, new object[] { payload }) as Task;

            if (task == null)
            {
                return null;
            }

            await task.ConfigureAwait(false);

            // Get the result from the completed task
            var resultProperty = task.GetType().GetProperty("Result");
            var result = resultProperty?.GetValue(task);

            // Cast to object[] (which is compatible with T[] for reference types)
            return result as object[];
        }

        public static async Task<object[][]?> ParseAsync(CudaPayload2D payload, string elementType)
        {
            Type? t = Type.GetType(elementType, throwOnError: false, ignoreCase: true);
            if (t == null)
            {
                await StaticLogger.LogAsync("[DataParser] Error parsing element type: " + elementType);
                return null;
            }

            // Reflection to call the generic method ParseAsync<T> with the resolved type
            var method = typeof(DataParser).GetMethod(nameof(ParseAsync), new Type[] { typeof(CudaPayload2D) });
            var task = method?.MakeGenericMethod(t).Invoke(null, new object[] { payload }) as Task;

            if (task == null)
            {
                return null;
            }

            await task.ConfigureAwait(false);

            // Get the result from the completed task
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task) as object[][];
        }


        // --------------------------------------------------------------------------------
        // 1D Deserialization (Zero-Allocation & Paralleles Base64-Slicing)
        // --------------------------------------------------------------------------------
        public static async Task<T[]?> ParseAsync<T>(CudaPayload1D payload) where T : unmanaged
        {
            if (payload == null || string.IsNullOrEmpty(payload.Data))
            {
                return [];
            }

            return await Parse1DAsync<T>(payload.Data);
        }

        public static async Task<T[]> Parse1DAsync<T>(string base64Data) where T : unmanaged
        {
            if (string.IsNullOrEmpty(base64Data))
            {
                return [];
            }

            int totalBytes = GetByteCountFromBase64(base64Data.AsSpan());
            int elementSize = Unsafe.SizeOf<T>();
            int totalElements = totalBytes / elementSize;

            // Einzige Allokation: Das finale Ziel-Array
            T[] result = new T[totalElements];
            int degreeOfParallelism = Math.Max(1, ParallelThreads);

            // Bei kleineren Datenmengen oder 1 Thread sofort im aktuellen Kontext dekodieren
            if (base64Data.Length < 100_000 || degreeOfParallelism == 1)
            {
                Span<byte> targetBytes = MemoryMarshal.AsBytes(result.AsSpan());
                Convert.TryFromBase64String(base64Data, targetBytes, out _);
                return result;
            }

            // Paralleles Dekodieren direkt in das Ziel-Array
            await Task.Run(() =>
            {
                // Base64 hat Blöcke von 4 Chars (= 3 Bytes). 
                // Slice-Größe muss zwingend durch 4 teilbar sein.
                int rawChunkChars = base64Data.Length / degreeOfParallelism;
                int chunkChars = Math.Max(4, (rawChunkChars / 4) * 4);
                int chunkCount = (int) Math.Ceiling((double) base64Data.Length / chunkChars);

                Parallel.For(0, chunkCount, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, chunkIdx =>
                {
                    int startChar = chunkIdx * chunkChars;
                    int countChar = Math.Min(chunkChars, base64Data.Length - startChar);

                    if (countChar <= 0)
                    {
                        return;
                    }

                    int startByte = (startChar / 4) * 3;

                    // Thread-safe Slice auf die Bytes des Ziel-Arrays
                    Span<byte> targetSlice = MemoryMarshal.AsBytes(result.AsSpan()).Slice(startByte);

                    Convert.TryFromBase64Chars(base64Data.AsSpan(startChar, countChar), targetSlice, out _);
                });
            });

            return result;
        }

        // --------------------------------------------------------------------------------
        // 2D Deserialization (Parallel.ForAsync Workerpool)
        // --------------------------------------------------------------------------------
        public static async Task<T[][]?> ParseAsync<T>(CudaPayload2D payload) where T : unmanaged
        {
            if (payload == null || payload.DataChunks == null)
            {
                return [];
            }

            return await Parse2DAsync<T>(payload.DataChunks);
        }

        public static async Task<T[][]> Parse2DAsync<T>(IEnumerable<string> chunks) where T : unmanaged
        {
            string[] chunkArray = chunks as string[] ?? chunks.ToArray();

            if (chunkArray.Length == 0)
            {
                return [];
            }

            T[][] results = new T[chunkArray.Length][];

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, ParallelThreads)
            };

            // Async Workerpool über alle 2D-Chunks
            await Parallel.ForAsync(0, chunkArray.Length, options, (i, ct) =>
            {
                string chunkStr = chunkArray[i];

                if (string.IsNullOrEmpty(chunkStr))
                {
                    results[i] = [];
                    return ValueTask.CompletedTask;
                }

                int totalBytes = GetByteCountFromBase64(chunkStr.AsSpan());
                int elementSize = Unsafe.SizeOf<T>();
                int totalElements = totalBytes / elementSize;

                T[] items = new T[totalElements];

                // Direkt ins Chunk-Array dekodieren ohne Zwischen-Byte-Array
                Span<byte> targetBytes = MemoryMarshal.AsBytes(items.AsSpan());
                Convert.TryFromBase64String(chunkStr, targetBytes, out _);

                results[i] = items;
                return ValueTask.CompletedTask;
            });

            return results;
        }

        public static object[] ParseArgumentValues(IEnumerable<string> args, CudaKernelInfo? kernelInfo)
        {
            if (kernelInfo == null)
            {
                return args.Select(arg => (object) arg).ToArray();
            }

            // Parse each argument based on the corresponding type in kernelInfo.ArgumentTypes
            return args.Select((arg, index) =>
            {
                if (index >= kernelInfo.ArgumentTypes.Length)
                {
                    return (object) arg; // Fallback to string if no type info is available
                }
                string argType = kernelInfo.ArgumentTypes[index];
                Type? t = Type.GetType(argType, throwOnError: false, ignoreCase: true);
                if (t == null)
                {
                    return (object) arg; // Fallback to string if type resolution fails
                }
                try
                {
                    if(t.IsPointer)
                    {
                        return IntPtr.TryParse(arg, out var ptr) ? ptr : IntPtr.Zero;
                    }

                    // Convert the argument to the specified type
                    return Convert.ChangeType(arg, t);
                }
                catch
                {
                    return (object) arg; // Fallback to string if conversion fails
                }
            }).ToArray();
        }

        // --------------------------------------------------------------------------------
        // Hilfsmethode: Exakte Byte-Anzahl aus Base64-String berechnen (inkl. Padding-Check)
        // --------------------------------------------------------------------------------
        private static int GetByteCountFromBase64(ReadOnlySpan<char> base64)
        {
            if (base64.IsEmpty)
            {
                return 0;
            }

            int padding = 0;
            if (base64[^1] == '=')
            {
                padding++;
            }

            if (base64.Length > 1 && base64[^2] == '=')
            {
                padding++;
            }

            return (base64.Length / 4) * 3 - padding;
        }
    }
}