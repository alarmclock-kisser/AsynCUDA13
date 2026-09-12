// In-place (IP, float I/O pointer) radial kaleidoscope. Divides image into mirrored wedge segments around center. Channel-agnostic; processes all channels. Linear dispatch: launch with global size = width*height.
#define M_PI_F 3.14159265358979f
__kernel void radial_kaleidoscope_image_IP(
	__global unsigned char* image,
	int width,
	int height,
	int channels,
	int segments)
{
	int pixel = get_global_id(0);
	if (pixel >= width * height) {
		return;
	}

	int x = pixel % width;
	int y = pixel / width;

	if (segments < 2) {
		segments = 2;
	}

	float cx = (float)width * 0.5f;
	float cy = (float)height * 0.5f;

	float dx = (float)x - cx;
	float dy = (float)y - cy;
	float r = sqrt(dx * dx + dy * dy);
	float angle = atan2(dy, dx);

	float segAngle = 2.0f * M_PI_F / (float)segments;
	angle = fmod(angle + M_PI_F, segAngle);
	if (angle < 0.0f) {
		angle += segAngle;
	}
	if (angle > segAngle * 0.5f) {
		angle = segAngle - angle;
	}

	float sx = cx + r * cos(angle);
	float sy = cy + r * sin(angle);

	int si = (int)floor(sx);
	int sj = (int)floor(sy);

	if (si < 0) {
		si = 0;
	}
	if (si >= width) {
		si = width - 1;
	}
	if (sj < 0) {
		sj = 0;
	}
	if (sj >= height) {
		sj = height - 1;
	}

	int srcIdx = (sj * width + si) * channels;
	int dstIdx = pixel * channels;

	for (int c = 0; c < channels; c++) {
		image[dstIdx + c] = image[srcIdx + c];
	}
}