using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Shared.MediaDtos;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    public class MediaController : ApiControllerBase
    {
        private readonly ImageCollection images;
        private readonly AudioCollection audios;

        public MediaController(ImageCollection images, AudioCollection audios)
        {
            this.images = images;
            this.audios = audios;
        }

        [HttpGet("images")]
        public ActionResult<IEnumerable<ImageInfo>> GetImages()
        {
            try
            {
                var imageInfos = this.images.Images.Select(MediaInfosBuilder.BuildImageInfo);
                return this.Ok(imageInfos);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving images",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("audios")]
        public ActionResult<IEnumerable<AudioInfo>> GetAudios()
        {
            try
            {
                var audioInfos = this.audios.Audios.Select(MediaInfosBuilder.BuildAudioInfo);
                return this.Ok(audioInfos);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving audios",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("upload-media")]
        public async Task<ActionResult<IMediaInfo>> UploadMediaAsync(IFormFile file)
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

            try
            {
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

                    var imageInfo = MediaInfosBuilder.BuildImageInfo(img);
                    return this.Ok(imageInfo);
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

                    var audioInfo = MediaInfosBuilder.BuildAudioInfo(audio);
                    return this.Ok(audioInfo);
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

        [HttpGet("image-data/{idOrName}")]
        public ActionResult<ImageData?> GetImageData(string idOrName, string format = "png", bool keepData = true)
        {
            try
            {
                var image = Guid.TryParse(idOrName, out var guid) ? this.images[guid] : this.images[idOrName];
                if (image == null)
                {
                    return this.NotFound(new ProblemDetails
                    {
                        Title = "Image not found",
                        Detail = $"No image found with ID or name '{idOrName}'.",
                        Status = 404
                    });
                }

                var imageData = MediaDatasBuilder.BuildImageData(image, format, keepData);
                return this.Ok(imageData);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving image data",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("audio-data/{idOrName}")]
        public ActionResult<AudioData?> GetAudioData(string idOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            try
            {
                var audio = Guid.TryParse(idOrName, out var guid) ? this.audios[guid] : this.audios[idOrName];
                if (audio == null)
                {
                    return this.NotFound(new ProblemDetails
                    {
                        Title = "Audio not found",
                        Detail = $"No audio found with ID or name '{idOrName}'.",
                        Status = 404
                    });
                }

                var audioData = MediaDatasBuilder.BuildAudioData(audio, chunkSize, overlap, keepData);
                return this.Ok(audioData);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving audio data",
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
                if (!deleted)
                {
                    return this.NotFound(new ProblemDetails
                    {
                        Title = "Media not found",
                        Detail = $"No media found with ID or name '{idOrName}'.",
                        Status = 404
                    });
                }
                return this.NoContent();
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
    }
}
