// In-place (IP, float I/O pointer) kaleidoscope effect. Mirrors the top-left quadrant of
// the source into the other three quadrants. Channel-agnostic; processes all channels.
// Linear dispatch: launch with global size = width*height. Note: reading and writing the
// same buffer can observe already-written pixels; use the _OOP variant for a race-free result.
__kernel void kaleidoscope_image_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int x = pixel % width;
	int y = pixel / width;

	int halfW = width / 2;
	int halfH = height / 2;

	int sx = x < halfW ? x : (width - 1 - x);
	int sy = y < halfH ? y : (height - 1 - y);

	if (sx >= halfW) sx = halfW > 0 ? halfW - 1 : 0;
	if (sy >= halfH) sy = halfH > 0 ? halfH - 1 : 0;

	int srcIdx = (sy * width + sx) * channels;
	int dstIdx = pixel * channels;

	for (int c = 0; c < channels; c++) {
		image[dstIdx + c] = image[srcIdx + c];
	}
}
