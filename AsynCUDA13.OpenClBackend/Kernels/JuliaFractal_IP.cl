// Julia set fractal - in-place (IP, float I/O pointer). Linear dispatch: launch with global
// size = width*height (one work-item per pixel). The width/height/channels parameters are
// injected by the OpenCL image launcher; the remaining scalar arguments (zoom, offsetX,
// offsetY, iterCoeff, juliaReal, juliaImag, baseR, baseG, baseB) follow. c = (juliaReal,
// juliaImag) is the fixed Julia constant. Alpha (a 4th channel, if present) is forced fully
// opaque (255). This generator only writes, so image serves as the output buffer.
inline uchar clamp_channel(int v)
{
	return (uchar)(v < 0 ? 0 : (v > 255 ? 255 : v));
}

__kernel void JuliaFractal_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels,
	float zoom,
	float offsetX,
	float offsetY,
	int iterCoeff,
	float juliaReal,
	float juliaImag,
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

	float zx = (px - width / 2.0f) / (width / 2.0f) / zoom + offsetX;
	float zy = (py - height / 2.0f) / (height / 2.0f) / zoom + offsetY;

	int iter = 0;
	while (zx * zx + zy * zy <= 4.0f && iter < maxIter)
	{
		float xtemp = zx * zx - zy * zy + juliaReal;
		zy = 2.0f * zx * zy + juliaImag;
		zx = xtemp;
		iter++;
	}

	int idx = pixel * channels;
	int colorChannels = channels < 3 ? channels : 3;

	if (iter == maxIter)
	{
		if (colorChannels > 0) image[idx + 0] = clamp_channel(baseR);
		if (colorChannels > 1) image[idx + 1] = clamp_channel(baseG);
		if (colorChannels > 2) image[idx + 2] = clamp_channel(baseB);
	}
	else
	{
		float t = (float)iter / (float)maxIter;
		float r = sin(t * 3.14159f) * 255.0f;
		float g = sin(t * 6.28318f + 1.0472f) * 255.0f;
		float b = sin(t * 9.42477f + 2.0944f) * 255.0f;

		if (colorChannels > 0) image[idx + 0] = clamp_channel(baseR + (int)(r * (1.0f - t)));
		if (colorChannels > 1) image[idx + 1] = clamp_channel(baseG + (int)(g * (1.0f - t)));
		if (colorChannels > 2) image[idx + 2] = clamp_channel(baseB + (int)(b * (1.0f - t)));
	}

	if (channels >= 4) {
		image[idx + 3] = 255;
	}
}
