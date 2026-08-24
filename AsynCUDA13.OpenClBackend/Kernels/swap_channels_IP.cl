// In-place (IP, float I/O pointer) channel swap (RGB -> BGR). Channel-agnostic: only
// swaps when there are at least 3 channels; swaps channel 0 and channel 2 and leaves a
// 4th (alpha) channel untouched. Linear dispatch: launch with global size = width*height.
__kernel void swap_channels_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	if (channels < 3) {
		return;
	}

	int idx = pixel * channels;
	unsigned char tmp = image[idx + 0];
	image[idx + 0] = image[idx + 2];
	image[idx + 2] = tmp;
	// Green and alpha left unchanged.
}
