// In-place (IP, float I/O pointer) 5x5 Gaussian blur. Channel-agnostic: blurs the first up-to-3 color channels and leaves a 4th (alpha) channel untouched. Linear dispatch: launch with global size = width*height.

__kernel void gaussian_blur_image_IP(__global unsigned char* image, int width, int height, int channels) {
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int x = pixel % width;
    int y = pixel / width;
    int dstIdx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    for (int c = 0; c < colorChannels; c++) {
        int sum = 0;

        for (int dy = -2; dy <= 2; dy++) {
            for (int dx = -2; dx <= 2; dx++) {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                int srcIdx = (ny * width + nx) * channels + c;
                unsigned char val = image[srcIdx];

                if (dy == -2) {
                    if (dx == -2) sum += val * 1;
                    else if (dx == -1) sum += val * 4;
                    else if (dx == 0) sum += val * 6;
                    else if (dx == 1) sum += val * 4;
                    else if (dx == 2) sum += val * 1;
                } else if (dy == -1) {
                    if (dx == -2) sum += val * 4;
                    else if (dx == -1) sum += val * 16;
                    else if (dx == 0) sum += val * 24;
                    else if (dx == 1) sum += val * 16;
                    else if (dx == 2) sum += val * 4;
                } else if (dy == 0) {
                    if (dx == -2) sum += val * 6;
                    else if (dx == -1) sum += val * 24;
                    else if (dx == 0) sum += val * 36;
                    else if (dx == 1) sum += val * 24;
                    else if (dx == 2) sum += val * 6;
                } else if (dy == 1) {
                    if (dx == -2) sum += val * 4;
                    else if (dx == -1) sum += val * 16;
                    else if (dx == 0) sum += val * 24;
                    else if (dx == 1) sum += val * 16;
                    else if (dx == 2) sum += val * 4;
                } else if (dy == 2) {
                    if (dx == -2) sum += val * 1;
                    else if (dx == -1) sum += val * 4;
                    else if (dx == 0) sum += val * 6;
                    else if (dx == 1) sum += val * 4;
                    else if (dx == 2) sum += val * 1;
                }
            }
        }

        image[dstIdx + c] = (unsigned char)(sum / 256 < 0 ? 0 : (sum / 256 > 255 ? 255 : sum / 256));
    }

    // Alpha left unchanged.
}
