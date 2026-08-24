// In-place (IP, float I/O pointer) brightness/contrast adjustment. Channel-agnostic.
// Linear dispatch: launch with global size = width*height (one work-item per pixel).
// brightness: additive offset in [-255, 255]; contrast: multiplicative factor.
__kernel void adjust_brightness_contrast_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels,
	int brightness,
	float contrast)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int idx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	for (int c = 0; c < colorChannels; c++) {
		float v = (float)image[idx + c];
		v = (v - 128.0f) * contrast + 128.0f + (float)brightness;
		int iv = (int)v;
		if (iv < 0) iv = 0;
		if (iv > 255) iv = 255;
		image[idx + c] = (unsigned char)iv;
	}
	// Alpha left unchanged.
}
