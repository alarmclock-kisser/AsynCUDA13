// Out-of-place (OOP, separate input/output pointers) channel swap (RGB -> BGR).
// Channel-agnostic: swaps channel 0 and channel 2 when there are at least 3 channels
// and copies the green plus any 4th (alpha) channel through unchanged. Linear dispatch:
// launch with global size = width*height.
__kernel void swap_channels_OOP(
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

	outputPixels[idx + 0] = inputPixels[idx + 2];
	outputPixels[idx + 1] = inputPixels[idx + 1];
	outputPixels[idx + 2] = inputPixels[idx + 0];

	for (int c = 3; c < channels; c++) {
		outputPixels[idx + c] = inputPixels[idx + c];
	}
}
