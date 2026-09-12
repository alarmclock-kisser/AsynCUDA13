// Out-of-place (OOP, separate input/output pointers) 3x3 emboss filter. Channel-agnostic: embosses the first up-to-3 color channels and copies a 4th (alpha) channel through unchanged. Linear dispatch: launch with global size = width*height.

__kernel void emboss_image_OOP(__global const unsigned char* inputPixels, __global unsigned char* outputPixels, int width, int height, int channels) {
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int x = pixel % width;
    int y = pixel / width;
    int dstIdx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    for (int c = 0; c < colorChannels; c++) {
        int sum = 0;

        int dx[9] = { -1,  0,  1, -1,  0,  1, -1,  0,  1 };
        int dy[9] = { -1, -1, -1,  0,  0,  0,  1,  1,  1 };
        int wt[9] = { -2, -1,  0, -1,  1,  1,  0,  1,  2 };

        for (int i = 0; i < 9; i++) {
            int nx = x + dx[i];
            int ny = y + dy[i];
            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
            sum += inputPixels[(ny * width + nx) * channels + c] * wt[i];
        }

        sum += 128;
        outputPixels[dstIdx + c] = (unsigned char)(sum < 0 ? 0 : (sum > 255 ? 255 : sum));
    }

    // Copy alpha through unchanged.
    if (channels > 3) {
        outputPixels[dstIdx + 3] = inputPixels[dstIdx + 3];
    }
}