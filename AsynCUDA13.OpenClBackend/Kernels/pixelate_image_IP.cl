// In-place (IP, float I/O pointer) pixelation/mosaic effect. Divides image into blocks and fills each with the average color. Channel-agnostic. Linear dispatch: launch with global size = width*height.
__kernel void pixelate_image_IP(__global unsigned char* image, int width, int height, int channels, int blockSize)
{
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int x = pixel % width;
    int y = pixel / width;

    int dstIdx = pixel * channels;
    int colorChannels = channels < 3 ? channels : 3;

    if (blockSize < 1) blockSize = 1;
    if (blockSize < 2) return;

    int bx = x / blockSize;
    int by = y / blockSize;

    int startX = bx * blockSize;
    int startY = by * blockSize;
    int endX = min(startX + blockSize, width);
    int endY = min(startY + blockSize, height);

    int blockArea = (endX - startX) * (endY - startY);

    for (int c = 0; c < colorChannels; c++)
    {
        int sum = 0;
        for (int by2 = startY; by2 < endY; by2++)
        {
            for (int bx2 = startX; bx2 < endX; bx2++)
            {
                int srcIdx = (by2 * width + bx2) * channels + c;
                sum += image[srcIdx];
            }
        }
        image[dstIdx + c] = (unsigned char)min(max(sum / blockArea, 0), 255);
    }

    // Alpha left unchanged.
}
