__global__ void gaussian_blur(unsigned char* input, unsigned char* output, int width, int height, float sigma) {
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    if (x >= width || y >= height) return;

    float sum = 0.0f;
    float weight_sum = 0.0f;
    int radius = 2; // Annahme für 5x5 Kernel

    for (int i = -radius; i <= radius; i++) {
        for (int j = -radius; j <= radius; j++) {
            int nx = min(max(x + i, 0), width - 1);
            int ny = min(max(y + j, 0), height - 1);

            // Einfache Gauß-Gewichtung (vereinfacht für das Beispiel)
            float dist_sq = (float)(i * i + j * j);
            float weight = expf(-dist_sq / (2.0f * sigma * sigma));

            // Graustufen-Wert aus unsigned char Array (0-255)
            float pixel_val = (float)input[ny * width + nx];
            sum += pixel_val * weight;
            weight_sum += weight;
        }
    }

    // Ergebnis in unsigned char Array schreiben
    output[y * width + x] = (unsigned char)(sum / weight_sum);
}
