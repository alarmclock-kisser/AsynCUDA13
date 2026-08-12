__global__ void rotate_bilinear(unsigned char* input, unsigned char* output, int width, int height, float angle_rad) {
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    if (x >= width || y >= height) return;

    // Zentrum des Bildes
    float cx = width / 2.0f;
    float cy = height / 2.0f;

    // Relative Koordinaten zum Zentrum
    float rel_x = x - cx;
    float rel_y = y - cy;

    // Inverse Rotation (um vom Ziel zurück zum Ursprung zu finden)
    float rot_x = rel_x * cosf(-angle_rad) - rel_y * sinf(-angle_rad);
    float rot_y = rel_x * sinf(-angle_rad) + rel_y * cosf(-angle_rad);

    // Absolute Koordinaten im Quellbild
    float src_x = rot_x + cx;
    float src_y = rot_y + cy;

    if (src_x >= 0 && src_x < width - 1 && src_y >= 0 && src_y < height - 1) {
        // Bilineare Interpolation
        int x0 = (int)floorf(src_x);
        int y0 = (int)floorf(src_y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        float dx = src_x - x0;
        float dy = src_y - y0;

        // Graustufen-Werte für bessere Interpolation extrahieren
        int idx00 = (y0 * width + x0) * 3;
        int idx10 = (y0 * width + x1) * 3;
        int idx01 = (y1 * width + x0) * 3;
        int idx11 = (y1 * width + x1) * 3;

        float p00_r = input[idx00];
        float p00_g = input[idx00 + 1];
        float p00_b = input[idx00 + 2];

        float p10_r = input[idx10];
        float p10_g = input[idx10 + 1];
        float p10_b = input[idx10 + 2];

        float p01_r = input[idx01];
        float p01_g = input[idx01 + 1];
        float p01_b = input[idx01 + 2];

        float p11_r = input[idx11];
        float p11_g = input[idx11 + 1];
        float p11_b = input[idx11 + 2];

        int out_idx = (y * width + x) * 3;

        output[out_idx] = (unsigned char)((1 - dx) * (1 - dy) * p00_r + dx * (1 - dy) * p10_r + (1 - dx) * dy * p01_r + dx * dy * p11_r);
        output[out_idx + 1] = (unsigned char)((1 - dx) * (1 - dy) * p00_g + dx * (1 - dy) * p10_g + (1 - dx) * dy * p01_g + dx * dy * p11_g);
        output[out_idx + 2] = (unsigned char)((1 - dx) * (1 - dy) * p00_b + dx * (1 - dy) * p10_b + (1 - dx) * dy * p01_b + dx * dy * p11_b);
    }
    else {
        int out_idx = (y * width + x) * 3;
        output[out_idx] = 0;
        output[out_idx + 1] = 0;
        output[out_idx + 2] = 0; // Schwarzer Rand
    }
}
