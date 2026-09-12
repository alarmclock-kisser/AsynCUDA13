// Out-of-place (OOP, separate input/output pointers) sepia tone filter. Channel-agnostic: processes first up-to-3 color channels. Copies a 4th (alpha) channel through unchanged. Linear dispatch: launch with global size = width*height.
__kernel void sepia_image_OOP(__global const unsigned char* inputPixels, __global unsigned char* outputPixels, int width, int height, int channels, float intensity)
{
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int idx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    intensity = clamp(intensity, 0.0f, 1.0f);

    if (colorChannels == 1)
    {
        float v = (float)inputPixels[idx];
        float sepia = 0.349f * v + 0.686f * v + 0.168f * v;
        if (sepia > 255.0f) sepia = 255.0f;
        float result = v + (sepia - v) * intensity;
        result = clamp(result, 0.0f, 255.0f);
        outputPixels[idx] = (unsigned char)((int)result);
    }
    else
    {
        float r = (float)inputPixels[idx + 0];
        float g = (float)inputPixels[idx + 1];
        float b = (colorChannels >= 3) ? (float)inputPixels[idx + 2] : 0.0f;

        float tr = 0.393f * r + 0.769f * g + 0.189f * b;
        float tg = 0.349f * r + 0.686f * g + 0.168f * b;
        float tb = 0.272f * r + 0.534f * g + 0.131f * b;

        if (tr > 255.0f) tr = 255.0f;
        if (tg > 255.0f) tg = 255.0f;
        if (tb > 255.0f) tb = 255.0f;

        float newR = r + (tr - r) * intensity;
        float newG = g + (tg - g) * intensity;

        newR = clamp(newR, 0.0f, 255.0f);
        newG = clamp(newG, 0.0f, 255.0f);

        outputPixels[idx + 0] = (unsigned char)((int)newR);
        outputPixels[idx + 1] = (unsigned char)((int)newG);

        if (colorChannels >= 3)
        {
            float newB = b + (tb - b) * intensity;
            newB = clamp(newB, 0.0f, 255.0f);
            outputPixels[idx + 2] = (unsigned char)((int)newB);
        }
    }

    for (int c = colorChannels; c < channels; c++)
    {
        outputPixels[idx + c] = inputPixels[idx + c];
    }
}
