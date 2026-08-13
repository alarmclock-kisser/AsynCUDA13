__device__ float edge_gray(const unsigned char* input, int x, int y, int width, int height, int channels)
{
    if (x < 0 || x >= width || y < 0 || y >= height)
    {
        return 0.0f;
    }

    int pixelIdx = (y * width + x) * channels;
    if (channels >= 3)
    {
        return 0.299f * input[pixelIdx] + 0.587f * input[pixelIdx + 1] + 0.114f * input[pixelIdx + 2];
    }

    return input[pixelIdx];
}

__device__ bool is_edge(const unsigned char* input, int x, int y, int width, int height, int channels, float threshold)
{
    const int sobelX[3][3] = {
        {-1, 0, 1},
        {-2, 0, 2},
        {-1, 0, 1}
    };
    const int sobelY[3][3] = {
        {-1, -2, -1},
        { 0,  0,  0},
        { 1,  2,  1}
    };

    float gradientX = 0.0f;
    float gradientY = 0.0f;
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            float gray = edge_gray(input, x + dx, y + dy, width, height, channels);
            gradientX += gray * sobelX[dy + 1][dx + 1];
            gradientY += gray * sobelY[dy + 1][dx + 1];
        }
    }

    float magnitude = sqrtf(gradientX * gradientX + gradientY * gradientY);
    float normalizedMagnitude = magnitude / 1020.0f;
    return normalizedMagnitude >= threshold;
}

extern "C" __global__ void edge_detection(unsigned char* input, unsigned char* output, int width, int height, int channels, int edgeR, int edgeG, int edgeB, int thickness, float threshold)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    if (x >= width || y >= height || channels <= 0)
    {
        return;
    }

    float t = fmaxf(0.0f, fminf(1.0f, threshold));
    int radius = max(0, thickness - 1);
    bool edge = false;

    for (int dy = -radius; dy <= radius && !edge; dy++)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (is_edge(input, x + dx, y + dy, width, height, channels, t))
            {
                edge = true;
                break;
            }
        }
    }

    int pixelIdx = (y * width + x) * channels;
    for (int channel = 0; channel < channels; channel++)
    {
        if (edge && channel < 3)
        {
            int color = channel == 0 ? edgeR : channel == 1 ? edgeG : edgeB;
            output[pixelIdx + channel] = (unsigned char)max(0, min(255, color));
        }
        else
        {
            output[pixelIdx + channel] = input[pixelIdx + channel];
        }
    }
}