// Out-of-place (OOP, separate input/output pointers) vignette effect. Radial darkening toward edges.
// Channel-agnostic: processes first up-to-3 color channels. Copies a 4th (alpha) channel
// through unchanged. Linear dispatch: launch with global size = width*height.
__kernel void vignette_image_OOP(
	__global const unsigned char* inputPixels,
	__global unsigned char* outputPixels,
	int width,
	int height,
	int channels,
	float radius,
	float intensity)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int x = pixel % width;
	int y = pixel / width;
	int idx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	radius = clamp(radius, 0.01f, 0.99f);
	intensity = clamp(intensity, 0.0f, 1.0f);

	float cx = (float)width * 0.5f;
	float cy = (float)height * 0.5f;
	float diag = sqrt(cx * cx + cy * cy);

	float dx = (float)x - cx;
	float dy = (float)y - cy;
	float d = sqrt(dx * dx + dy * dy);
	float nd = d / diag;

	float factor = 1.0f;
	if (nd > radius) {
		factor = 1.0f - intensity * ((nd - radius) / (1.0f - radius));
		factor = clamp(factor, 0.0f, 1.0f);
	}

	for (int c = 0; c < colorChannels; c++) {
		outputPixels[idx + c] = (unsigned char)((float)inputPixels[idx + c] * factor);
	}

	for (int c = colorChannels; c < channels; c++) {
		outputPixels[idx + c] = inputPixels[idx + c];
	}
}
