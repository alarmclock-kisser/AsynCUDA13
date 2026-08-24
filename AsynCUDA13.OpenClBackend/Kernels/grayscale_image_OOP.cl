// Out-of-place (OOP, separate input/output pointers) grayscale conversion (luminance).
// Channel-agnostic: converts the first three channels and copies any remaining (alpha)
// channel through unchanged. Linear dispatch: launch with global size = width*height.
__kernel void grayscale_image_OOP(
	__global const unsigned char* inputPixels,
	__global unsigned char* outputPixels,
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
		for (int c = 0; c < channels; c++) {
			outputPixels[idx + c] = inputPixels[idx + c];
		}
		return;
	}

	float r = (float)inputPixels[idx + 0];
	float g = (float)inputPixels[idx + 1];
	float b = (float)inputPixels[idx + 2];
	int lum = (int)(0.299f * r + 0.587f * g + 0.114f * b);
	if (lum > 255) lum = 255;

	outputPixels[idx + 0] = (unsigned char)lum;
	outputPixels[idx + 1] = (unsigned char)lum;
	outputPixels[idx + 2] = (unsigned char)lum;

	for (int c = 3; c < channels; c++) {
		outputPixels[idx + c] = inputPixels[idx + c];
	}
}
