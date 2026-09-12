// Out-of-place (OOP, separate input/output pointers) posterization filter. Reduces color levels per channel. Channel-agnostic. Linear dispatch: launch with global size = width*height.

__kernel void posterize_image_OOP(__global const unsigned char* inputPixels, __global unsigned char* outputPixels, int width, int height, int channels, int levels)
{
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int idx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    levels = clamp(levels, 1, 255);

    float step = 256.0f / (float)levels;

    for (int c = 0; c < colorChannels; c++)
    {
        float v = (float)inputPixels[idx + c];
        if (levels < 255)
        {
            v = (float)((int)(v / step + 0.5f)) * step;
            v = clamp(v, 0.0f, 255.0f);
        }
        outputPixels[idx + c] = (unsigned char)v;
    }

    // Alpha left unchanged.
    if (channels > 3)
    {
        outputPixels[idx + 3] = inputPixels[idx + 3];
    }
}
