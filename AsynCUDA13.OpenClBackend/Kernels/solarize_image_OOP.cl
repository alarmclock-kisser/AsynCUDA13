// Out-of-place (OOP, separate input/output pointers) solarization effect. Inverts pixels above threshold. Channel-agnostic: processes first up-to-3 color channels. Copies a 4th (alpha) channel through unchanged. Linear dispatch: launch with global size = width*height.

__kernel void solarize_image_OOP(__global const unsigned char* inputPixels, __global unsigned char* outputPixels, int width, int height, int channels, int threshold)
{
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int idx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    int thr = clamp(threshold, 0, 255);

    for (int c = 0; c < colorChannels; c++)
    {
        if (inputPixels[idx + c] >= thr)
        {
            outputPixels[idx + c] = 255 - inputPixels[idx + c];
        }
        else
        {
            outputPixels[idx + c] = inputPixels[idx + c];
        }
    }

    // Copy alpha through unchanged.
    if (channels > 3)
    {
        outputPixels[idx + 3] = inputPixels[idx + 3];
    }
}
