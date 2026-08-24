// In-place (IP, float I/O pointer) binary threshold on luminance. Channel-agnostic.
// Linear dispatch: launch with global size = width*height (one work-item per pixel).
__kernel void threshold_image_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels,
	int threshold)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int idx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	float lum;
	if (channels >= 3) {
		lum = 0.299f * image[idx + 0] + 0.587f * image[idx + 1] + 0.114f * image[idx + 2];
	} else {
		lum = (float)image[idx];
	}

	unsigned char v = lum >= (float)threshold ? 255 : 0;
	for (int c = 0; c < colorChannels; c++) {
		image[idx + c] = v;
	}
	// Alpha left unchanged.
}
