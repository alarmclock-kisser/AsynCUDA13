// In-place (IP, float I/O pointer) grayscale conversion (luminance). Channel-agnostic.
// Linear dispatch: launch with global size = width*height (one work-item per pixel).
__kernel void grayscale_image_IP(
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
	if (channels < 3) {
		return;
	}

	float r = (float)image[idx + 0];
	float g = (float)image[idx + 1];
	float b = (float)image[idx + 2];
	int lum = (int)(0.299f * r + 0.587f * g + 0.114f * b);
	if (lum > 255) lum = 255;

	image[idx + 0] = (unsigned char)lum;
	image[idx + 1] = (unsigned char)lum;
	image[idx + 2] = (unsigned char)lum;
	// Alpha left unchanged.
}
