// Out-of-place (OOP, separate input/output pointers) color inversion. Channel-agnostic:
// inverts the first up-to-3 color channels and copies any remaining (alpha) channel
// through unchanged. Linear dispatch: launch with global size = width*height.
__kernel void InvertImage_OOP(
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
	int colorChannels = channels < 3 ? channels : 3;

	for (int c = 0; c < colorChannels; c++) {
		outputPixels[idx + c] = 255 - inputPixels[idx + c];
	}

	for (int c = colorChannels; c < channels; c++) {
		outputPixels[idx + c] = inputPixels[idx + c];
	}
}
