// Out-of-place (OOP, separate input/output pointers) Sobel edge detection. Channel-agnostic
// for the first up-to-3 color channels; a 4th (alpha) channel, if present, is written fully
// opaque (255). Linear dispatch: launch with global size = width*height.
//
// Detected edges are drawn in (edgeR, edgeG, edgeB). "thickness" dilates each edge pixel
// into a square neighbourhood so lines can be made thicker. "threshold" is the normalised
// Sobel gradient magnitude (0..~1) above which a pixel counts as an edge. Non-edge pixels
// are copied through from the input unchanged. There is no background/mode.
inline uchar clamp_channel(int v)
{
	return (uchar)(v < 0 ? 0 : (v > 255 ? 255 : v));
}

__kernel void edge_detection_OOP(
	__global const unsigned char* inputPixels,
	__global unsigned char* outputPixels,
	int width,
	int height,
	int channels,
	int edgeR,
	int edgeG,
	int edgeB,
	int thickness,
	float threshold)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int x = pixel % width;
	int y = pixel / width;

	int dstIdx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	if (thickness < 1) thickness = 1;
	int radius = thickness - 1;

	bool isEdge = false;
	for (int oy = -radius; oy <= radius && !isEdge; oy++) {
		for (int ox = -radius; ox <= radius && !isEdge; ox++) {
			int cx = x + ox;
			int cy = y + oy;
			if (cx < 1 || cx >= width - 1 || cy < 1 || cy >= height - 1) continue;

			float gx = 0.0f;
			float gy = 0.0f;

			for (int ky = -1; ky <= 1; ky++) {
				for (int kx = -1; kx <= 1; kx++) {
					int sIdx = ((cy + ky) * width + (cx + kx)) * channels;
					float lum = 0.0f;
					for (int c = 0; c < colorChannels; c++) {
						lum += (float)inputPixels[sIdx + c];
					}
					lum /= (float)colorChannels;

					int sobelX = kx * (2 - (ky != 0));
					int sobelY = ky * (2 - (kx != 0));
					gx += lum * sobelX;
					gy += lum * sobelY;
				}
			}

			float magnitude = sqrt(gx * gx + gy * gy) / 1442.5f;
			if (magnitude >= threshold) {
				isEdge = true;
			}
		}
	}

	if (isEdge) {
		if (colorChannels >= 1) outputPixels[dstIdx + 0] = clamp_channel(edgeR);
		if (colorChannels >= 2) outputPixels[dstIdx + 1] = clamp_channel(edgeG);
		if (colorChannels >= 3) outputPixels[dstIdx + 2] = clamp_channel(edgeB);
	}
	else {
		for (int c = 0; c < colorChannels; c++) {
			outputPixels[dstIdx + c] = inputPixels[dstIdx + c];
		}
	}

	for (int c = colorChannels; c < channels; c++) {
		outputPixels[dstIdx + c] = 255;
	}
}
