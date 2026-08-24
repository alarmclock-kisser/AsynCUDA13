using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.MediaDtos;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp.Formats.Png;

namespace AsynCUDA13.Api.Controllers
{
    public class ImageKernelController : ApiControllerBase
    {
        private readonly ICudaService cuda;
        private readonly ImageCollection images;

        public ImageKernelController(ICudaService cuda, ImageCollection images)
        {
            this.cuda = cuda;
            this.images = images;
        }

        [HttpPost("execute-image/{kernelName}/{imageIdOrNameOrPath}")]
        public async Task<ActionResult<ImageData?>> ExecuteImageAsync(string imageIdOrNameOrPath, string kernelName, IEnumerable<string> argumentValues, bool overwriteImage = true, bool unloadKernelAfterExecution = false)
        {
            if (!this.cuda.Online || this.cuda._compiler == null || this.cuda._launcher == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA service is offline",
                    Detail = "The CUDA service is currently offline. Please ensure that the CUDA service is running and try again.",
                    Status = 503
                };

                return this.StatusCode(503, pd);
            }

            try
            {
                var imageObj = Guid.TryParse(imageIdOrNameOrPath, out var imageId) ? this.images[imageId] : this.images[imageIdOrNameOrPath] ?? (System.IO.File.Exists(imageIdOrNameOrPath) ? await this.images.LoadImageAsync(imageIdOrNameOrPath) : null);
                if (imageObj == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Image not found",
                        Detail = $"The image with ID or name '{imageIdOrNameOrPath}' was not found.",
                        Status = 404
                    };

                    return this.StatusCode(404, pd);
                }

                this.cuda.SetCurrent();
                this.cuda._compiler.LoadKernel(kernelName);
                if (string.IsNullOrEmpty(this.cuda._compiler.KernelName))
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Kernel not found",
                        Detail = $"The kernel with name '{kernelName}' was not found.",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                var mem = await this.cuda.PushDataAsync(await imageObj.GetBytesAsync());
                if (mem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Failed to push image data to GPU",
                        Detail = "The image data could not be pushed to the GPU memory.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var outputMem = await this.cuda.AllocateSingleAsync<Byte>((IntPtr) mem.TotalLength);
                if (outputMem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Failed to allocate output memory on GPU",
                        Detail = "The output memory could not be allocated on the GPU.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                object[] arguments = this.cuda._compiler.MergeArgumentsImage(mem, outputMem, imageObj.Width, imageObj.Height, imageObj.Channels, imageObj.Bitdepth, argumentValues.ToArray());

                var elapsedMs = await this.cuda._launcher.ExecuteGenericKernelAsync(kernelName, arguments, unloadKernelAfterExecution);
                if (!elapsedMs.HasValue)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Kernel execution failed",
                        Detail = "The kernel execution did not complete successfully.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                if (overwriteImage)
                {
                    await imageObj.SetImageAsync(await this.cuda.PullDataAsync<Byte>(outputMem.IndexPointer) ?? throw new InvalidOperationException("Failed to pull data from GPU."));
                }
                else
                {
                    imageObj = new ImageObj(await this.cuda.PullDataAsync<Byte>(outputMem.IndexPointer) ?? throw new InvalidOperationException("Failed to pull data from GPU."), imageObj.Width, imageObj.Height, imageObj.Name + "_" + kernelName);
                }

                return MediaDatasBuilder.BuildImageData(imageObj);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error executing image kernel",
                    Detail = ex.Message,
                    Status = 500
                };

                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("execute-image-file")]
        public async Task<IActionResult> ExecuteImageFileAsync(IFormFile imageFile, [FromForm] string kernelName, [FromForm] IEnumerable<string> argumentValues, [FromForm] bool overwriteImage = true, [FromForm] bool unloadKernelAfterExecution = false)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                var pd = new ProblemDetails
                {
                    Title = "No image file provided",
                    Detail = "Please provide a valid image file to execute the kernel on.",
                    Status = 400
                };
                return this.StatusCode(400, pd);
            }
            var tempFilePath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                if (!this.cuda.Online || this.cuda._compiler == null || this.cuda._launcher == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA service is offline",
                        Detail = "The CUDA service is currently offline. Please ensure that the CUDA service is running and try again.",
                        Status = 503
                    };
                    return this.StatusCode(503, pd);
                }

                try
                {
                    var imageObj = this.images[tempFilePath] ?? await this.images.LoadImageAsync(tempFilePath);
                    if (imageObj == null)
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Image not found",
                            Detail = $"The image could not be loaded from the uploaded file.",
                            Status = 400
                        };
                        return this.StatusCode(400, pd);
                    }

                    this.cuda.SetCurrent();
                    this.cuda._compiler.LoadKernel(kernelName);
                    if (string.IsNullOrEmpty(this.cuda._compiler.KernelName))
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Kernel not found",
                            Detail = $"The kernel with name '{kernelName}' was not found.",
                            Status = 404
                        };
                        return this.StatusCode(404, pd);
                    }

                    var mem = await this.cuda.PushDataAsync(await imageObj.GetBytesAsync());
                    if (mem == null)
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Failed to push image data to GPU",
                            Detail = "The image data could not be pushed to the GPU memory.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }

                    var outputMem = await this.cuda.AllocateSingleAsync<Byte>((IntPtr) mem.TotalLength);
                    if (outputMem == null)
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Failed to allocate output memory on GPU",
                            Detail = "The output memory could not be allocated on the GPU.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }

                    object[] arguments = this.cuda._compiler.MergeArgumentsImage(mem, outputMem, imageObj.Width, imageObj.Height, imageObj.Channels, imageObj.Bitdepth, argumentValues.ToArray());

                    var elapsedMs = await this.cuda._launcher.ExecuteGenericKernelAsync(kernelName, arguments, unloadKernelAfterExecution);
                    if (!elapsedMs.HasValue)
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Kernel execution failed",
                            Detail = "The kernel execution did not complete successfully.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }

                    if (overwriteImage)
                    {
                        await imageObj.SetImageAsync(await this.cuda.PullDataAsync<Byte>(outputMem.IndexPointer) ?? throw new InvalidOperationException("Failed to pull data from GPU."));
                    }
                    else
                    {
                        imageObj = new ImageObj(await this.cuda.PullDataAsync<Byte>(outputMem.IndexPointer) ?? throw new InvalidOperationException("Failed to pull data from GPU."), imageObj.Width, imageObj.Height, imageObj.Name + "_" + kernelName);
                    }

                    var imageBytes = await imageObj.GetImageAsFileFormatAsync(new PngEncoder());
                    return this.File(imageBytes, "image/png");
                }
                catch (Exception ex)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Error executing image kernel",
                        Detail = StaticLogger.GetAllInnerExceptionsRecursively(ex),
                        Status = 500
                    };

                    return this.StatusCode(500, pd);
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }


        // Specific scoped image kernel function endpoints

        [HttpPost("execute-image-scoped/edge_detection")]
        public async Task<IActionResult> ExecuteImageScoped_EdgeDetectionAsync(IFormFile imageFile, [FromForm] int thickness = 1, [FromForm] float threshold = 0.125f, [FromForm] int edgeR = 255, [FromForm] int edgeG = 0, [FromForm] int edgeB = 0, [FromForm] int deviceId = 0, [FromForm] string kernelVersion = "")
        {
            // Only pass user-defined arguments (edgeR, edgeG, edgeB, thickness, threshold)
            // MergeArgumentsImage automatically handles input/output pointers and width/height/channels/bitdepth
            string[] args = [edgeR.ToString(), edgeG.ToString(), edgeB.ToString(), thickness.ToString(), threshold.ToString()];

            try
            {
                if (!this.cuda.Online || this.cuda.SelectedDeviceId != deviceId)
                {
                    this.cuda.Dispose();
                    this.cuda.Initialize(deviceId);
                }

                string kernelName = string.IsNullOrEmpty(kernelVersion) ? "edge_detection" : $"edge_detection_{kernelVersion}";

                return await this.ExecuteImageFileAsync(imageFile, kernelName, args, overwriteImage: true, unloadKernelAfterExecution: true);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error executing edge detection kernel",
                    Detail = ex.Message,
                    Status = 500
                };

                return this.StatusCode(500, pd);
            }
            finally
            {
                this.cuda.Dispose();
            }
        }


    }
}
