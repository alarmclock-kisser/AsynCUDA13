using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : ApiControllerBase
    {
        private readonly ImageCollection images;
        private readonly AudioCollection audios;

        public MediaController(IRuntimeService cudaService, ImageCollection images, AudioCollection audios, IRollingFileMemoryLogger logger)
            : base(cudaService, logger)
        {
            this.images = images;
            this.audios = audios;
        }

        [HttpGet("media-infos")]
        public ActionResult<IEnumerable<IMediaInfo>> GetMediaInfos()
        {
            try
            {
                var imageInfos = this.images.Images.Select(MediaInfosBuilder.BuildImageInfo).Cast<IMediaInfo>();
                var audioInfos = this.audios.Audios.Select(MediaInfosBuilder.BuildAudioInfo).Cast<IMediaInfo>();
                var allInfos = imageInfos.Concat(audioInfos).ToList();
                return this.Ok(allInfos);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving media infos",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("upload-media")]
        public async Task<ActionResult<string?>> UploadMediaAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return this.BadRequest(new ProblemDetails
                {
                    Title = "Invalid file",
                    Detail = "No file was uploaded or the file is empty.",
                    Status = 400
                });
            }

            string tempFilePath = Path.GetTempFileName();
            string originalFileName = Path.GetFileNameWithoutExtension(file.FileName);

            try
            {
                string? mediaId = null;
                // Copy to temp path
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    var img = await this.images.LoadImageAsync(tempFilePath);
                    if (img == null)
                    {
                        return this.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid image file",
                            Detail = "The uploaded file could not be processed as an image.",
                            Status = 400
                        });
                    }
                    this.images[img.Id]?.Name = originalFileName;

                    mediaId = MediaInfosBuilder.BuildImageInfo(img)?.Id.ToString();
                    return this.Ok(mediaId);
                }
                else if (file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    var audio = await this.audios.ImportAudioAsync(tempFilePath);
                    if (audio == null)
                    {
                        return this.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid audio file",
                            Detail = "The uploaded file could not be processed as an audio.",
                            Status = 400
                        });
                    }
                    this.audios[audio.Id]?.Name = originalFileName;

                    mediaId = MediaInfosBuilder.BuildAudioInfo(audio)?.Id.ToString();
                    return this.Ok(mediaId);
                }
                else
                {
                    return this.BadRequest(new ProblemDetails
                    {
                        Title = "Unsupported file type",
                        Detail = $"The uploaded file type '{file.ContentType}' is not supported. Please upload an image or audio file.",
                        Status = 400
                    });
                }
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error uploading media",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
            finally
            {
                // Clean up temp file
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }

        [HttpGet("download-media")]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 404)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<IActionResult> DownloadMediaAsync(string idOrName, string format = "png", float normalizeAudio = 1.0f, bool pullIfRequired = true, bool keepBufferWhenPulled = false)
        {
            string tempFilePath = string.Empty;

            try
            {
                tempFilePath = Path.GetTempFileName();

                var image = Guid.TryParse(idOrName, out var guid) ? this.images[guid] : this.images[idOrName];
                if (image != null)
                {
                    if (pullIfRequired && image.Pointer != 0 && image.Pointer != IntPtr.Zero)
                    {
                        await image.SetImageAsync(await this.backend.Register.PullDataAsync<Byte>((IntPtr) image.Pointer, keepBufferWhenPulled) ?? throw new InvalidOperationException("Failed to pull image data from CUDA."));
                    }

                    // Export image with format to temp path
                    tempFilePath = await this.images.ExportImageAsync(image.Id, tempFilePath, format) ?? tempFilePath;
                    var contentType = format.ToLower() switch
                    {
                        "jpg" or "jpeg" => "image/jpeg",
                        "bmp" => "image/bmp",
                        "gif" => "image/gif",
                        _ => "image/png"
                    };

                    var fileBytes = await System.IO.File.ReadAllBytesAsync(tempFilePath);
                    return this.File(fileBytes, contentType, $"{image.Name}.{format}");
                }
                var audio = Guid.TryParse(idOrName, out guid) ? this.audios[guid] : this.audios[idOrName];
                if (audio != null)
                {
                    if (pullIfRequired && audio.Pointer != 0 && audio.Pointer != IntPtr.Zero)
                    {
                        IRuntimeMem audioMem = this.backend[(IntPtr) audio.Pointer] ?? throw new InvalidOperationException("Failed to retrieve audio data from CUDA.");
                        if (audioMem.Count > 1)
                        {
                            await audio.AggregateChunksAsync(await this.backend.Register.PullChunksAsync<float>((IntPtr) audio.Pointer, keepBufferWhenPulled) ?? throw new InvalidOperationException("Failed to pull audio data from CUDA."), (int) audioMem.IndexLength);
                        }
                        else
                        {
                            audio.Data = await this.backend.Register.PullDataAsync<float>((IntPtr) audio.Pointer, keepBufferWhenPulled) ?? throw new InvalidOperationException("Failed to pull audio data from CUDA.");
                        }
                    }

                    if (normalizeAudio > 0)
                    {
                        await audio.NormalizeAsync(normalizeAudio);
                    }

                    // Export audio with bits from format to temp path
                    tempFilePath = await audio.ExportWavAsync(Path.GetDirectoryName(tempFilePath), null, int.TryParse(format, out int bits) ? bits : 16) ?? tempFilePath;

                    var fileBytes = await System.IO.File.ReadAllBytesAsync(tempFilePath);
                    return this.File(fileBytes, "application/octet-stream", $"{audio.Name}.wav");
                }
                return this.NotFound(new ProblemDetails
                {
                    Title = "Media not found",
                    Detail = $"No media found with ID or name '{idOrName}'.",
                    Status = 404
                });
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error downloading media",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
            finally
            {
                // Clean up temp file
                if (!string.IsNullOrEmpty(tempFilePath) && System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }

        [HttpGet("media-data/{idOrName}")]
        public ActionResult<IMediaData?> GetMediaData(string idOrName, string format = "png", int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            try
            {
                var image = Guid.TryParse(idOrName, out var guid) ? this.images[guid] : this.images[idOrName];
                if (image != null)
                {
                    var mediaData = MediaDatasBuilder.BuildImageData(image, format, keepData);
                    return this.Ok(mediaData);
                }

                var audio = Guid.TryParse(idOrName, out guid) ? this.audios[guid] : this.audios[idOrName];
                if (audio != null)
                {
                    var mediaData = MediaDatasBuilder.BuildAudioData(audio, chunkSize, overlap, keepData);
                    return this.Ok(mediaData);
                }

                return this.NotFound(new ProblemDetails
                {
                    Title = "Media not found",
                    Detail = $"No media found with ID or name '{idOrName}'.",
                    Status = 404
                });
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving media data",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("media-preview/{idOrName}")]
        public ActionResult<IMediaData?> GetMediaPreview(string idOrName, int maxDimenions = 256, int width = 512, int height = 128)
        {
            try
            {
                var image = Guid.TryParse(idOrName, out var guid) ? this.images[guid] : this.images[idOrName];
                if (image != null)
                {
                    var mediaPreview = MediaDatasBuilder.BuildImagePreview(image, maxDimenions);
                    return this.Ok(mediaPreview);
                }

                var audio = Guid.TryParse(idOrName, out guid) ? this.audios[guid] : this.audios[idOrName];
                if (audio != null)
                {
                    var mediaPreview = MediaDatasBuilder.BuildAudioPreview(audio, width, height);
                    return this.Ok(mediaPreview);
                }

                return this.NotFound(new ProblemDetails
                {
                    Title = "Media not found",
                    Detail = $"No media found with ID or name '{idOrName}'.",
                    Status = 404
                });
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving media preview",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


        [HttpDelete("delete/{idOrName}")]
        public ActionResult DeleteMedia(string idOrName)
        {
            try
            {
                bool deleted = false;
                if (Guid.TryParse(idOrName, out var guid))
                {
                    deleted = this.images.Remove(guid) || this.audios.RemoveAudio(guid);
                }
                else
                {
                    guid = this.images[idOrName]?.Id ?? this.audios[idOrName]?.Id ?? Guid.Empty;
                    deleted = this.images.Remove(guid) || this.audios.RemoveAudio(idOrName);
                }
                return !deleted
                    ?  this.NotFound(new ProblemDetails
                    {
                        Title = "Media not found",
                        Detail = $"No media found with ID or name '{idOrName}'.",
                        Status = 404
                    })
                    :  this.NoContent();
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error deleting media",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("clear-all")]
        public async Task<IActionResult> ClearAllMediaAsync()
        {
            try
            {
                await this.images.ClearAsync();
                await this.audios.ClearAudiosAsync();
                return this.NoContent();
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error clearing media",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }
    }
}
