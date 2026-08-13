extern "C" __global__ void edge_detection_v3(
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

	// Das Eingabebild zunächst unverändert kopieren.
	outputPixels[pixelPos] = inputPixels[pixelPos];
	outputPixels[pixelPos + 1] = inputPixels[pixelPos + 1];
	outputPixels[pixelPos + 2] = inputPixels[pixelPos + 2];
	outputPixels[pixelPos + 3] = inputPixels[pixelPos + 3];

	// Die Farbkanäle wie im OpenCL-Referenzkernel tauschen.
	const unsigned char clampedB = (unsigned char)min(max(edgeR, 0), 255);
	const unsigned char clampedG = (unsigned char)min(max(edgeG, 0), 255);
	const unsigned char clampedR = (unsigned char)min(max(edgeB, 0), 255);
	const int clampedThickness = min(max(thickness, 0), 10);
	const float absThreshold = fabsf(threshold);

	if (x < clampedThickness || x >= width - clampedThickness ||
		y < clampedThickness || y >= height - clampedThickness)
	{
		return;
	}

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

	for (int dy = -1; dy <= 1; dy++)
	{
		const int neighborY = y + dy;
		for (int dx = -1; dx <= 1; dx++)
		{
			const int neighborX = x + dx;
			const int neighborPos = (neighborY * width + neighborX) * 4;

			float3 rgb = make_float3(
				inputPixels[neighborPos + 2] / 255.0f,
				inputPixels[neighborPos + 1] / 255.0f,
				inputPixels[neighborPos] / 255.0f);

			const float sobelValueX = (float)sobelX[dy + 1][dx + 1];
			const float sobelValueY = (float)sobelY[dy + 1][dx + 1];
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
		for (int dy = -clampedThickness; dy <= clampedThickness; dy++)
		{
			const int outputY = y + dy;
			if (outputY < 0 || outputY >= height)
			{
				continue;
			}

			for (int dx = -clampedThickness; dx <= clampedThickness; dx++)
			{
				if (dx * dx + dy * dy > clampedThickness * clampedThickness)
				{
					continue;
				}

				const int outputX = x + dx;
				if (outputX < 0 || outputX >= width)
				{
					continue;
				}

				const int outputPos = (outputY * width + outputX) * 4;
				outputPixels[outputPos] = clampedR;
				outputPixels[outputPos + 1] = clampedG;
				outputPixels[outputPos + 2] = clampedB;
				outputPixels[outputPos + 3] = 255;
			}
		}
	}
}
