// In-place (IP, float I/O pointer) 3x3 box blur. Channel-agnostic: blurs the first
// up-to-3 color channels and leaves a 4th (alpha) channel untouched. Linear dispatch:
// launch with global size = width*height. Note: reading and writing the same buffer
// means neighbour reads may observe already-blurred pixels; use the _OOP variant for a
// race-free result.
__kernel void box_blur_image_IP(
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

	int dstIdx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	for (int c = 0; c < colorChannels; c++) {
		int sum = 0;
		int count = 0;
		for (int dy = -1; dy <= 1; dy++) {
			for (int dx = -1; dx <= 1; dx++) {
				int nx = x + dx;
				int ny = y + dy;
				if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
				int nIdx = (ny * width + nx) * channels;
				sum += image[nIdx + c];
				count++;
			}
		}
		image[dstIdx + c] = (unsigned char)(count > 0 ? sum / count : image[dstIdx + c]);
	}
	// Alpha left unchanged.
}
