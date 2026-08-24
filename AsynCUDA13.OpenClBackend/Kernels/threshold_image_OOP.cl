// Out-of-place (OOP, separate input/output pointers) binary threshold on luminance.
// Channel-agnostic: writes the binary value to the first up-to-3 channels and copies any
// remaining (alpha) channel through unchanged. Linear dispatch: global size = width*height.
__kernel void threshold_image_OOP(
	__global const unsigned char* inputPixels,
	__global unsigned char* outputPixels,
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
		lum = 0.299f * inputPixels[idx + 0] + 0.587f * inputPixels[idx + 1] + 0.114f * inputPixels[idx + 2];
	} else {
		lum = (float)inputPixels[idx];
	}

	unsigned char v = lum >= (float)threshold ? 255 : 0;
	for (int c = 0; c < colorChannels; c++) {
		outputPixels[idx + c] = v;
	}

	for (int c = colorChannels; c < channels; c++) {
		outputPixels[idx + c] = inputPixels[idx + c];
	}
}
