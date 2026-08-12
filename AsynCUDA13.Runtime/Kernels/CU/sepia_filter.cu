__global__ void sepia_filter(unsigned char* input, unsigned char* output, int width, int height) {
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    if (x >= width || y >= height) return;

    int idx = (y * width + x) * 3;

    // Graustufen-Werte aus RGB extrahieren (einfacher Graustufen-Ansatz)
    float r = input[idx];
    float g = input[idx + 1];
    float b = input[idx + 2];

    // Graustufen-Wert für Sepia-Berechnung
    float gray = 0.299f * r + 0.587f * g + 0.114f * b;

    // Sepia Matrix Koeffizienten (auf Graustufen angewandt)
    float sepia_r = (gray * 0.393f);
    float sepia_g = (gray * 0.349f);
    float sepia_b = (gray * 0.272f);

    // Clamp auf 255
    output[idx] = (unsigned char)fminf(sepia_r, 255.0f);
    output[idx + 1] = (unsigned char)fminf(sepia_g, 255.0f);
    output[idx + 2] = (unsigned char)fminf(sepia_b, 255.0f);
}
