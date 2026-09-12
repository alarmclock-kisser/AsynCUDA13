// Out-of-place (OOP, separate input/output pointers) mirror tile effect. Divides image into tiles and mirrors top-left quadrant within each tile. Channel-agnostic; copies all channels. Linear dispatch: launch with global size = width*height.
__kernel void mirror_tile_image_OOP(__global const unsigned char* inputPixels, __global unsigned char* outputPixels, int width, int height, int channels, int tileCountX, int tileCountY) {
    int pixel = get_global_id(0);
    if (pixel >= width * height) return;

    int x = pixel % width;
    int y = pixel / width;

    if (tileCountX < 1) tileCountX = 1;
    if (tileCountY < 1) tileCountY = 1;

    int tileW = width / tileCountX;
    int tileH = height / tileCountY;

    int tx = x % tileW;
    int ty = y % tileH;

    int sx = tx < (tileW / 2) ? tx : (tileW - 1 - tx);
    int sy = ty < (tileH / 2) ? ty : (tileH - 1 - ty);

    if (sx >= tileW / 2) sx = (tileW / 2) > 0 ? (tileW / 2) - 1 : 0;
    if (sy >= tileH / 2) sy = (tileH / 2) > 0 ? (tileH / 2) - 1 : 0;

    int srcX = (x / tileW) * tileW + sx;
    int srcY = (y / tileH) * tileH + sy;

    int dstIdx = (y * width + x) * channels;
    int srcIdx = (srcY * width + srcX) * channels;

    for (int c = 0; c < channels; c++) {
        outputPixels[dstIdx + c] = inputPixels[srcIdx + c];
    }
}