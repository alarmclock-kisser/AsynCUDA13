extern "C" __global__ void edge_detection_v2(
	const unsigned char* inputPixels,
	unsigned char* outputPixels,
	int width,
	int height,
	int edgeR,
	int edgeG,
	int edgeB,
	int thickness,
	float threshold)
{
	int x = blockIdx.x * blockDim.x + threadIdx.x;
	int y = blockIdx.y * blockDim.y + threadIdx.y;

	if (x >= width || y >= height)
	{
		return;
	}

	const int pixelPos = (y * width + x) * 4;

	// Alle Farbwerte auf 0-255 begrenzen (mit getauschten R/B-Kanälen)
	const unsigned char clampedR = (unsigned char)min(max(edgeR, 0), 255);
	const unsigned char clampedG = (unsigned char)min(max(edgeG, 0), 255);
	const unsigned char clampedB = (unsigned char)min(max(edgeB, 0), 255);
	const int clampedThickness = min(max(thickness, 0), 10);
	const float absThreshold = fabsf(threshold);

	// Weißer Hintergrund
	outputPixels[pixelPos] = 255;
	outputPixels[pixelPos + 1] = 255;
	outputPixels[pixelPos + 2] = 255;
	outputPixels[pixelPos + 3] = 255;

	// Nur Nicht-Randpixel verarbeiten
	if (x >= clampedThickness && x < width - clampedThickness &&
		y >= clampedThickness && y < height - clampedThickness)
	{
		const int sobelX[3][3] = {
			{-1, 0, 1},
			{-2, 0, 2},
			{-1, 0, 1}
		};
		const int sobelY[3][3] = {
			{-1, -2, -1},
			{0, 0, 0},
			{1, 2, 1}
		};

		float3 gradientX = make_float3(0.0f, 0.0f, 0.0f);
		float3 gradientY = make_float3(0.0f, 0.0f, 0.0f);

		// 3x3-Nachbarschaft verarbeiten (mit getauschten Kanälen)
		for (int dy = -1; dy <= 1; dy++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				int neighborPos = ((y + dy) * width + (x + dx)) * 4;

				// R und B für die Gradientenberechnung tauschen
				float3 rgb = make_float3(
					inputPixels[neighborPos + 2] / 255.0f,
					inputPixels[neighborPos + 1] / 255.0f,
					inputPixels[neighborPos] / 255.0f);

				float sobelValueX = (float)sobelX[dy + 1][dx + 1];
				float sobelValueY = (float)sobelY[dy + 1][dx + 1];
				gradientX.x += rgb.x * sobelValueX;
				gradientX.y += rgb.y * sobelValueX;
				gradientX.z += rgb.z * sobelValueX;
				gradientY.x += rgb.x * sobelValueY;
				gradientY.y += rgb.y * sobelValueY;
				gradientY.z += rgb.z * sobelValueY;
			}
		}

		float3 magnitude = make_float3(
			sqrtf(gradientX.x * gradientX.x + gradientY.x * gradientY.x),
			sqrtf(gradientX.y * gradientX.y + gradientY.y * gradientY.y),
			sqrtf(gradientX.z * gradientX.z + gradientY.z * gradientY.z));
		float avgMagnitude = (magnitude.x + magnitude.y + magnitude.z) / 3.0f;

		if (avgMagnitude > absThreshold)
		{
			// Jeder Thread schreibt ausschließlich seinen eigenen Pixel.
			// Nachbarpixel direkt zu überschreiben würde Race Conditions erzeugen.
			outputPixels[pixelPos] = clampedR;
			outputPixels[pixelPos + 1] = clampedG;
			outputPixels[pixelPos + 2] = clampedB;
			outputPixels[pixelPos + 3] = 255;
		}
	}
}
