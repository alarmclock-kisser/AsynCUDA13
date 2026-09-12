// In-place (IP, float I/O pointer) solarization effect. Inverts pixels above threshold. Channel-agnostic: processes first up-to-3 color channels. Alpha left unchanged. Linear dispatch: launch with global size = width*height.

__kernel void solarize_image_IP(__global unsigned char* image, int width, int height, int channels, int threshold)
{
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int idx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    int thr = clamp(threshold, 0, 255);

    for (int c = 0; c < colorChannels; c++)
    {
        if (image[idx + c] >= thr)
        {
            image[idx + c] = 255 - image[idx + c];
        }
    }

    // Alpha left unchanged.
}
