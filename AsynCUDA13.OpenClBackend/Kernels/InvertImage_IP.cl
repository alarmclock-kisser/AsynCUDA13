// In-place (IP, float I/O pointer) color inversion. Channel-agnostic: inverts the first
// up-to-3 color channels and leaves a 4th (alpha) channel untouched. Linear dispatch:
// launch with global size = width*height (one work-item per pixel).
__kernel void InvertImage_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int idx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	for (int c = 0; c < colorChannels; c++) {
		image[idx + c] = 255 - image[idx + c];
	}
	// Alpha left unchanged.
}
