using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Reflection;
using AsynCUDA13.Shared.Interfaces;
using Color = SixLabors.ImageSharp.Color;
using Size = SixLabors.ImageSharp.Size;

namespace AsynCUDA13.Media
{
    public class ImageCollection : IDisposable, IMediaCollection
    {
        private readonly ConcurrentDictionary<Guid, ImageObj> images = [];
        private readonly object lockObj = new();

        public string ExportDirectory { get; set; } = Path.GetFullPath(Environment.GetEnvironmentVariable("SHARPAI_IMAGE_EXPORT_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SharpAI_ImageExports"));

        public IReadOnlyCollection<ImageObj> Images => this.images.Values.ToList();
        public IReadOnlyCollection<IMediaObj> Objects => this.images.Values.Cast<IMediaObj>().ToList();

        public ImageObj? this[Guid guid] => this.images.TryGetValue(guid, out ImageObj? imageObj) ? imageObj : null;
        IMediaObj? IMediaCollection.this[Guid id] => this[id];

        public ImageObj? this[string name]
        {
            get
            {
                lock (this.lockObj)
                {
                    return this.images.Values.FirstOrDefault(img => img.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        public ImageObj? this[int index]
        {
            get
            {
                lock (this.lockObj)
                {
                    return this.images.Values.ElementAtOrDefault(index);
                }
            }
        }

        IMediaObj? IMediaCollection.this[int index] => this[index];

        // Options
        public string ImportPath { get; set; } = string.Empty;
        public string ExportPath { get; set; } = string.Empty;
        public bool SaveMemory { get; set; } = false;
        public int DefaultWidth { get; set; } = 720;
        public int DefaultHeight { get; set; } = 480;
        public int MaxImages { get; set; } = 0;

        // Ctor with options
        public ImageCollection(bool saveMemory = false, int defaultWidth = 720, int defaultHeight = 480, int maxImages = 0, bool loadResources = false)
        {
            this.DefaultWidth = Math.Max(defaultWidth, 360); // Min is 360px width
            this.DefaultHeight = Math.Max(defaultHeight, 240); // Min is 240px height
            this.MaxImages = Math.Max(maxImages, 0); // 0 means no limit
            this.SaveMemory = saveMemory;
            if (this.SaveMemory)
            {
                Console.WriteLine("ImageCollection: Memory saving enabled. All images will be disposed on add.");
            }

            if (loadResources)
            {
                var _ = this.LoadResourcesAsync().Result;
            }
        }

        public bool Add(ImageObj imgObj)
        {
            if (this.SaveMemory)
            {
                // Dispose every image
                lock (this.lockObj)
                {
                    foreach (var i in this.images.Values)
                    {
                        i.Dispose();
                    }

                    this.images.Clear();
                }
            }

            bool added = this.images.TryAdd(imgObj.Id, imgObj);
            if (added && this.MaxImages > 0)
            {
                // Ensure collection respects max limit by removing oldest items if necessary
                this.ApplyImagesLimitAsync().GetAwaiter().GetResult();
            }

            return added;
        }

        public ImageObj? CreateFromInfo(Shared.MediaDtos.ImageInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true, bool emptyData = false, long? pointer = 0)
        {
            long ptr = pointer ?? (long.TryParse(info.Pointer, out var p) ? p : 0);
            ImageObj obj = new(info.Width, info.Height)
            {
                Img = emptyData ? null : new Image<Rgba32>(info.Width, info.Height),
                Bitdepth = info.BitDepth,
                Channels = info.Channels,
                CreatedAt = info.CreatedAt,
                FilePath = info.FilePath,
                Name = info.Name,
                Height = info.Height,
                Width = info.Width,
                Pointer = ptr,
                Meta = info.Meta
            };

            if (tryAdd)
            {
                if (this.images.TryAdd(obj.Id, obj))
                {
                    return obj;
                }
                else if (disposeIfFailedToAdd)
                {
                    obj.Dispose();
                    return null;
                }
            }

            return obj;
        }

        public bool Remove(Guid guid)
        {
            bool result = this.images.TryRemove(guid, out ImageObj? imgObj);
            if (result && imgObj != null)
            {
                imgObj.Dispose();
                Console.WriteLine($"Removed and disposed image '{imgObj.Name}' (ID: {imgObj.Id}).");
            }
            else
            {
                Console.WriteLine($"Failed to remove image with ID: {guid}. It might not exist.");
            }

            return result;
        }

        public async Task ClearAsync()
        {
            await Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    foreach (var imgObj in this.images.Values)
                    {
                        imgObj.Dispose();
                        Console.WriteLine($"Disposed image '{imgObj.Name}' (Guid: {imgObj.Id}).");
                    }

                    this.images.Clear();
                }
            });
        }

        public async Task<IEnumerable<Guid>?> LoadResourcesAsync(string? customResourcesPath = null)
        {
            List<Guid> loadedGuids = [];

            string? resolvedResourcesPath = null;
            if (!string.IsNullOrWhiteSpace(customResourcesPath))
            {
                if (Directory.Exists(customResourcesPath))
                {
                    resolvedResourcesPath = Path.GetFullPath(customResourcesPath);
                    Console.WriteLine($"LoadResourcesAsync: Using custom Resources directory at '{resolvedResourcesPath}'");
                }
                else
                {
                    Console.WriteLine($"LoadResourcesAsync: Custom Resources directory not found at '{customResourcesPath}'");
                }
            }
            else
            {
                // Try get project Resources directory relative to current executing assembly (bin/Debug/... -> project root)
                var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources"));
                if (Directory.Exists(devPath))
                {
                    resolvedResourcesPath = devPath;
                }
                else
                {
                    // If not in DEV environment, try relative to EXE
                    var exePath = Path.Combine(AppContext.BaseDirectory, "Resources");
                    if (Directory.Exists(exePath))
                    {
                        resolvedResourcesPath = exePath;
                    }
                    else
                    {
                        Console.WriteLine($"LoadResourcesAsync: Resources directory not found at '{exePath}'");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(resolvedResourcesPath))
            {
                string[] resourceImageFiles = Directory.GetFiles(resolvedResourcesPath)
                    .Where(file => SupportedFormats.Contains(Path.GetExtension(file).TrimStart('.').ToLower()))
                    .ToArray();
                if (resourceImageFiles.Length > 0)
                {
                    var loadTasks = resourceImageFiles.Select(file => this.LoadImageAsync(file)).ToArray();
                    var loadedImages = await Task.WhenAll(loadTasks);

                    loadedGuids.AddRange(loadedImages.Where(img => img != null).Select(img => img!.Id));
                    Console.WriteLine($"LoadResourcesAsync: Loaded {resourceImageFiles.Length} images from Resources directory at '{resolvedResourcesPath}'");
                }
                else
                {
                    Console.WriteLine($"LoadResourcesAsync: No supported image files found in Resources directory at '{resolvedResourcesPath}'");
                }
            }

            var assembly = typeof(ImageCollection).Assembly;
            string[] resourceNames = assembly.GetManifestResourceNames()
                .Where(name => SupportedFormats.Contains(Path.GetExtension(name).TrimStart('.').ToLower()))
                .ToArray();

            if (resourceNames.Length > 0)
            {
                var embeddedTasks = resourceNames.Select(name => this.LoadEmbeddedResourceAsync(assembly, name)).ToArray();
                var embeddedResults = await Task.WhenAll(embeddedTasks);
                loadedGuids.AddRange(embeddedResults.Where(id => id.HasValue).Select(id => id!.Value));
                Console.WriteLine($"LoadResourcesAsync: Loaded {embeddedResults.Count(id => id.HasValue)} embedded image resources from assembly.");
            }

            if (loadedGuids.Count == 0)
            {
                var fallback = await this.PopEmptyAsync(new Size(1024, 1024), "FallbackEmptyImage", true);
                if (fallback != null)
                {
                    loadedGuids.Add(fallback.Id);
                    Console.WriteLine("LoadResourcesAsync: No resources found. Added fallback empty image.");
                }
            }

            return loadedGuids;
        }

        private async Task<Guid?> LoadEmbeddedResourceAsync(Assembly assembly, string resourceName)
        {
            try
            {
                await using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    Console.WriteLine($"LoadResourcesAsync: Resource stream not found for '{resourceName}'");
                    return null;
                }

                ImageObj? imgObj = await this.LoadImageFromStreamAsync(resourceStream, Path.GetFileNameWithoutExtension(resourceName));
                if (imgObj != null && this.Add(imgObj))
                {
                    Console.WriteLine($"Loaded and added embedded image '{imgObj.Name}' (ID: {imgObj.Id}) from manifest resource '{resourceName}'.");
                    return imgObj.Id;
                }

                imgObj?.Dispose();
                Console.WriteLine($"Failed to add embedded image from resource '{resourceName}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading embedded image '{resourceName}': {ex.Message}");
            }

            return null;
        }

        private async Task<ImageObj?> LoadImageFromStreamAsync(Stream stream, string resourceName)
        {
            try
            {
                ImageObj? obj = await Task.Run(() =>
                {
                    using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                    Byte[] pixelData = new Byte[image.Width * image.Height * 4];
                    image.CopyPixelDataTo(pixelData);
                    return new ImageObj(pixelData, image.Width, image.Height, resourceName);
                });

                return obj;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating ImageObj from stream '{resourceName}': {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            this.ClearAsync().Wait();
            GC.SuppressFinalize(this);
        }

        public async Task<ImageObj?> LoadImageAsync(string filePath, string? name = null)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"LoadImageAsync: File not found or path empty: {filePath}");
                return null;
            }

            ImageObj obj;
            try
            {
                obj = await Task.Run(() =>
                {
                    return new ImageObj(filePath);
                });
                if (!string.IsNullOrEmpty(name))
                {
                    obj.Name = name;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    obj = new ImageObj(filePath);
                }
                catch (Exception innerEx)
                {
                    Console.WriteLine($"Error creating ImageObj from file '{filePath}': {innerEx.Message}");
                    return null;
                }

                Console.WriteLine($"Error loading image from file '{filePath}': {ex.Message}");
                return null;
            }

            if (this.Add(obj))
            {
                Console.WriteLine($"Loaded and added image '{obj.Name}' (ID: {obj.Id}) from file.");
                return obj;
            }

            // obj.Dispose();
            Console.WriteLine($"Failed to add image '{obj.Name}' (ID: {obj.Id}). An image with this ID might already exist.");
            return null;
        }

        public async Task<ImageObj?> PopEmptyAsync(Size? size = null, string? name = null, bool add = false)
        {
            size ??= new Size(1080, 1920);
            int number = this.images.Count + 1;
            int digits = (int) Math.Log10(number) + 1;

            ImageObj imgObj;
            try
            {
                imgObj = await Task.Run(() =>
                {
                    lock (this.lockObj)
                    {
                        return new ImageObj(new Byte[size.Value.Width * size.Value.Height * 4], size.Value.Width, size.Value.Height, $"EmptyImage_{number.ToString().PadLeft(digits, '0')}");
                    }
                });
                if (!string.IsNullOrEmpty(name))
                {
                    imgObj.Name = name;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating empty image: {ex.Message}");
                return null;
            }

            if (add)
            {
                if (this.Add(imgObj))
                {
                    Console.WriteLine($"Created and added empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}.");
                    return imgObj;
                }

                imgObj.Dispose();
                Console.WriteLine($"Failed to add empty image '{imgObj.Name}' (ID: {imgObj.Id}). An image with this ID might already exist.");
                return null;
            }

            Console.WriteLine($"Created empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}, but not added to collection.");
            return imgObj;
        }

        public async Task<string?> ExportImageAsync(Guid guid, string? exportPath = null, string format = "png")
        {
            exportPath ??= this.ExportPath;
            ImageObj? obj = this[guid];
            return obj != null ? await obj.ExportAsync(exportPath, format) :  null;
        }

        public async Task<int> CleanupOldImagesAsync(int maxImages = 1)
        {
            return await Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    int removedCount = 0;
                    while (this.images.Count > maxImages)
                    {
                        var oldest = this.images.Values.OrderBy(img => img.CreatedAt).FirstOrDefault();
                        if (oldest != null)
                        {
                            if (this.images.TryRemove(oldest.Id, out _))
                            {
                                oldest.Dispose();
                                removedCount++;
                                Console.WriteLine($"Cleaned up and disposed old image '{oldest.Name}' (ID: {oldest.Id}).");
                            }
                        }
                        else
                        {
                            break; // No more images to remove
                        }
                    }
                    return removedCount;
                }
            });
        }



        public static Size GetSharpSize(int height, int width)
        {
            width = Math.Clamp(width, 1, 32768);
            height = Math.Clamp(height, 1, 32768);

            return new Size(width, height);
        }

        public static Color? GetSharpColor(System.Drawing.Color color)
        {
            return color == System.Drawing.Color.Empty ?  null :  SixLabors.ImageSharp.Color.FromRgba(color.R, color.G, color.B, color.A);
        }

        public static Color GetSharpColor(string hexColor = "#00000000")
        {
            if (string.IsNullOrWhiteSpace(hexColor))
            {
                hexColor = "#00000000";
            }
            if (!hexColor.StartsWith("#"))
            {
                hexColor = "#" + hexColor;
            }
            try
            {
                return SixLabors.ImageSharp.Color.ParseHex(hexColor);
            }
            catch
            {
                return SixLabors.ImageSharp.Color.FromRgba(0, 0, 0, 0);
            }
        }

        public static System.Drawing.Color GetDrawingColor(Color color)
        {
            var rgba = color.ToPixel<Rgba32>();
            return System.Drawing.Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
        }

        // Fügen Sie diese statische Eigenschaft in die Klasse ImageCollection ein
        public static readonly HashSet<string> SupportedFormats =
        [
        "png",
        "jpg",
        "jpeg",
        "bmp",
        "gif",
        "tiff"
        ];


        public static int[] GetRgbFromHexColor(string hexColor)
        {
            if (string.IsNullOrWhiteSpace(hexColor))
            {
                return [0, 0, 0];
            }

            // Remove # if present
            if (hexColor.StartsWith("#"))
            {
                hexColor = hexColor[1..];
            }

            try
            {
                if (hexColor.Length == 6)
                {
                    // RRGGBB format
                    int r = Convert.ToInt16(hexColor.Substring(0, 2), 16);
                    int g = Convert.ToInt16(hexColor.Substring(2, 2), 16);
                    int b = Convert.ToInt16(hexColor.Substring(4, 2), 16);

                    Console.WriteLine($"Resolved hex-Color: #{hexColor} to R: {r} G: {g} B: {b}");
                    return [r, g, b];
                }
                else if (hexColor.Length == 8)
                {
                    // AARRGGBB format - extract RGB and ignore alpha
                    int r = Convert.ToInt16(hexColor.Substring(2, 2), 16);
                    int g = Convert.ToInt16(hexColor.Substring(4, 2), 16);
                    int b = Convert.ToInt16(hexColor.Substring(6, 2), 16);
                    int a = Convert.ToInt16(hexColor.Substring(0, 2), 16);

                    Console.WriteLine($"Resolved hex-Color: #{hexColor} to R: {r} G: {g} B: {b} A: {a}");
                    return [r, g, b, a];
                }
                else if (hexColor.Length == 3)
                {
                    // RGB shorthand format
                    int r = Convert.ToInt16(hexColor[0].ToString() + hexColor[0].ToString(), 16);
                    int g = Convert.ToInt16(hexColor[1].ToString() + hexColor[1].ToString(), 16);
                    int b = Convert.ToInt16(hexColor[2].ToString() + hexColor[2].ToString(), 16);

                    Console.WriteLine($"Resolved hex-Color: #{hexColor} to R: {r} G: {g} B: {b}");
                    return [r, g, b];
                }
                else
                {
                    Console.WriteLine($"Invalid hex color length: {hexColor} (Expected 3, 6 or 8 characters)");
                    return [0, 0, 0];
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not resolve hex-Color: {hexColor} - Error: {ex.Message}");
                return [0, 0, 0];
            }
        }

        public async Task<int> ApplyImagesLimitAsync()
        {
            return this.MaxImages > 0 && this.images.Count > this.MaxImages ? await this.CleanupOldImagesAsync(this.MaxImages) : 0;
        }


    }
}