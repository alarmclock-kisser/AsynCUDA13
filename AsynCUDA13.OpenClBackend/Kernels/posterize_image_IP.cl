// In-place (IP, float I/O pointer) posterization filter. Reduces color levels per channel. Channel-agnostic. Linear dispatch: launch with global size = width*height.

__kernel void posterize_image_IP(__global unsigned char* image, int width, int height, int channels, int levels)
{
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int idx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    levels = clamp(levels, 1, 255);
    if (levels >= 255) return;

    float step = 256.0f / (float)levels;

    for (int c = 0; c < colorChannels; c++)
    {
        float v = (float)image[idx + c];
        v = (float)((int)(v / step + 0.5f)) * step;
        v = clamp(v, 0.0f, 255.0f);
        image[idx + c] = (unsigned char)v;
    }

    // Alpha left unchanged.
}
