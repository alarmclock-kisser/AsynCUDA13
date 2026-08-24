// Mandelbrot set fractal - out-of-place (OOP, separate input/output pointers). Linear
// dispatch: launch with global size = width*height (one work-item per pixel). The
// width/height/channels parameters are injected by the OpenCL image launcher; the remaining
// scalar arguments (zoom, offsetX, offsetY, iterCoeff, baseR, baseG, baseB) follow. Alpha
// (a 4th channel, if present) is forced fully opaque (255). This generator writes into the
// dedicated output buffer; inputPixels is unused.
inline uchar clamp_channel(int v)
{
	return (uchar)(v < 0 ? 0 : (v > 255 ? 255 : v));
}

__kernel void mandelbrot_OOP(
	__global const unsigned char* inputPixels,
	__global unsigned char* outputPixels,
	int width,
	int height,
	int channels,
	float zoom,
	float offsetX,
	float offsetY,
	int iterCoeff,
	int baseR,
	int baseG,
	int baseB)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int px = pixel % width;
	int py = pixel / width;

	if (iterCoeff < 1) iterCoeff = 1;
	if (iterCoeff > 1000) iterCoeff = 1000;

	int maxIter = 100 + (int)(iterCoeff * log(zoom + 1.0f));

	float x0 = (px - width / 2.0f) / (width / 2.0f) / zoom + offsetX;
	float y0 = (py - height / 2.0f) / (height / 2.0f) / zoom + offsetY;

	float x = 0.0f;
	float y = 0.0f;
	int iter = 0;

	while (x * x + y * y <= 4.0f && iter < maxIter)
	{
		float xtemp = x * x - y * y + x0;
		y = 2.0f * x * y + y0;
		x = xtemp;
		iter++;
	}

	int idx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	if (iter == maxIter)
	{
		if (colorChannels > 0) outputPixels[idx + 0] = clamp_channel(baseR);
		if (colorChannels > 1) outputPixels[idx + 1] = clamp_channel(baseG);
		if (colorChannels > 2) outputPixels[idx + 2] = clamp_channel(baseB);
	}
	else
	{
		float t = (float)iter / (float)maxIter;
		float r = sin(t * 3.14159f) * 255.0f;
		float g = sin(t * 6.28318f + 1.0472f) * 255.0f;
		float b = sin(t * 9.42477f + 2.0944f) * 255.0f;

		if (colorChannels > 0) outputPixels[idx + 0] = clamp_channel(baseR + (int)(r * (1.0f - t)));
		if (colorChannels > 1) outputPixels[idx + 1] = clamp_channel(baseG + (int)(g * (1.0f - t)));
		if (colorChannels > 2) outputPixels[idx + 2] = clamp_channel(baseB + (int)(b * (1.0f - t)));
	}

	if (channels >= 4) {
		outputPixels[idx + 3] = 255;
	}
}
